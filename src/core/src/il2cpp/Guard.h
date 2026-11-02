// 层：内核层 / il2cpp —— 异常边界：唯一一个"跨进托管代码再活着回来"的入口
#pragma once

#include <string>

#include "il2cpp/Symbols.h"

namespace pylon::il2cpp {

struct Caught {
    bool threw = false;
    Il2CppObject* exception = nullptr;  // C# 异常对象
    const char* cppType = nullptr;      // 否则是抛出的 C++ 类型名（mangled）
};

class Guard {
public:
    /// body 到游戏代码之间不能有未完成的析构：跳转会不带清理地丢弃那些帧。
    /// @return true = 正常返回
    static bool Run(void (*body)(void*), void* arg, Caught* caught = nullptr);

    template <class F>
    static bool Run(F& fn, Caught* caught = nullptr) {
        return Run([](void* p) { (*static_cast<F*>(p))(); }, &fn, caught);
    }
};

std::string Describe(const Caught& caught);
std::string DescribeException(Il2CppObject* exception);

}  // namespace pylon::il2cpp
