// 层：内核层 / il2cpp —— C# 方法绑定与调用（实现）
#include "il2cpp/Call.h"

#include <cstdio>
#include <cstring>
#include <unistd.h>

#include "pylon/Log.h"

namespace pylon::il2cpp {
namespace {

/// 托管字符串布局：对象头 + 长度 + UTF-16 数据。**不靠猜**：自检里 `String.Concat`
/// 必须原样读回 `pylon-binding-ok`，布局错了就读到乱码（见 boot 的自检日志）。
struct Il2CppStringLayout {
    void* klass;
    void* monitor;
    int32_t length;
    char16_t chars[1];
};

/// UTF-16（ASCII/基本多文种面足够）→ UTF-8。控制字符换成 '.'，日志不会被搞乱。
std::string Utf16ToUtf8(const char16_t* chars, int32_t length) {
    std::string out;
    out.reserve(static_cast<size_t>(length));
    for (int32_t i = 0; i < length; ++i) {
        const uint32_t code = chars[i];
        if (code < 0x20) {
            out.push_back('.');
        } else if (code < 0x80) {
            out.push_back(static_cast<char>(code));
        } else if (code < 0x800) {
            out.push_back(static_cast<char>(0xC0 | (code >> 6)));
            out.push_back(static_cast<char>(0x80 | (code & 0x3F)));
        } else {
            // 3 字节形式；代理对（emoji 等）会显示成 U+FFFD，M3 用不到，需要时再补。
            out.push_back(static_cast<char>(0xE0 | (code >> 12)));
            out.push_back(static_cast<char>(0x80 | ((code >> 6) & 0x3F)));
            out.push_back(static_cast<char>(0x80 | (code & 0x3F)));
        }
    }
    return out;
}

std::string ReadManagedString(void* pointer) {
    if (pointer == nullptr) return "(null)";
    const auto* str = static_cast<const Il2CppStringLayout*>(pointer);
    if (str->length < 0 || str->length > 4096) {
        // 布局不对或字符串异常大：宁可报出来，也不要按错误长度读内存。
        return "(长度异常：" + std::to_string(str->length) + "，字符串布局可能不匹配)";
    }
    return Utf16ToUtf8(str->chars, str->length);
}

ValueKind KindFromTypeName(const std::string& name) {
    if (name.empty()) return ValueKind::Unknown;
    if (name == "System.Void") return ValueKind::Void;
    if (name == "System.Boolean") return ValueKind::Boolean;
    if (name == "System.Int32") return ValueKind::Int32;
    if (name == "System.Int64") return ValueKind::Int64;
    if (name == "System.Single") return ValueKind::Single;
    if (name == "System.Double") return ValueKind::Double;
    if (name == "System.String") return ValueKind::String;
    // 其余都当引用处理（对象/数组/委托/自带类型…）：值就是指针。
    return name.find('*') != std::string::npos ? ValueKind::Unknown : ValueKind::Reference;
}

/// 值类型返回在 runtime_invoke 里被装箱了：解箱拿到数据指针，再按类型读。
void ReadValueFromBox(void* boxData, ValueKind kind, Value* out) {
    if (boxData == nullptr) {
        out->text = "(解箱失败)";
        return;
    }
    char buffer[64];
    switch (kind) {
        case ValueKind::Boolean: {
            const bool v = *static_cast<const bool*>(boxData);
            out->raw = v ? 1 : 0;
            out->text = v ? "true" : "false";
            break;
        }
        case ValueKind::Int32: {
            const int32_t v = *static_cast<const int32_t*>(boxData);
            out->raw = v;
            out->text = std::to_string(v);
            break;
        }
        case ValueKind::Int64: {
            const int64_t v = *static_cast<const int64_t*>(boxData);
            out->raw = v;
            out->text = std::to_string(v);
            break;
        }
        case ValueKind::Single: {
            const float v = *static_cast<const float*>(boxData);
            std::memcpy(&out->raw, &v, sizeof(float));
            std::snprintf(buffer, sizeof(buffer), "%g", static_cast<double>(v));
            out->text = buffer;
            break;
        }
        case ValueKind::Double: {
            const double v = *static_cast<const double*>(boxData);
            std::memcpy(&out->raw, &v, sizeof(double));
            std::snprintf(buffer, sizeof(buffer), "%g", v);
            out->text = buffer;
            break;
        }
        default:
            std::snprintf(buffer, sizeof(buffer), "%p", boxData);
            out->text = buffer;
            break;
    }
}

std::string DescribeExceptionText(void* exception) {
    auto& api = Runtime::Instance().api();
    if (exception == nullptr) return {};
    if (api.format_exception != nullptr) {
        char buffer[512] = {0};
        api.format_exception(static_cast<Il2CppObject*>(exception), buffer, sizeof(buffer) - 1);
        if (buffer[0] != '\0') return buffer;
    }
    return "托管异常（il2cpp_format_exception 不可用，只有对象指针）";
}

}  // namespace

const char* ValueKindName(ValueKind kind) {
    switch (kind) {
        case ValueKind::Void: return "void";
        case ValueKind::Boolean: return "bool";
        case ValueKind::Int32: return "int32";
        case ValueKind::Int64: return "int64";
        case ValueKind::Single: return "float";
        case ValueKind::Double: return "double";
        case ValueKind::String: return "string";
        case ValueKind::Reference: return "reference";
        case ValueKind::Unknown: return "unknown";
    }
    return "?";
}

void CallArgs::PushReference(const void* pointer) {
    storage_.emplace_back(sizeof(void*));
    std::memcpy(storage_.back().data(), &pointer, sizeof(void*));
    pointers_.push_back(storage_.back().data());
}

void CallArgs::PushInt32(int32_t value) {
    storage_.emplace_back(sizeof(int32_t));
    std::memcpy(storage_.back().data(), &value, sizeof(int32_t));
    pointers_.push_back(storage_.back().data());
}

void CallArgs::PushBoolean(bool value) {
    storage_.emplace_back(sizeof(bool));
    std::memcpy(storage_.back().data(), &value, sizeof(bool));
    pointers_.push_back(storage_.back().data());
}

void CallArgs::PushSingle(float value) {
    storage_.emplace_back(sizeof(float));
    std::memcpy(storage_.back().data(), &value, sizeof(float));
    pointers_.push_back(storage_.back().data());
}

void CallArgs::PushRaw(const void* data, size_t size) {
    storage_.emplace_back(size == 0 ? 1 : size);
    if (data != nullptr && size > 0) std::memcpy(storage_.back().data(), data, size);
    pointers_.push_back(storage_.back().data());
}

std::string CallResult::Describe() const {
    if (!invoked) return "未调用：" + error;
    std::string text = "返回 " + std::string(ValueKindName(value.kind));
    if (!value.text.empty()) text += " " + value.text;
    if (threw) text += "（本次调用抛了托管异常：" + exception + "）";
    return text;
}

namespace {

/// 取一个类型的名字（`il2cpp_type_get_name` 给的是 malloc 出来的串，用完要还）。
std::string TypeNameOf(const Il2CppType* type) {
    auto& api = Runtime::Instance().api();
    if (type == nullptr || api.type_get_name == nullptr) return {};
    char* raw = api.type_get_name(type);
    if (raw == nullptr) return {};
    std::string name = raw;
    if (api.il2cpp_free != nullptr) api.il2cpp_free(raw);
    return name;
}

/// 枚举类（含父类链）里所有同名同参数个数的候选，连同参数类型名——据此判"唯一/歧义/精确匹配"。
struct Candidate {
    const MethodInfo* info = nullptr;
    std::vector<std::string> param_types;
    std::string return_type;
};

std::vector<Candidate> EnumerateCandidates(Il2CppClass* klass, const std::string& name, int argc) {
    std::vector<Candidate> found;
    auto& api = Runtime::Instance().api();
    if (api.class_get_methods == nullptr || api.method_get_param == nullptr ||
        api.method_get_param_count == nullptr) {
        return found;  // 缺可选符号：交给调用方走 class_get_method_from_name 的旧路
    }

    for (Il2CppClass* current = klass; current != nullptr; current = api.class_get_parent(current)) {
        void* iterator = nullptr;
        while (const MethodInfo* method = api.class_get_methods(current, &iterator)) {
            const char* method_name = api.method_get_name(method);
            if (method_name == nullptr || name != method_name) continue;
            const uint32_t count = api.method_get_param_count(method);
            if (argc >= 0 && static_cast<int>(count) != argc) continue;

            Candidate candidate;
            candidate.info = method;
            candidate.param_types.reserve(count);
            for (uint32_t i = 0; i < count; ++i) {
                candidate.param_types.push_back(TypeNameOf(api.method_get_param(method, i)));
            }
            if (api.method_get_return_type != nullptr) {
                candidate.return_type = TypeNameOf(api.method_get_return_type(method));
            }
            found.push_back(std::move(candidate));
        }
    }
    return found;
}

std::string JoinTypes(const std::vector<std::string>& types) {
    std::string text = "(";
    for (size_t i = 0; i < types.size(); ++i) {
        if (i != 0) text += ", ";
        text += types[i].empty() ? "?" : types[i];
    }
    return text + ")";
}

}  // namespace

Outcome<MethodRef> MethodRef::Resolve(const std::string& qualified_type, const std::string& name,
                                      int argc) {
    Query query;
    query.type = qualified_type;
    query.name = name;
    query.argc = argc;
    return Resolve(query);
}

Outcome<MethodRef> MethodRef::Resolve(const Query& query) {
    auto& runtime = Runtime::Instance();
    if (!runtime.AttachedOnThisThread()) {
        return Outcome<MethodRef>::Bad(
                "当前线程未 attach 到 IL2CPP 域（先调 Runtime::Attach()；attach 是按线程的）");
    }

    const size_t dot = query.type.rfind('.');
    const std::string namespaze = dot == std::string::npos ? "" : query.type.substr(0, dot);
    const std::string class_name = dot == std::string::npos ? query.type
                                                           : query.type.substr(dot + 1);
    auto klass = runtime.FindClass(namespaze, class_name);
    if (!klass) return Outcome<MethodRef>::Bad(klass.reason);

    // 先枚举同名（+同个数）的候选：这是"重载歧义"和"按类型精确匹配"的共同基础。
    std::vector<std::string> param_types = query.param_types;
    int argc = query.argc;
    if (!param_types.empty()) argc = static_cast<int>(param_types.size());

    const auto candidates = EnumerateCandidates(klass.value, query.name, argc);

    const MethodInfo* chosen = nullptr;

    if (!param_types.empty()) {
        // 精确匹配：逐参数比类型名。
        for (const auto& candidate : candidates) {
            if (candidate.param_types.size() != param_types.size()) continue;
            bool same = true;
            for (size_t i = 0; i < param_types.size(); ++i) {
                if (candidate.param_types[i] != param_types[i]) {
                    same = false;
                    break;
                }
            }
            if (same) {
                chosen = candidate.info;
                break;
            }
        }
        if (chosen == nullptr) {
            std::string available;
            for (const auto& candidate : candidates) {
                if (!available.empty()) available += "; ";
                available += candidate.param_types.empty() ? "(无参数)" : JoinTypes(candidate.param_types);
            }
            return Outcome<MethodRef>::Bad(
                    "按类型找不到 " + query.type + "::" + query.name + JoinTypes(param_types) +
                    (available.empty() ? "（该类里没有同名方法）" : "；现有同名重载：" + available));
        }
    } else if (argc >= 0) {
        // 只给了个数：只有一个候选才敢用；多个就拒绝（这正是 String.Concat 踩的坑）。
        if (candidates.size() > 1) {
            std::string available;
            for (const auto& candidate : candidates) {
                if (!available.empty()) available += "; ";
                available += candidate.param_types.empty() ? "(无参数)" : JoinTypes(candidate.param_types);
            }
            return Outcome<MethodRef>::Bad(
                    "重载歧义：" + query.type + "::" + query.name + "/" + std::to_string(argc) +
                    " 有 " + std::to_string(candidates.size()) + " 个候选，请写参数类型：现有 " +
                    available);
        }
        if (candidates.size() == 1) {
            chosen = candidates.front().info;
        }
    } else {
        // 连个数都没给：按 0..8 探（有副作用的东西不能这么用，只给诊断/清单用）。
        for (int probe = 0; probe <= 8 && chosen == nullptr; ++probe) {
            if (const auto found = runtime.FindMethod(klass.value, query.name, probe); found) {
                chosen = found.value;
                argc = probe;
            }
        }
    }

    if (chosen == nullptr) {
        // 退路：枚举不可用（缺可选符号）时按"名字 + 个数"直接取，不判歧义但比不工作好。
        if (candidates.empty() && argc >= 0) {
            if (const auto found = runtime.FindMethod(klass.value, query.name, argc); found) {
                chosen = found.value;
                LOGW("il2cpp", "无法枚举重载（可选符号缺失），%s/%d 直接按名字取",
                     query.name.c_str(), argc);
            }
        }
    }
    if (chosen == nullptr) {
        return Outcome<MethodRef>::Bad("找不到方法 " + query.type + "::" + query.name +
                                       (argc >= 0 ? "/" + std::to_string(argc) : ""));
    }

    auto& api = runtime.api();
    MethodRef ref;
    ref.info_ = chosen;
    ref.argc_ = argc >= 0 ? argc : static_cast<int>(api.method_get_param_count(chosen));

    if (api.method_is_instance != nullptr) {
        ref.is_static_ = !api.method_is_instance(ref.info_);
    } else {
        // 没有 method_is_instance 时不敢猜：标成"非实例"、要求显式给 self，宁错也不把静态当实例传野指针。
        ref.is_static_ = false;
        LOGW("il2cpp", "method_is_instance 不可用，%s 的静态性未知（按实例方法处理）",
             query.name.c_str());
    }

    if (api.method_get_return_type != nullptr) {
        const Il2CppType* return_type = api.method_get_return_type(ref.info_);
        if (return_type != nullptr && api.type_get_name != nullptr) {
            char* type_name = api.type_get_name(return_type);
            if (type_name != nullptr) {
                ref.return_type_name_ = type_name;
                if (api.il2cpp_free != nullptr) api.il2cpp_free(type_name);
            }
        }
    }
    return Outcome<MethodRef>::Good(std::move(ref));
}

std::string MethodRef::Describe() const {
    std::string text = "argc=" + std::to_string(argc_);
    text += is_static_ ? " static" : " instance";
    text += " 返回=" + (return_type_name_.empty() ? "(未知类型)" : return_type_name_);
    return text;
}

CallResult MethodRef::Invoke(void* self, CallArgs& args) const {
    return InvokeImpl(self, args.data(), static_cast<int>(args.size()));
}

CallResult MethodRef::InvokeRaw(void* self, void** argv, int argc) const {
    return InvokeImpl(self, argv, argc);
}

CallResult MethodRef::InvokeImpl(void* self, void** params, int argc) const {
    CallResult result;
    auto& api = Runtime::Instance().api();

    if (info_ == nullptr) {
        result.error = "MethodInfo 为空";
        return result;
    }
    if (api.runtime_invoke == nullptr) {
        // runtime_invoke 缺失时**不退回直接调用**：那会把托管异常变成 C++ 异常穿过我们的帧（见文件头），宁可报错。
        result.error = "il2cpp_runtime_invoke 不可用（可选符号缺失），拒绝调用";
        return result;
    }
    if (!is_static_ && self == nullptr) {
        result.error = "实例方法但 self 为空";
        return result;
    }
    if (argc != argc_) {
        result.error = "参数个数不符：给了 " + std::to_string(argc) + "，需要 " +
                       std::to_string(argc_);
        return result;
    }
    // 托管方法只能在游戏线程上调：别处调 runtime_invoke 实测会让 :game SIGSEGV/SIGBUS
    // 反复重启（exit-info: APP CRASH(NATIVE) status=11/7）；上游同样只在主线程派发，
    // 这里宁可返回一句可读原因。
    auto& runtime = Runtime::Instance();
    if (!runtime.IsGameThread()) {
        result.error = "必须在游戏线程（UnityMain）上调用托管方法；当前 tid=" +
                       std::to_string(static_cast<unsigned long>(gettid())) +
                       "，已登记的游戏线程 tid=" + std::to_string(runtime.GameThreadId()) +
                       "。用 UnityPlayer.queueGLThreadEvent(Runnable) 投递过去再调。";
        return result;
    }

    void* exception = nullptr;
    Il2CppObject* returned = api.runtime_invoke(
            info_, is_static_ ? nullptr : self, params,
            reinterpret_cast<Il2CppObject**>(&exception));

    result.invoked = true;
    result.value.kind = KindFromTypeName(return_type_name_);

    if (exception != nullptr) {
        result.threw = true;
        result.exception = DescribeExceptionText(exception);
        return result;
    }

    switch (result.value.kind) {
        case ValueKind::Void:
            result.value.text = "(void)";
            break;
        case ValueKind::String:
            result.value.raw = reinterpret_cast<int64_t>(returned);
            result.value.text = ReadManagedString(returned);
            break;
        case ValueKind::Reference:
            result.value.raw = reinterpret_cast<int64_t>(returned);
            {
                char buffer[64];
                std::snprintf(buffer, sizeof(buffer), "%p", static_cast<void*>(returned));
                result.value.text = buffer;
            }
            break;
        case ValueKind::Unknown:
            // 类型名拿不到（可选符号缺失）时不敢解箱，只报指针。
            result.value.raw = reinterpret_cast<int64_t>(returned);
            {
                char buffer[64];
                std::snprintf(buffer, sizeof(buffer), "%p（类型未知，未解箱）",
                              static_cast<void*>(returned));
                result.value.text = buffer;
            }
            break;
        default:
            // 值类型：runtime_invoke 返回的是装箱对象，解箱后按类型读。
            if (returned != nullptr && api.object_unbox != nullptr) {
                ReadValueFromBox(api.object_unbox(returned), result.value.kind, &result.value);
            } else {
                result.value.raw = reinterpret_cast<int64_t>(returned);
                result.value.text = api.object_unbox == nullptr
                                            ? "(il2cpp_object_unbox 不可用，未解箱)"
                                            : "(返回空)";
            }
            break;
    }
    return result;
}

CallResult InvokeStatic0(const std::string& qualified_type, const std::string& name) {
    auto resolved = MethodRef::Resolve(qualified_type, name, 0);
    if (!resolved) {
        CallResult result;
        result.error = resolved.reason;
        return result;
    }
    if (!resolved.value.is_static()) {
        CallResult result;
        result.error = "目标是实例方法，InvokeStatic0 不能调";
        return result;
    }
    CallArgs args;
    return resolved.value.Invoke(nullptr, args);
}

CallResult InvokeInstance1(const std::string& qualified_type, const std::string& name, void* self,
                           void* arg) {
    auto resolved = MethodRef::Resolve(qualified_type, name, 1);
    if (!resolved) {
        CallResult result;
        result.error = resolved.reason;
        return result;
    }
    CallArgs args;
    args.PushReference(arg);
    return resolved.value.Invoke(self, args);
}

}  // namespace pylon::il2cpp
