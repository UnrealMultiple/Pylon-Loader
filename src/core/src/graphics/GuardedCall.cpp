// 层：内核层 / graphics —— 受保护调用实现（移植自 Bunny Loader 的 il2cpp/GuardedCall.cpp）
#include "graphics/GuardedCall.h"

#include <cstdint>
#include <cstring>
#include <dlfcn.h>
#include <unwind.h>

#include "il2cpp/Il2CppRuntime.h"

#if defined(__aarch64__)

namespace {

/**
 * 个人风格把异常里要的东西先拷出来：只读词，不调任何东西（它在抛出方展开器的上下文里跑）。
 * 谁读这些值？guardedCall，落地之后。
 */
struct Raw {
    bool cxx;              // C++ 异常（CLNGC++ 或 GNUCC++）
    const void* typeAt88;
    const void* typeAt80;
    void* thrownWord;      // 抛出对象的第一词
};

// trampoline 写在自己帧里的标记（见 GuardedCall.S）：[sp+160] = kMarkMagic ^ (sp+160)，
// [sp+168] = 本调用的 Raw*。
constexpr uint64_t kMarkMagic = 0x626c5f65685f6d6bull;
constexpr uintptr_t kMarkOffset = 160;

// C++ 异常的 exception_class："CLNGC++\0"（libc++abi）与 "GNUCC++\0"（libsupc++）。
constexpr uint64_t kClangCxx = 0x434C4E47432B2B00ull;
constexpr uint64_t kGnuCxx = 0x474E5543432B2B00ull;

/**
 * 从 __cxa_exception 拷出关心的字段（它以 _Unwind_Exception 结尾）。
 * 抛出的对象紧跟在 _Unwind_Exception（32 字节）后面；IL2CPP 抛 Il2CppExceptionWrapper，
 * 它唯一的字段就是 Il2CppException*。
 *
 * `std::type_info* exceptionType` 的位置随 libc++abi 版本变：_Unwind_Exception 按 16 对齐，
 * 旧的（NDK r21 那代，libil2cpp 自带）在它前面留 8 字节填充 → 类型在 -88；
 * 新的把填充挪到开头（reserve 字段）→ 类型在 -80，-88 是 referenceCount。
 * 实测 libil2cpp：-88 是类型、-80 是析构（空）。
 */
void copyRaw(uint64_t cls, _Unwind_Exception* ue, Raw* out) {
    *out = Raw{};
    if (cls != kClangCxx && cls != kGnuCxx) return;
    auto* base = reinterpret_cast<char*>(ue);
    out->cxx = true;
    out->typeAt88 = *reinterpret_cast<void* const*>(base - 88);
    out->typeAt80 = *reinterpret_cast<void* const*>(base - 80);
    out->thrownWord = *reinterpret_cast<void**>(base + sizeof(_Unwind_Exception));
}

/**
 * 从当前帧往上找最内层 trampoline 的标记。
 * 故意不设上限：个人风格只在阶段 2、trampoline 的帧上被调，标记就在这条栈里、在我们上方。
 * 更老的标记不存在：trampoline 走两个出口都会擦掉自己的。
 */
uintptr_t* findMark() {
    auto* p = static_cast<uintptr_t*>(__builtin_frame_address(0));
    while (*p != (kMarkMagic ^ reinterpret_cast<uintptr_t>(p))) ++p;
    return p;
}

/** 指针落进已加载的库里（说明能读）。 */
bool inLoadedModule(const void* p) {
    Dl_info info;
    return p && dladdr(p, &info) != 0 && info.dli_fbase != nullptr;
}

/**
 * 抛出的类型名（mangled）。每个候选只在指向库内时才读：新布局 -88 是计数器（1），
 * 旧布局 -80 是析构（空或代码）。
 */
const char* thrownTypeName(const Raw& r) {
    for (const void* type : {r.typeAt88, r.typeAt80}) {
        if (!inLoadedModule(type)) continue;
        // std::type_info：{ vtable, const char* name }。
        const char* name = reinterpret_cast<const char* const*>(type)[1];
        if (inLoadedModule(name)) return name;
    }
    return nullptr;
}

}  // namespace

