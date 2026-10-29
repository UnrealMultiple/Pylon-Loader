// 层：内核层 —— 显式错误传递
// 刻意不用异常：内核跑在游戏进程里，异常穿过 JNI 边界或 native 栈的代价（栈展开 +
// 与 Unity/IL2CPP 的 C++ 运行时交互）不值得；"失败要给用户一句能读懂的原因"用返回值
// 表达更直接——docs/README 纪律：解析失败就让 Mod 停用并写日志。
#pragma once

#include <string>
#include <utility>

namespace pylon {

/// 只表达"成功/失败 + 原因"，不带错误码枚举：原因字符串就是给人和日志看的。
struct Status {
    bool ok = true;
    std::string reason;

    static Status Ok() { return Status{true, {}}; }
    static Status Fail(std::string why) { return Status{false, std::move(why)}; }

    explicit operator bool() const { return ok; }
};

/// 带值的版本；失败时 value 无意义（保持默认构造，避免半初始化状态）。
template <typename T>
struct Outcome {
    bool ok = false;
    T value{};
    std::string reason;

    static Outcome Good(T v) { return Outcome{true, std::move(v), {}}; }
    static Outcome Bad(std::string why) { return Outcome{false, {}, std::move(why)}; }

    explicit operator bool() const { return ok; }
};

}  // namespace pylon
