// 层：内核层 / mod —— 导出 ABI 的实现（声明与纪律见 include/pylon/mod/ModApi.h）
#include "pylon/mod/ModApi.h"

#include <cstdio>
#include <cstring>
#include <unistd.h>
#include <optional>
#include <string>

#include "il2cpp/Domain.h"
#include "il2cpp/Invoke.h"
#include "mod/LimitPatch.h"
#include "pylon/Log.h"

using namespace pylon;  // NOLINT(google-build-using-namespace) 导出层，写法短一点更好读

namespace {

/// 线程内错误缓冲：`PylonMod_LastError()` 的指针须稳定到下次调用为止，不能用会被释放的 c_str()。
thread_local char g_last_error[512] = {0};
thread_local std::string g_exception_text;

void SetError(const std::string& text) {
    std::snprintf(g_last_error, sizeof(g_last_error), "%s", text.c_str());
}

void ClearError() { g_last_error[0] = '\0'; }

/// 托管字符串长度上限：与 il2cpp/Call.cpp 保持一致，避免拿着可疑指针连续读。
constexpr int32_t kMaxManagedStringLength = 4096;

/// UTF-16 码元 → UTF-8 字节数。
int Utf8Length(char16_t code) {
    if (code < 0x80) return 1;
    if (code < 0x800) return 2;
    return 3;
}

/// UTF-16 码元 → UTF-8 字节（不处理代理对：BMP 之外会各自编码成 3 字节）。
int EncodeUtf8(char16_t code, char out[4]) {
    if (code < 0x80) {
        out[0] = static_cast<char>(code);
        return 1;
    }
    if (code < 0x800) {
        out[0] = static_cast<char>(0xC0 | (code >> 6));
        out[1] = static_cast<char>(0x80 | (code & 0x3F));
        return 2;
    }
    out[0] = static_cast<char>(0xE0 | (code >> 12));
    out[1] = static_cast<char>(0x80 | ((code >> 6) & 0x3F));
    out[2] = static_cast<char>(0x80 | (code & 0x3F));
    return 3;
}

// 每次入口都 attach：按线程生效，新线程上跳过就会 SIGSEGV。
std::optional<il2cpp::Session> Enter() {
    auto attached = il2cpp::Domain::Attach();
    if (!attached) {
        SetError("未 attach 到 IL2CPP 域：" + attached.reason);
        return std::nullopt;
    }
    return attached.value;
}

/// 解析 `命名空间.类型`，失败时把原因写进 LastError。
il2cpp::Il2CppClass* RequireType(const il2cpp::Session& session, const char* qualified_name) {
    if (qualified_name == nullptr || *qualified_name == '\0') {
        SetError("类型名为空");
        return nullptr;
    }
    const std::string full(qualified_name);
    const size_t dot = full.rfind('.');
    auto found = session.FindClass(dot == std::string::npos ? "" : full.substr(0, dot),
                                   dot == std::string::npos ? full : full.substr(dot + 1));
    if (!found) {
        SetError(found.reason);
        return nullptr;
    }
    return found.value;
}

// 宽度确认不是可选项：il2cpp_field_get_value 按字段真实类型写入，缓冲小了就是静默栈破坏。
bool PrepareField(const il2cpp::Session& session, const char* type, const char* field, il2cpp::Field* out) {
    il2cpp::Il2CppClass* klass = RequireType(session, type);
    if (klass == nullptr) return false;

    auto resolved = il2cpp::Field::Resolve(session, klass, field != nullptr ? field : "");
    if (!resolved) {
        SetError(resolved.reason + "（类 " + session.ClassName(klass) + "）");
        return false;
    }
    if (resolved.value.Size() == 0) {
        SetError(std::string("无法确定字段宽度：") + type + "::" + (field != nullptr ? field : "") + "（拒绝读写）");
        return false;
    }
    *out = resolved.value;
    return true;
}

bool GetFieldRaw(void* instance, const char* type, const char* field, void* out, size_t size) {
    const auto session = Enter();
    if (!session) return false;

    il2cpp::Field found;
    if (!PrepareField(*session, type, field, &found)) return false;
    if (found.Size() != size) {
        SetError("字段 " + std::string(type) + "::" + field + " 是 " + std::to_string(found.Size()) +
                 " 字节，调用方按 " + std::to_string(size) + " 字节读（已拒绝）");
        return false;
    }
    if (Status read = found.Read(instance, out); !read) {
        SetError(read.reason);
        return false;
    }
    ClearError();
    return true;
}

bool SetFieldRaw(void* instance, const char* type, const char* field, const void* value) {
    const auto session = Enter();
    if (!session) return false;

    il2cpp::Field found;
    if (!PrepareField(*session, type, field, &found)) return false;
    if (Status written = found.Write(instance, value); !written) {
        SetError(written.reason);
        return false;
    }
    ClearError();
    return true;
}

/// 方法调用的共同路径：解析 → InvokeRaw → 记录异常状态。
il2cpp::CallResult CallMethod(void* instance, const char* type, const char* method, int32_t argc,
                              void** argv, bool* is_static_out) {
    ClearError();
    g_exception_text.clear();

    il2cpp::CallResult result;
    if (type == nullptr || method == nullptr) {
        SetError("类型名或方法名为空");
        result.error = g_last_error;
        return result;
    }
    if (argc < 0) {
        SetError("参数个数不能为负");
        result.error = g_last_error;
        return result;
    }
    const auto session = Enter();
    if (!session) {
        result.error = g_last_error;
        return result;
    }

    // 参数个数由调用方给死（Mod ABI 不做 0..8 探测：调用是有副作用的，不能试）。
    auto resolved = il2cpp::Method::Resolve(*session, type, method, argc);
    if (!resolved) {
        SetError(resolved.reason);
        result.error = resolved.reason;
        return result;
    }
    if (is_static_out != nullptr) *is_static_out = resolved.value.IsStatic();

    result = resolved.value.InvokeRaw(*session, instance, argv, argc);
    if (!result.invoked) {
        SetError(result.error);
    } else if (result.threw) {
        g_exception_text = result.exception;
    }
    return result;
}

}  // namespace

