#pragma once

#include "il2cpp/Il2CppApi.h"

#include <string>

namespace pylon::il2cpp {

/// 一次受保护调用抓到的东西。
struct Caught {
    bool threw = false;
    // C# 异常对象（IL2CPP 抛的 Il2CppExceptionWrapper 里的 Il2CppException*）。
    Il2CppObject* exception = nullptr;
    // 否则是抛出的 C++ 类型名（mangled），如果读得出来。
    const char* cppType = nullptr;
};

/**
 * 运行 `body(arg)`，把从它里面抛出来的异常拦在这里。
 *
 * `body` 在它到游戏代码之间**不能有**未完成的析构：跳转会不带清理地丢弃那些帧。
 * 实际用法就是"搭好调用 → 直调 → 存结果"，不做别的。
 *
 * @return true = body 正常返回；false = 抛了（`caught` 说明是什么）。
 */
bool guardedCall(void (*body)(void*), void* arg, Caught* caught = nullptr);

/// 同上的 lambda 形态。
template <class F>
bool guarded(F& fn, Caught* caught = nullptr) {
    return guardedCall([](void* p) { (*static_cast<F*>(p))(); }, &fn, caught);
}

/// 异常文本："System.IndexOutOfRangeException: ..."（il2cpp_format_exception）或 C++ 类型名。
std::string describeCaught(const Caught& c);

/// 同上，对 runtime_invoke 返回的异常对象。
std::string describeException(Il2CppObject* exception);

}  // namespace pylon::il2cpp
