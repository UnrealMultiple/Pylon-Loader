// 层：内核层 / il2cpp —— 调用实现
#include "il2cpp/Invoke.h"

#include <cstdio>
#include <cstring>

#include "il2cpp/Domain.h"
#include "il2cpp/Guard.h"
#include "pylon/Log.h"

namespace pylon::il2cpp {

namespace {

// 托管字符串：对象头 + 长度 + UTF-16。自检里 Concat 原样读回，布局错了立刻暴露。
struct Il2CppStringLayout {
    void* klass;
    void* monitor;
    int32_t length;
    char16_t chars[1];
};

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
            out.push_back(static_cast<char>(0xE0 | (code >> 12)));
            out.push_back(static_cast<char>(0x80 | ((code >> 6) & 0x3F)));
            out.push_back(static_cast<char>(0x80 | (code & 0x3F)));
        }
    }
    return out;
}

std::string ReadManagedString(const void* pointer) {
    if (pointer == nullptr) return "(null)";
    const auto* str = static_cast<const Il2CppStringLayout*>(pointer);
    if (str->length < 0 || str->length > 4096) {
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
    if (name.find('*') != std::string::npos) return ValueKind::Unknown;  // 指针类型名：不解箱
    return ValueKind::Reference;
}

// 值类型返回被装箱了：解箱拿数据指针再按类型读。
void ReadFromBox(void* boxData, ValueKind kind, Value* out) {
    if (boxData == nullptr) {
        out->text = "(解箱失败)";
        return;
    }
    char buffer[64];
    switch (kind) {
        case ValueKind::Boolean: {
            const bool value = *static_cast<const bool*>(boxData);
            out->raw = value ? 1 : 0;
            out->text = value ? "true" : "false";
            break;
        }
        case ValueKind::Int32: {
            const int32_t value = *static_cast<const int32_t*>(boxData);
            out->raw = value;
            out->text = std::to_string(value);
            break;
        }
        case ValueKind::Int64: {
            const int64_t value = *static_cast<const int64_t*>(boxData);
            out->raw = value;
            out->text = std::to_string(value);
            break;
        }
        case ValueKind::Single: {
            const float value = *static_cast<const float*>(boxData);
            std::memcpy(&out->raw, &value, sizeof(float));
            std::snprintf(buffer, sizeof(buffer), "%g", static_cast<double>(value));
            out->text = buffer;
            break;
        }
        case ValueKind::Double: {
            const double value = *static_cast<const double*>(boxData);
            std::memcpy(&out->raw, &value, sizeof(double));
            std::snprintf(buffer, sizeof(buffer), "%g", value);
            out->text = buffer;
            break;
        }
        default:
            std::snprintf(buffer, sizeof(buffer), "%p", boxData);
            out->text = buffer;
            break;
    }
}

// il2cpp_type_get_name 的串是 malloc 出来的，用完要还。
std::string TypeName(const Symbols& symbols, const Il2CppType* type) {
    if (type == nullptr || symbols.type_get_name == nullptr) return {};
    char* raw = symbols.type_get_name(type);
    if (raw == nullptr) return {};
    std::string name(raw);
    if (symbols.free != nullptr) symbols.free(raw);
    return name;
}

// 同名同个数的候选，连同参数类型名：据此判"唯一 / 歧义 / 精确匹配"。
struct Candidate {
    const MethodInfo* info = nullptr;
    std::vector<std::string> param_types;
    std::string return_type;
};

std::string JoinTypes(const std::vector<std::string>& types) {
    std::string text = "(";
    for (size_t i = 0; i < types.size(); ++i) {
        if (i != 0) text += ", ";
        text += types[i].empty() ? "?" : types[i];
    }
    return text + ")";
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

Outcome<Method> Method::Resolve(const Session& session, std::string_view type, std::string_view name,
                                int argc) {
    Query query;
    query.type = std::string(type);
    query.name = std::string(name);
    query.argc = argc;
    return Resolve(session, query);
}

Outcome<Method> Method::Resolve(const Session& session, const Query& query) {
    const Symbols& symbols = session.raw();
    if (symbols.class_get_methods == nullptr || symbols.method_get_param == nullptr) {
        return Outcome<Method>::Bad("无法枚举方法：il2cpp_class_get_methods/il2cpp_method_get_param 不可用");
    }

    const size_t dot = query.type.rfind('.');
    const std::string namespaze = dot == std::string::npos ? "" : query.type.substr(0, dot);
    const std::string className = dot == std::string::npos ? query.type : query.type.substr(dot + 1);

    auto klass = session.FindClass(namespaze, className);
    if (!klass) return Outcome<Method>::Bad(klass.reason);

    std::vector<std::string> paramTypes = query.param_types;
    int argc = query.argc;
    if (!paramTypes.empty()) argc = static_cast<int>(paramTypes.size());

    std::vector<Candidate> candidates;
    for (Il2CppClass* current = klass.value; current != nullptr; current = symbols.class_get_parent(current)) {
        void* iterator = nullptr;
        while (const MethodInfo* method = symbols.class_get_methods(current, &iterator)) {
            const char* methodName = symbols.method_get_name(method);
            if (methodName == nullptr || query.name != methodName) continue;
            const uint32_t count = symbols.method_get_param_count(method);
            if (argc >= 0 && static_cast<int>(count) != argc) continue;

            Candidate candidate;
            candidate.info = method;
            candidate.param_types.reserve(count);
            for (uint32_t i = 0; i < count; ++i) {
                candidate.param_types.push_back(TypeName(symbols, symbols.method_get_param(method, i)));
            }
            if (symbols.method_get_return_type != nullptr) {
                candidate.return_type = TypeName(symbols, symbols.method_get_return_type(method));
            }
            candidates.push_back(std::move(candidate));
        }
    }

    const MethodInfo* chosen = nullptr;
    if (!paramTypes.empty()) {
        for (const Candidate& candidate : candidates) {
            if (candidate.param_types == paramTypes) {
                chosen = candidate.info;
                break;
            }
        }
        if (chosen == nullptr) {
            std::string available;
            for (const Candidate& candidate : candidates) {
                if (!available.empty()) available += "; ";
                available += candidate.param_types.empty() ? "(无参数)" : JoinTypes(candidate.param_types);
            }
            return Outcome<Method>::Bad("按类型找不到 " + query.type + "::" + query.name + JoinTypes(paramTypes) +
                                        (available.empty() ? "（该类里没有同名方法）" : "；现有同名重载：" + available));
        }
    } else if (argc >= 0) {
        if (candidates.size() > 1) {
            std::string available;
            for (const Candidate& candidate : candidates) {
                if (!available.empty()) available += "; ";
                available += candidate.param_types.empty() ? "(无参数)" : JoinTypes(candidate.param_types);
            }
            return Outcome<Method>::Bad("重载歧义：" + query.type + "::" + query.name + "/" + std::to_string(argc) +
                                        " 有 " + std::to_string(candidates.size()) + " 个候选，请写参数类型：现有 " +
                                        available);
        }
        if (candidates.size() == 1) chosen = candidates.front().info;
    } else {
        // 没给个数：找"该个数只有一个候选"的那一档；多候选的档跳过（有副作用的方法不能这样用）。
        for (int probe = 0; probe <= kMaxProbeArity && chosen == nullptr; ++probe) {
            const MethodInfo* found = nullptr;
            int matches = 0;
            for (const Candidate& candidate : candidates) {
                if (static_cast<int>(candidate.param_types.size()) != probe) continue;
                found = candidate.info;
                ++matches;
            }
            if (matches == 1) {
                chosen = found;
                argc = probe;
            }
        }
    }

    if (chosen == nullptr) {
        return Outcome<Method>::Bad("找不到方法 " + query.type + "::" + query.name +
                                    (argc >= 0 ? "/" + std::to_string(argc) : ""));
    }

    Method method;
    method.symbols_ = &symbols;
    method.info_ = chosen;
    method.argc_ = argc >= 0 ? argc : static_cast<int>(symbols.method_get_param_count(chosen));
    if (symbols.method_is_instance != nullptr) {
        method.isStatic_ = !symbols.method_is_instance(chosen);
    } else {
        // 不敢猜静态：标成实例，调用方必须显式给 self。
        method.isStatic_ = false;
        LOGW("il2cpp", "method_is_instance 不可用，%s 的静态性未知（按实例方法处理）", query.name.c_str());
    }
    if (symbols.method_get_return_type != nullptr) {
        method.returnTypeName_ = TypeName(symbols, symbols.method_get_return_type(chosen));
    }
    return Outcome<Method>::Good(std::move(method));
}

std::string Method::Describe() const {
    return "argc=" + std::to_string(argc_) + (isStatic_ ? " static" : " instance") + " 返回=" +
           (returnTypeName_.empty() ? "(未知类型)" : returnTypeName_);
}

CallResult Method::Invoke(const Session& session, void* self, CallArgs& args) const {
    return InvokeImpl(session, self, args.data(), static_cast<int>(args.size()));
}

CallResult Method::InvokeRaw(const Session& session, void* self, void** argv, int argc) const {
    return InvokeImpl(session, self, argv, argc);
}

CallResult Method::InvokeImpl(const Session& session, void* self, void** params, int argc) const {
    CallResult result;
    const Symbols& symbols = *symbols_;

    if (info_ == nullptr) {
        result.error = "MethodInfo 为空";
        return result;
    }
    if (symbols.runtime_invoke == nullptr) {
        // 不退回 methodPointer：那会把托管异常变成 C++ 异常穿过我们的帧。
        result.error = "il2cpp_runtime_invoke 不可用（可选符号缺失），拒绝调用";
        return result;
    }
    if (!isStatic_ && self == nullptr) {
        result.error = "实例方法但 self 为空";
        return result;
    }
    if (argc != argc_) {
        result.error = "参数个数不符：给了 " + std::to_string(argc) + "，需要 " + std::to_string(argc_);
        return result;
    }
    // 别处调 runtime_invoke 实测会让 :game SIGSEGV/SIGBUS 反复重启。
    if (Status ready = Domain::RequireGameThread("调用托管方法"); !ready) {
        result.error = ready.reason + "。用 UnityPlayer.queueGLThreadEvent(Runnable) 投递过去再调。";
        return result;
    }

    void* exception = nullptr;
    Il2CppObject* returned = symbols.runtime_invoke(info_, isStatic_ ? nullptr : self, params,
                                                    reinterpret_cast<Il2CppObject**>(&exception));

    result.invoked = true;
    result.value.kind = KindFromTypeName(returnTypeName_);
    if (exception != nullptr) {
        result.threw = true;
        result.exception = DescribeException(static_cast<Il2CppObject*>(exception));
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
        case ValueKind::Unknown: {
            result.value.raw = reinterpret_cast<int64_t>(returned);
            char buffer[64];
            std::snprintf(buffer, sizeof(buffer), "%p", static_cast<void*>(returned));
            result.value.text = buffer;
            break;
        }
        default:
            if (returned != nullptr && symbols.object_unbox != nullptr) {
                ReadFromBox(symbols.object_unbox(returned), result.value.kind, &result.value);
            } else {
                result.value.raw = reinterpret_cast<int64_t>(returned);
                result.value.text = symbols.object_unbox == nullptr ? "(il2cpp_object_unbox 不可用，未解箱)"
                                                                    : "(返回空)";
            }
            break;
    }
    return result;
}

}  // namespace pylon::il2cpp