// ---------------------------------------------------------------------------
// 版本 / 日志 / 错误
// ---------------------------------------------------------------------------

PYLON_MOD_EXPORT int32_t PylonMod_ApiVersion() {
    return (mod::kApiVersionMajor << 16) | mod::kApiVersionMinor;
}

PYLON_MOD_EXPORT void PylonMod_Log(const char* utf8) {
    LOGI("mod", "%s", utf8 != nullptr ? utf8 : "(null)");
}

PYLON_MOD_EXPORT const char* PylonMod_LastError() { return g_last_error; }

// ---------------------------------------------------------------------------
// 按名解析
// ---------------------------------------------------------------------------

PYLON_MOD_EXPORT void* PylonMod_FindType(const char* qualified_name) {
    ClearError();
    const auto session = Enter();
    if (!session) return nullptr;
    return RequireType(*session, qualified_name);
}

// ---------------------------------------------------------------------------
// 按名解析：方法的原生代码指针
// ---------------------------------------------------------------------------

PYLON_MOD_EXPORT void* PylonMod_MethodPointer(const char* qualified_name, const char* method,
                                              int32_t argc) {
    ClearError();
    if (qualified_name == nullptr || method == nullptr || *method == '\0') {
        SetError("类型名或方法名为空");
        return nullptr;
    }
    if (argc < 0) {
        SetError("参数个数不能为负");
        return nullptr;
    }

    // 与 PylonMod_Invoke* 同一纪律：按名解析前必须先 attach（当前线程）。
    const auto session = Enter();
    if (!session) return nullptr;

    auto resolved = il2cpp::Method::Resolve(*session, qualified_name, method, argc);
    if (!resolved) {
        SetError(resolved.reason);
        return nullptr;
    }
    const il2cpp::MethodInfo* info = resolved.value.info();
    if (info == nullptr) {
        SetError("解析到的 MethodInfo 为空");
        return nullptr;
    }

    // IL2CPP 的 MethodInfo 头部第一个指针宽字段就是 methodPointer（代码地址，ShadowHook 要 patch 它）。
    void* method_pointer = nullptr;
    std::memcpy(&method_pointer, info, sizeof(method_pointer));
    if (method_pointer == nullptr) {
        SetError("methodPointer 为空（抽象/泛型/接口方法没有实现体）");
        return nullptr;
    }
    return method_pointer;
}

// ---------------------------------------------------------------------------
// 编译期类型上限：改 ARM64 的 cmp 立即数（见 mod/LimitPatch.cpp）
// ---------------------------------------------------------------------------

