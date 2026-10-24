// 层：内核层 / boot —— 调用自检实现
#include "boot/BindSelfTest.h"

#include <cstdarg>
#include <cstdio>
#include <vector>

#include "il2cpp/Call.h"
#include "il2cpp/Il2CppRuntime.h"
#include "pylon/Log.h"

namespace pylon::boot {
namespace {

std::string Format(const char* fmt, ...) {
    char buffer[512];
    va_list args;
    va_start(args, fmt);
    std::vsnprintf(buffer, sizeof(buffer), fmt, args);
    va_end(args);
    return buffer;
}

/// 一条自检（静态、无参、值类型返回）：step 是序号，**执行前**先打一行日志（崩了也知道崩在哪一步）。
std::string Line0(int step, const char* type, const char* name) {
    LOGI("selftest", "第 %d 步：调用 %s::%s()", step, type, name);

    auto resolved = il2cpp::MethodRef::Resolve(type, name, 0);
    if (!resolved) return Format("✗ %s::%s —— %s", type, name, resolved.reason.c_str());

    il2cpp::CallArgs args;
    const auto result = resolved.value.Invoke(nullptr, args);
    return Format("%s %s::%s —— %s → %s", result.invoked && !result.threw ? "✓" : "✗", type, name,
                  resolved.value.Describe().c_str(), result.Describe().c_str());
}

/// 字符串参数的调用：顺便验证托管字符串的读回。
/// **按类型精确解析**：`Concat/2` 有 (string,string) 与 (ReadOnlySpan<char>,ReadOnlySpan<char>)
/// 两个重载，只按个数挑会挑到那个按值传结构体的——本机实测直接 SIGBUS。
std::string LineConcat(int step) {
    LOGI("selftest", "第 %d 步：调用 System.String::Concat(string, string)", step);

    auto& api = il2cpp::Runtime::Instance().api();
    if (api.string_new == nullptr) return "✗ System.String::Concat —— il2cpp_string_new 不可用";

    void* left = api.string_new("pylon-");
    void* right = api.string_new("binding-ok");
    if (left == nullptr || right == nullptr) return "✗ System.String::Concat —— 造不出托管字符串";

    il2cpp::MethodRef::Query query;
    query.type = "System.String";
    query.name = "Concat";
    query.param_types = {"System.String", "System.String"};
    auto resolved = il2cpp::MethodRef::Resolve(query);
    if (!resolved) return Format("✗ System.String::Concat(string,string) —— %s", resolved.reason.c_str());

    il2cpp::CallArgs args;
    args.PushReference(left);
    args.PushReference(right);
    const auto result = resolved.value.Invoke(nullptr, args);

    // 期望值写死：字符串布局或调用路径错了，这里对不上（或读出乱码），一眼可见。
    const bool correct = result.invoked && !result.threw && result.value.text == "pylon-binding-ok";
    return Format("%s System.String::Concat(\"pylon-\", \"binding-ok\") —— %s%s", correct ? "✓" : "✗",
                  result.Describe().c_str(),
                  correct ? "" : "，期望 'pylon-binding-ok'");
}

/// 故意抛异常：值必须失败，但进程必须活着，并且异常文本要读得出来。
std::string LineThrowingCall(int step) {
    LOGI("selftest", "第 %d 步：调用 System.Int32::Parse(\"not-a-number\")，期望抛托管异常", step);

    auto& api = il2cpp::Runtime::Instance().api();
    if (api.string_new == nullptr) return "✗ System.Int32::Parse —— il2cpp_string_new 不可用";

    void* text = api.string_new("not-a-number");

    il2cpp::MethodRef::Query query;
    query.type = "System.Int32";
    query.name = "Parse";
    query.param_types = {"System.String"};
    auto resolved = il2cpp::MethodRef::Resolve(query);
    if (!resolved) return Format("✗ System.Int32::Parse(string) —— %s", resolved.reason.c_str());

    il2cpp::CallArgs args;
    args.PushReference(text);
    const auto result = resolved.value.Invoke(nullptr, args);

    if (!result.invoked) {
        return Format("✗ System.Int32::Parse(\"not-a-number\") —— %s", result.error.c_str());
    }
    if (!result.threw) {
        return Format("✗ System.Int32::Parse(\"not-a-number\") —— 期望抛异常，实际 %s",
                      result.Describe().c_str());
    }
    return Format("✓ System.Int32::Parse(\"not-a-number\") —— 托管异常被捕获，进程存活：%s",
                  result.exception.c_str());
}

}  // namespace

std::string RunBindSelfTest() {    // 自检可能被**任意**线程调起（Java 侧的观察线程、Mod 线程…），所以先把自己 attach 上。
    // 踩过的坑：只判 Attached()（域拿到了）就往下走，会在未 attach 的新线程上调 IL2CPP → SIGSEGV。
    const auto attached = il2cpp::Runtime::Instance().Attach();
    if (!attached) {
        return "✗ 自检未运行：attach 失败 —— " + attached.reason;
    }

    std::string report = "[绑定自检] C# 方法绑定与调用\n";
    report += Line0(1, "Terraria.Main", "get_mouseX") + "\n";
    report += Line0(2, "Terraria.Main", "get_mouseLeftRelease") + "\n";
    report += Line0(3, "UnityEngine.Time", "get_deltaTime") + "\n";
    report += LineConcat(4) + "\n";
    report += LineThrowingCall(5) + "\n";
    return report;
}

}  // namespace pylon::boot