extern "C" {

int bl_eh_try(void (*body)(void*), void* arg, void* raw);
[[noreturn]] void bl_eh_land(uintptr_t sp);

/**
 * trampoline 的个人风格。由抛出方的展开器调用，带的是**它**的上下文——所以这里从不读上下文。
 */
__attribute__((visibility("hidden"))) _Unwind_Reason_Code
bl_eh_personality(int version, _Unwind_Action actions, uint64_t cls, _Unwind_Exception* ue,
                  _Unwind_Context*) {
    if (version != 1 || !ue) return _URC_FATAL_PHASE1_ERROR;
    // 阶段 1：处理者是 trampoline。还不动任何东西：throw 与它之间的帧在阶段 2 跑清理。
    if (actions & _UA_SEARCH_PHASE) return _URC_HANDLER_FOUND;
    // 强制展开（bionic 上没有，但协议要求）：放行。
    if ((actions & _UA_FORCE_UNWIND) || !(actions & _UA_HANDLER_FRAME)) {
        return _URC_CONTINUE_UNWIND;
    }
    uintptr_t* mark = findMark();
    copyRaw(cls, ue, reinterpret_cast<Raw*>(mark[1]));
    // 像接一个外来异常一样：由它的主人释放。C# 异常指针已经拷到栈上，收集器看得见。
    // （libil2cpp 的 std::uncaught_exceptions 计数会多 1：只有它自己的 __cxa_begin_catch 扣。）
    if (ue->exception_cleanup) ue->exception_cleanup(_URC_FOREIGN_EXCEPTION_CAUGHT, ue);
    // trampoline 下方帧的清理在阶段 2 已经跑完；剩的是纯栈。像 trampoline 返回 1 一样跳回。
    bl_eh_land(reinterpret_cast<uintptr_t>(mark) - kMarkOffset);
}

}  // extern "C"

namespace pylon::il2cpp {

bool guardedCall(void (*body)(void*), void* arg, Caught* caught) {
    Raw raw;  // 只有个人风格写，而且只在抛了的时候
    if (!bl_eh_try(body, arg, &raw)) {
        if (caught) *caught = Caught{};
        return true;
    }
    if (caught) {
        *caught = Caught{};
        caught->threw = true;
        const char* name = raw.cxx ? thrownTypeName(raw) : nullptr;
        if (name && std::strstr(name, "Il2CppExceptionWrapper")) {
            caught->exception = static_cast<Il2CppObject*>(raw.thrownWord);
        } else {
            caught->cppType = name;
        }
    }
    return false;
}

}  // namespace pylon::il2cpp

#else  // !__aarch64__

namespace pylon::il2cpp {
bool guardedCall(void (*body)(void*), void* arg, Caught* caught) {
    body(arg);
    if (caught) *caught = Caught{};
    return true;
}
}  // namespace pylon::il2cpp

#endif

namespace pylon::il2cpp {

std::string describeException(Il2CppObject* exception) {
    if (!exception) return "exception object missing";
    auto& a = il2cpp::Runtime::Instance().api();
    if (a.format_exception) {
        char buf[512];
        buf[0] = 0;
        a.format_exception(exception, buf, sizeof(buf));
        buf[sizeof(buf) - 1] = 0;
        if (buf[0]) return buf;
    }
    Il2CppClass* cls = a.object_get_class ? a.object_get_class(exception) : nullptr;
    if (!cls) return "C# exception";
    std::string ns = a.class_get_namespace ? a.class_get_namespace(cls) : "";
    return (ns.empty() ? "" : ns + ".") + a.class_get_name(cls);
}

std::string describeCaught(const Caught& c) {
    if (c.exception) return describeException(c.exception);
    if (c.cppType) return std::string("C++ exception ") + c.cppType;
    return "unknown exception type";
}

}  // namespace pylon::il2cpp