PYLON_MOD_EXPORT int32_t PylonMod_PatchCompareLimit(const char* qualifiedName, const char* method,
                                                    int32_t argc, uint32_t oldLimit,
                                                    uint32_t newLimit, int32_t belowToo,
                                                    int32_t shifted) {
    ClearError();
    if (qualifiedName == nullptr || method == nullptr || *method == '\0') {
        SetError("类型名或方法名为空");
        return -1;
    }
    if (argc < 0) {
        SetError("参数个数不能为负");
        return -1;
    }
    if (oldLimit > 0xFFFu || newLimit > 0xFFFu) {
        SetError("上限放不进 cmp 的 12 位立即数");
        return -1;
    }

    const auto session = Enter();
    if (!session) return -1;

    auto resolved = il2cpp::Method::Resolve(*session, qualifiedName, method, argc);
    if (!resolved) {
        SetError(resolved.reason);
        return -1;
    }

    const auto patched = pylon::mod::PatchCompareLimit(resolved.value.info(), oldLimit, newLimit,
                                           belowToo != 0, shifted != 0);
    if (patched == 0) {
        SetError("没有找到可改的 cmp #" + std::to_string(oldLimit) + "（方法 " +
                 resolved.value.Describe() + "）");
        return 0;
    }
    return patched;
}

// ---------------------------------------------------------------------------
// 字段：实例
// ---------------------------------------------------------------------------

PYLON_MOD_EXPORT int32_t PylonMod_GetInt32Field(void* instance, const char* type,
                                                const char* field, int32_t* out) {
    if (out == nullptr) {
        SetError("out 为空");
        return 0;
    }
    return GetFieldRaw(instance, type, field, out, sizeof(int32_t)) ? 1 : 0;
}

PYLON_MOD_EXPORT int32_t PylonMod_SetInt32Field(void* instance, const char* type,
                                                const char* field, int32_t value) {
    return SetFieldRaw(instance, type, field, &value) ? 1 : 0;
}

PYLON_MOD_EXPORT int32_t PylonMod_GetSingleField(void* instance, const char* type,
                                                 const char* field, float* out) {
    if (out == nullptr) {
        SetError("out 为空");
        return 0;
    }
    return GetFieldRaw(instance, type, field, out, sizeof(float)) ? 1 : 0;
}

PYLON_MOD_EXPORT int32_t PylonMod_SetSingleField(void* instance, const char* type,
                                                 const char* field, float value) {
    return SetFieldRaw(instance, type, field, &value) ? 1 : 0;
}

PYLON_MOD_EXPORT int32_t PylonMod_GetBooleanField(void* instance, const char* type,
                                                  const char* field, int32_t* out) {
    if (out == nullptr) {
        SetError("out 为空");
        return 0;
    }
    // C# 的 bool 在托管内存里是 1 字节，跨 ABI 用 int32 表达更好用。
    uint8_t raw = 0;
    if (!GetFieldRaw(instance, type, field, &raw, sizeof(raw))) return 0;
    *out = raw != 0 ? 1 : 0;
    return 1;
}

PYLON_MOD_EXPORT int32_t PylonMod_SetBooleanField(void* instance, const char* type,
                                                  const char* field, int32_t value) {
    const uint8_t raw = value != 0 ? 1 : 0;
    return SetFieldRaw(instance, type, field, &raw) ? 1 : 0;
}

PYLON_MOD_EXPORT void* PylonMod_GetObjectField(void* instance, const char* type,
                                               const char* field) {
    void* value = nullptr;
    if (!GetFieldRaw(instance, type, field, &value, sizeof(void*))) return nullptr;
    return value;
}

PYLON_MOD_EXPORT int32_t PylonMod_SetObjectField(void* instance, const char* type,
                                                 const char* field, void* value) {
    return SetFieldRaw(instance, type, field, &value) ? 1 : 0;
}

// ---------------------------------------------------------------------------
// 线程纪律（Mod 必须知道的一件事）
// ---------------------------------------------------------------------------

PYLON_MOD_EXPORT int32_t PylonMod_IsGameThread() {
    return il2cpp::Domain::OnGameThread() ? 1 : 0;
}

PYLON_MOD_EXPORT int64_t PylonMod_GameThreadId() {
    return static_cast<int64_t>(il2cpp::Domain::GameThreadId());
}

PYLON_MOD_EXPORT int64_t PylonMod_CurrentThreadId() {
    return static_cast<int64_t>(gettid());
}

// ---------------------------------------------------------------------------
// 字段：静态
// ---------------------------------------------------------------------------

PYLON_MOD_EXPORT int32_t PylonMod_GetStaticInt32(const char* type, const char* field,
                                                 int32_t* out) {
    if (out == nullptr) {
        SetError("out 为空");
        return 0;
    }
    return GetFieldRaw(nullptr, type, field, out, sizeof(int32_t)) ? 1 : 0;
}

