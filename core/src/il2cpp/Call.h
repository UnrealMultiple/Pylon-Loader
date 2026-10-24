// 层：内核层 / il2cpp —— C# 方法绑定与调用
// **只走 `il2cpp_runtime_invoke`，不直接用 `methodPointer`**：C# 异常在 IL2CPP 里是真正的
// C++ 异常，由 libgcc 展开器抛出，而我们这把 .so 用 LLVM libunwind——同栈相遇直接崩进程（见 docs/13）。
#pragma once

#include <cstdint>
#include <deque>
#include <string>
#include <vector>

#include "il2cpp/Il2CppApi.h"
#include "il2cpp/Il2CppRuntime.h"
#include "pylon/Status.h"

namespace pylon::il2cpp {

/// 返回值按托管类型分类（名字取自 `il2cpp_type_get_name`，不猜布局）。
enum class ValueKind {
    Void,
    Boolean,
    Int32,
    Int64,
    Single,
    Double,
    String,
    Reference,  // 其它引用类型（对象/数组/委托…）：就是个指针
    Unknown,
};

const char* ValueKindName(ValueKind kind);

struct Value {
    ValueKind kind = ValueKind::Unknown;
    int64_t raw = 0;          // 原始位模式（引用类型就是指针值）
    std::string text;         // 人类可读（字符串类型会真的读出来）
};

/// 调用参数：`runtime_invoke` 要"指向每个实参的指针"的数组，故用 deque 保证**地址稳定**（push_back 不搬动已有元素）。
class CallArgs {
public:
    /// 引用类型实参（Il2CppObject*/Il2CppString*/数组指针…）。
    void PushReference(const void* pointer);

    /// 值类型实参：按托管类型的**字节宽度**给（int32/float 各 4 字节，bool 1 字节…）。
    void PushInt32(int32_t value);
    void PushBoolean(bool value);
    void PushSingle(float value);

    /// 结构体按值（Rectangle 16 字节、Vector2 8 字节…）：原样拷贝字节，不做 ABI 推断。
    void PushRaw(const void* data, size_t size);

    size_t size() const { return pointers_.size(); }
    void** data() { return pointers_.empty() ? nullptr : pointers_.data(); }

private:
    std::deque<std::vector<uint8_t>> storage_;
    std::vector<void*> pointers_;
};

struct CallResult {
    bool invoked = false;       // 调用真的发生了（不管托管层有没有抛）
    bool threw = false;         // 托管层抛了异常（已被 runtime_invoke 捕获）
    std::string exception;      // 异常文本（il2cpp_format_exception）
    std::string error;          // invoked=false 的原因
    Value value;

    /// 一行摘要，日志直接用。
    std::string Describe() const;
};

/// 一个已解析的 C# 方法（含是否实例方法、参数个数、返回类型名）。
class MethodRef {
public:
    /// 解析请求：给了 `param_types` 就逐参数比类型名（精确）；只给 `argc` 时若同名同个数的
    /// 重载有多个则**拒绝**并列出候选——猜错会把指针当结构体读，实测直接 SIGBUS（见 docs/09）。
    struct Query {
        std::string type;                      // "Terraria.Main"
        std::string name;                      // "DrawMenu"
        int argc = -1;                         // -1 = 按 0..8 探测
        std::vector<std::string> param_types;  // 可选；给了就精确匹配（含 ref/struct 的区分）
    };

    static Outcome<MethodRef> Resolve(const std::string& qualified_type,
                                      const std::string& name,
                                      int argc = -1);
    static Outcome<MethodRef> Resolve(const Query& query);

    bool valid() const { return info_ != nullptr; }
    bool is_static() const { return is_static_; }
    int argc() const { return argc_; }
    const std::string& return_type_name() const { return return_type_name_; }
    const MethodInfo* info() const { return info_; }

    /// 形如 `Terraria.Main::get_mouseX/0 -> System.Int32`。
    std::string Describe() const;

    /// 调用。静态方法 self 传 nullptr；实例方法传对象指针。
    CallResult Invoke(void* self, CallArgs& args) const;

    /// 以 `il2cpp_runtime_invoke` 的 `params` 形态直接调用（Mod ABI 用：`argv[i]` 指向第 i 个实参的值；参数个数必须一致）。
    CallResult InvokeRaw(void* self, void** argv, int argc) const;

private:
    CallResult InvokeImpl(void* self, void** params, int argc) const;

    const MethodInfo* info_ = nullptr;
    bool is_static_ = false;
    int argc_ = 0;
    std::string return_type_name_;
};

/// 便捷：静态无参调用（属性 getter 最多，M3 读输入状态全靠它）。
CallResult InvokeStatic0(const std::string& qualified_type, const std::string& name);

/// 便捷：实例单参调用（`UIElement.Append(UIElement*)`、`UIText.SetText(string)` 一类）。
CallResult InvokeInstance1(const std::string& qualified_type, const std::string& name, void* self,
                           void* arg);

}  // namespace pylon::il2cpp
