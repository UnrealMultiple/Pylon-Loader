// 层：内核层 / il2cpp —— 异常边界实现（aarch64 上靠自定义 personality + 汇编 trampoline）
#include "il2cpp/Guard.h"

#include <cstring>

#include "il2cpp/Domain.h"

#if defined(__aarch64__)

#include <dlfcn.h>
#include <unwind.h>

#include <cstdint>

namespace {

// 只读词，不调任何东西：它在抛出方展开器的上下文里跑。
struct Raw {
    bool cxx;
    const void* typeAt88;
    const void* typeAt80;
    void* thrownWord;
};

// trampoline 写在自己帧上（见 Guard.S）：[sp+160] = magic ^ (sp+160)，[sp+168] = Raw*。
constexpr uint64_t kMarkMagic = 0x626c5f65685f6d6bull;
constexpr uintptr_t kMarkOffset = 160;

constexpr uint64_t kClangCxx = 0x434C4E47432B2B00ull;  // "CLNGC++\0"
constexpr uint64_t kGnuCxx = 0x474E5543432B2B00ull;    // "GNUCC++\0"

// __cxa_exception 以 _Unwind_Exception 结尾，抛出对象紧跟其后；IL2CPP 抛的 Il2CppExceptionWrapper
// 唯一字段就是 Il2CppException*。exceptionType 的位置随 libc++abi 版本变：libil2cpp 自带的是
// 旧布局（-88 类型、-80 空），新布局把填充挪到开头（-88 引用计数、-80 类型），所以两个都留着。
void copyRaw(uint64_t cls, _Unwind_Exception* ue, Raw* out) {
    *out = Raw{};
    if (cls != kClangCxx && cls != kGnuCxx) return;
    auto* base = reinterpret_cast<char*>(ue);
    out->cxx = true;
    out->typeAt88 = *reinterpret_cast<void* const*>(base - 88);
    out->typeAt80 = *reinterpret_cast<void* const*>(base - 80);
    out->thrownWord = *reinterpret_cast<void**>(base + sizeof(_Unwind_Exception));
}

// 从当前帧往上找最内层 trampoline 的标记：只在阶段 2、且只在 trampoline 的帧上被调，
// 标记一定在这条栈上（两个出口都会擦掉自己的）。
uintptr_t* findMark() {
    auto* p = static_cast<uintptr_t*>(__builtin_frame_address(0));
    while (*p != (kMarkMagic ^ reinterpret_cast<uintptr_t>(p))) ++p;
    return p;
}

bool inLoadedModule(const void* p) {
    Dl_info info;
    return p != nullptr && dladdr(p, &info) != 0 && info.dli_fbase != nullptr;
}

// std::type_info = { vtable, const char* name }；两个候选都只在指向库内时才读。
const char* thrownTypeName(const Raw& raw) {
    for (const void* type : {raw.typeAt88, raw.typeAt80}) {
        if (!inLoadedModule(type)) continue;
        const char* name = reinterpret_cast<const char* const*>(type)[1];
        if (inLoadedModule(name)) return name;
    }
    return nullptr;
}

}  // namespace

extern "C" {

int pylon_eh_try(void (*body)(void*), void* arg, void* raw);
[[noreturn]] void pylon_eh_land(uintptr_t sp);

// 由抛出方的展开器调用，带的是**它**的上下文：这里从不读上下文。
__attribute__((visibility("hidden"))) _Unwind_Reason_Code
pylon_eh_personality(int version, _Unwind_Action actions, uint64_t cls, _Unwind_Exception* ue,
                     _Unwind_Context*) {
    if (version != 1 || ue == nullptr) return _URC_FATAL_PHASE1_ERROR;
    // 阶段 1 只声明"这里有处理者"，不动任何东西：throw 与 trampoline 之间的帧要在阶段 2 跑清理。
    if (actions & _UA_SEARCH_PHASE) return _URC_HANDLER_FOUND;
    if ((actions & _UA_FORCE_UNWIND) || !(actions & _UA_HANDLER_FRAME)) return _URC_CONTINUE_UNWIND;

    uintptr_t* mark = findMark();
    copyRaw(cls, ue, reinterpret_cast<Raw*>(mark[1]));
    // 当外来异常交给主人释放；C# 异常指针已拷到栈上，收集器看得见。
    if (ue->exception_cleanup) ue->exception_cleanup(_URC_FOREIGN_EXCEPTION_CAUGHT, ue);
    // trampoline 下方帧的清理已在阶段 2 跑完，剩下纯栈：跳回去。
    pylon_eh_land(reinterpret_cast<uintptr_t>(mark) - kMarkOffset);
}

}  // extern "C"

namespace pylon::il2cpp {

bool Guard::Run(void (*body)(void*), void* arg, Caught* caught) {
    Raw raw;
    if (!pylon_eh_try(body, arg, &raw)) {
        if (caught != nullptr) *caught = Caught{};
        return true;
    }
    if (caught != nullptr) {
        *caught = Caught{};
        caught->threw = true;
        const char* name = raw.cxx ? thrownTypeName(raw) : nullptr;
        if (name != nullptr && std::strstr(name, "Il2CppExceptionWrapper") != nullptr) {
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

bool Guard::Run(void (*body)(void*), void* arg, Caught* caught) {
    body(arg);
    if (caught != nullptr) *caught = Caught{};
    return true;
}

}  // namespace pylon::il2cpp

#endif

namespace pylon::il2cpp {

std::string DescribeException(Il2CppObject* exception) {
    if (exception == nullptr) return "exception object missing";
    const Symbols& symbols = Domain::symbols();
    if (symbols.format_exception != nullptr) {
        char buffer[512] = {0};
        symbols.format_exception(exception, buffer, sizeof(buffer) - 1);
        if (buffer[0] != '\0') return buffer;
    }
    Il2CppClass* klass = symbols.object_get_class != nullptr ? symbols.object_get_class(exception) : nullptr;
    if (klass == nullptr) return "C# exception";
    const char* namespaze = symbols.class_get_namespace(klass);
    return (namespaze != nullptr && *namespaze != '\0' ? std::string(namespaze) + "." : std::string()) +
           symbols.class_get_name(klass);
}

std::string Describe(const Caught& caught) {
    if (caught.exception != nullptr) return DescribeException(caught.exception);
    if (caught.cppType != nullptr) return std::string("C++ exception ") + caught.cppType;
    return "unknown exception type";
}

}  // namespace pylon::il2cpp