PYLON_MOD_EXPORT int32_t PylonMod_SetStaticInt32(const char* type, const char* field,
                                                 int32_t value) {
    return SetFieldRaw(nullptr, type, field, &value) ? 1 : 0;
}

PYLON_MOD_EXPORT int32_t PylonMod_GetStaticSingle(const char* type, const char* field,
                                                  float* out) {
    if (out == nullptr) {
        SetError("out 为空");
        return 0;
    }
    return GetFieldRaw(nullptr, type, field, out, sizeof(float)) ? 1 : 0;
}

PYLON_MOD_EXPORT int32_t PylonMod_GetStaticBoolean(const char* type, const char* field,
                                                   int32_t* out) {
    if (out == nullptr) {
        SetError("out 为空");
        return 0;
    }
    uint8_t raw = 0;
    if (!GetFieldRaw(nullptr, type, field, &raw, sizeof(raw))) return 0;
    *out = raw != 0 ? 1 : 0;
    return 1;
}

PYLON_MOD_EXPORT void* PylonMod_GetStaticObject(const char* type, const char* field) {
    void* value = nullptr;
    if (!GetFieldRaw(nullptr, type, field, &value, sizeof(void*))) return nullptr;
    return value;
}

// ---------------------------------------------------------------------------
// 方法调用
// ---------------------------------------------------------------------------

PYLON_MOD_EXPORT void* PylonMod_InvokeStatic(const char* type, const char* method,
                                             int32_t argc, void** argv) {
    bool is_static = false;
    const auto result = CallMethod(nullptr, type, method, argc, argv, &is_static);
    if (!result.invoked) return nullptr;
    if (!is_static) {
        SetError("目标是实例方法，请用 PylonMod_InvokeInstance");
        return nullptr;
    }
    return reinterpret_cast<void*>(result.value.raw);
}

PYLON_MOD_EXPORT void* PylonMod_InvokeInstance(void* instance, const char* type,
                                               const char* method, int32_t argc, void** argv) {
    bool is_static = false;
    const auto result = CallMethod(instance, type, method, argc, argv, &is_static);
    if (!result.invoked) return nullptr;
    if (is_static) {
        SetError("目标是静态方法，请用 PylonMod_InvokeStatic");
        return nullptr;
    }
    return reinterpret_cast<void*>(result.value.raw);
}

PYLON_MOD_EXPORT int32_t PylonMod_InvokeStaticInt32(const char* type, const char* method,
                                                    int32_t argc, void** argv, int32_t* out) {
    if (out == nullptr) {
        SetError("out 为空");
        return 0;
    }
    const auto result = CallMethod(nullptr, type, method, argc, argv, nullptr);
    if (!result.invoked || result.threw) {
        if (result.threw) SetError("调用抛托管异常：" + result.exception);
        return 0;
    }
    if (result.value.kind != il2cpp::ValueKind::Int32) {
        SetError(std::string("返回类型不是 Int32，而是 ") + il2cpp::ValueKindName(result.value.kind));
        return 0;
    }
    *out = static_cast<int32_t>(result.value.raw);
    return 1;
}

PYLON_MOD_EXPORT int32_t PylonMod_InvokeInstanceInt32(void* instance, const char* type,
                                                      const char* method, int32_t argc,
                                                      void** argv, int32_t* out) {
    if (out == nullptr) {
        SetError("out 为空");
        return 0;
    }
    const auto result = CallMethod(instance, type, method, argc, argv, nullptr);
    if (!result.invoked || result.threw) {
        if (result.threw) SetError("调用抛托管异常：" + result.exception);
        return 0;
    }
    if (result.value.kind != il2cpp::ValueKind::Int32) {
        SetError(std::string("返回类型不是 Int32，而是 ") + il2cpp::ValueKindName(result.value.kind));
        return 0;
    }
    *out = static_cast<int32_t>(result.value.raw);
    return 1;
}

PYLON_MOD_EXPORT int32_t PylonMod_InvokeStaticSingle(const char* type, const char* method,
                                                     int32_t argc, void** argv, float* out) {
    if (out == nullptr) {
        SetError("out 为空");
        return 0;
    }
    const auto result = CallMethod(nullptr, type, method, argc, argv, nullptr);
    if (!result.invoked || result.threw) {
        if (result.threw) SetError("调用抛托管异常：" + result.exception);
        return 0;
    }
    if (result.value.kind != il2cpp::ValueKind::Single) {
        SetError(std::string("返回类型不是 Single，而是 ") + il2cpp::ValueKindName(result.value.kind));
        return 0;
    }
    float value = 0.0f;
    std::memcpy(&value, &result.value.raw, sizeof(float));
    *out = value;
    return 1;
}

