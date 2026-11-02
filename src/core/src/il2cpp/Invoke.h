// 层：内核层 / il2cpp —— 值、参数与方法调用
//
// 只走 il2cpp_runtime_invoke，绝不用 methodPointer：C# 异常是真的 C++ 异常，由 libil2cpp 自带的
// libgcc 展开器抛出，与我们的 LLVM libunwind 同栈相遇直接崩进程。
#pragma once

#include <cstddef>
#include <cstdint>
#include <deque>
#include <string>
#include <string_view>
#include <vector>

#include "il2cpp/Symbols.h"
#include "pylon/Status.h"

namespace pylon::il2cpp {

class Session;

enum class ValueKind {
    Void,
    Boolean,
    Int32,
    Int64,
    Single,
    Double,
    String,
    Reference,
    Unknown,
};

const char* ValueKindName(ValueKind kind);

struct Value {
    ValueKind kind = ValueKind::Unknown;
    int64_t raw = 0;
    std::string text;
};

/// runtime_invoke 要的是"指向每个实参的指针"数组，deque 保证这些地址在调用期间不动。
class CallArgs {
public:
    void PushReference(const void* pointer);
    void PushInt32(int32_t value);
    void PushBoolean(bool value);
    void PushSingle(float value);
    void PushRaw(const void* data, size_t size);

    size_t size() const { return pointers_.size(); }
    void** data() { return pointers_.empty() ? nullptr : pointers_.data(); }

private:
    std::deque<std::vector<uint8_t>> storage_;
    std::vector<void*> pointers_;
};

struct CallResult {
    bool invoked = false;
    bool threw = false;
    std::string exception;
    std::string error;
    Value value;

    std::string Describe() const;
};

class Method {
public:
    struct Query {
        std::string type;
        std::string name;
        int argc = -1;  // -1 = 按 0..kMaxProbeArity 探测
        std::vector<std::string> param_types;
    };

    Method() = default;

    /// 同个数重载不唯一时拒绝并列出候选：猜错会把指针当结构体传进去，实测 SIGBUS。
    static Outcome<Method> Resolve(const Session& session, const Query& query);
    static Outcome<Method> Resolve(const Session& session, std::string_view type, std::string_view name,
                                   int argc = -1);

    bool valid() const { return info_ != nullptr; }
    bool IsStatic() const { return isStatic_; }
    int ArgCount() const { return argc_; }
    const std::string& ReturnTypeName() const { return returnTypeName_; }
    const MethodInfo* info() const { return info_; }

    std::string Describe() const;

    CallResult Invoke(const Session& session, void* self, CallArgs& args) const;
    CallResult InvokeRaw(const Session& session, void* self, void** argv, int argc) const;

private:
    CallResult InvokeImpl(const Session& session, void* self, void** params, int argc) const;

    static constexpr int kMaxProbeArity = 8;

    const Symbols* symbols_ = nullptr;
    const MethodInfo* info_ = nullptr;
    bool isStatic_ = false;
    int argc_ = 0;
    std::string returnTypeName_;
};

}  // namespace pylon::il2cpp
