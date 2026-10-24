// 层：内核层 / il2cpp —— 符号表、域、attach 票据
//
// 两条硬约束用类型表达：每个线程先 attach（票据只能由 Attach 产生）；托管方法与实例字段
// 只能在游戏线程上碰。
#pragma once

#include <string>
#include <string_view>
#include <vector>

#include "il2cpp/Metadata.h"
#include "il2cpp/Symbols.h"
#include "pylon/Status.h"

namespace pylon::il2cpp {

class Session {
public:
    Session() = default;

    const Symbols& raw() const { return *symbols_; }

    Outcome<Il2CppClass*> FindClass(std::string_view namespaze, std::string_view name) const;

    std::string ClassName(Il2CppClass* klass) const;

    std::vector<std::string> Images() const;

private:
    friend class Domain;
    Session(const Symbols* symbols, Il2CppDomain* domain) : symbols_(symbols), domain_(domain) {}

    const Symbols* symbols_ = nullptr;
    Il2CppDomain* domain_ = nullptr;
};

class Domain {
public:
    static Status Bind();
    static bool Bound();
    static const Symbols& symbols();

    /// 唯一允许在未 attach 时调的探针；不缓存 domain。
    static bool Ready();

    static Outcome<Session> Attach();
    static bool Attached();

    static void MarkGameThread();
    static bool OnGameThread();
    static unsigned long GameThreadId();

    static Status RequireGameThread(std::string_view what);
};

}  // namespace pylon::il2cpp