PYLON_MOD_EXPORT int32_t PylonMod_InvokeInstanceSingle(void* instance, const char* type,
                                                       const char* method, int32_t argc,
                                                       void** argv, float* out) {
    if (out == nullptr) {
        SetError("out 为空");
        return 0;
    }
    const auto result = CallMethod(instance, type, method, argc, argv, nullptr);
    if (!result.invoked || result.threw) {
        if (result.threw) SetError("调用抛托管异常：" + result.exception);
        return 0;
    }
    if (result.value.kind != il2cpp::ValueKind::Single) {
        SetError(std::string("返回类型不是 Single，而是 ") + il2cpp::ValueKindName(result.value.kind));
        return 0;
    }
    float value = 0.0f;
    std::memcpy(&value, &result.value.raw, sizeof(float));
    *out = value;
    return 1;
}

PYLON_MOD_EXPORT int32_t PylonMod_HadException() { return g_exception_text.empty() ? 0 : 1; }

PYLON_MOD_EXPORT const char* PylonMod_ExceptionText() { return g_exception_text.c_str(); }

// ---------------------------------------------------------------------------
// 托管字符串
// ---------------------------------------------------------------------------

PYLON_MOD_EXPORT void* PylonMod_NewString(const char* utf8) {
    ClearError();
    if (utf8 == nullptr) {
        SetError("字符串为空");
        return nullptr;
    }
    const il2cpp::Symbols& symbols = il2cpp::Domain::symbols();
    if (symbols.string_new == nullptr) {
        SetError("il2cpp_string_new 不可用（可选符号缺失）");
        return nullptr;
    }
    const auto session = Enter();
    if (!session) return nullptr;
    // 造托管字符串就是碰托管堆，纪律与"调方法/读写实例字段"相同。
    if (Status ready = il2cpp::Domain::RequireGameThread("创建托管字符串"); !ready) {
        SetError(ready.reason);
        return nullptr;
    }
    return symbols.string_new(utf8);
}

PYLON_MOD_EXPORT int32_t PylonMod_ReadString(void* managed_string, char* out, int32_t out_size) {
    ClearError();
    if (managed_string == nullptr) {
        SetError("托管字符串为空");
        return -1;
    }

    const auto session = Enter();
    if (!session) return -1;
    if (Status ready = il2cpp::Domain::RequireGameThread("读托管字符串"); !ready) {
        SetError(ready.reason);
        return -1;
    }

    const il2cpp::Symbols& symbols = il2cpp::Domain::symbols();
    // 先确认拿到的**确实是** System.String：否则按字符串布局读任意对象的字段就是越界读。
    if (symbols.object_get_class != nullptr && symbols.class_get_name != nullptr) {
        il2cpp::Il2CppClass* klass =
                symbols.object_get_class(static_cast<il2cpp::Il2CppObject*>(managed_string));
        const char* name = klass != nullptr ? symbols.class_get_name(klass) : nullptr;
        if (name == nullptr || std::strcmp(name, "String") != 0) {
            SetError(std::string("不是托管字符串（实际类 ") + (name != nullptr ? name : "?") + "）");
            return -1;
        }
    }

    // 与 il2cpp/Call.cpp 读返回值用同一套布局（上限也一致，避免拿着野指针一直读）。
    struct StringLayout {
        void* klass;
        void* monitor;
        int32_t length;
        char16_t chars[1];
    };
    const auto* str = static_cast<const StringLayout*>(managed_string);
    if (str->length < 0 || str->length > kMaxManagedStringLength) {
        SetError("字符串长度异常：" + std::to_string(str->length) + "（布局可能不匹配）");
        return -1;
    }

    // 先算出 UTF-8 总长；缓冲不够时**不写半截**，只回所需长度，调用方按它重开缓冲重试。
    int32_t needed = 0;
    for (int32_t i = 0; i < str->length; ++i) needed += Utf8Length(str->chars[i]);

    if (out == nullptr || out_size <= 0) return needed + 1;
    if (out_size < needed + 1) return needed + 1;

    int32_t written = 0;
    for (int32_t i = 0; i < str->length; ++i) {
        char utf8[4] = {0};
        const int bytes = EncodeUtf8(str->chars[i], utf8);
        std::memcpy(out + written, utf8, static_cast<size_t>(bytes));
        written += bytes;
    }
    out[written] = '\0';
    return written;
}

