// 层：内核层 / il2cpp —— 域与线程实现
#include "il2cpp/Domain.h"

#include <unistd.h>

#include <atomic>
#include <cstdio>
#include <mutex>

#include "pylon/Log.h"

namespace pylon::il2cpp {

namespace {

std::mutex g_bindMutex;
Symbols g_symbols;
std::atomic<bool> g_bound{false};

std::atomic<Il2CppDomain*> g_domain{nullptr};
std::atomic<unsigned long> g_gameThread{0};
thread_local bool g_attached = false;

std::string Join(const std::vector<std::string>& items) {
    std::string text;
    for (const std::string& item : items) {
        if (!text.empty()) text += ", ";
        text += item;
    }
    return text;
}

}  // namespace

Status Domain::Bind() {
    std::lock_guard<std::mutex> lock(g_bindMutex);
    if (g_bound) return Status::Ok();

    BindReport report;
    if (!g_symbols.Load(&report)) {
        const std::string missing = Join(report.missingRequired);
        LOGW("core", "il2cpp API 绑定失败：必需 %d/%d，可选 %d/%d，缺少：%s", report.required,
             Symbols::kRequiredCount, report.optional, Symbols::kOptionalCount, missing.c_str());
        return Status::Fail(missing);
    }

    if (!report.missingOptional.empty()) {
        LOGW("core", "可选符号缺失（不影响解析，用到时判空）：%s", Join(report.missingOptional).c_str());
    }
    LOGI("core", "il2cpp API 绑定：必需 %d/%d，可选 %d/%d（dlopen RTLD_NOLOAD，未调用任何 API）",
         report.required, Symbols::kRequiredCount, report.optional, Symbols::kOptionalCount);
    g_bound = true;
    return Status::Ok();
}

bool Domain::Bound() {
    return g_bound;
}

const Symbols& Domain::symbols() {
    return g_symbols;
}

bool Domain::Ready() {
    return g_bound && g_symbols.domain_get != nullptr && g_symbols.domain_get() != nullptr;
}

Outcome<Session> Domain::Attach() {
    if (!g_bound) return Outcome<Session>::Bad("il2cpp API 未绑定完成，拒绝调用（先 nativeStart）");

    Il2CppDomain* domain = g_domain.load();
    if (domain == nullptr) {
        domain = g_symbols.domain_get();
        if (domain == nullptr) return Outcome<Session>::Bad("il2cpp_domain_get() 返回 null（引擎尚未完成初始化？）");
        g_domain.store(domain);
        LOGI("core", "domain=%p", static_cast<void*>(domain));
    }

    if (!g_attached) {
        if (g_symbols.thread_attach(domain) == nullptr) return Outcome<Session>::Bad("il2cpp_thread_attach() 返回 null");
        g_attached = true;
        LOGI("core", "已 attach 当前线程（线程 %lu）", static_cast<unsigned long>(gettid()));
    }

    return Outcome<Session>::Good(Session(&g_symbols, domain));
}

void Domain::MarkGameThread() {
    const unsigned long tid = static_cast<unsigned long>(gettid());
    if (g_gameThread.exchange(tid) == tid) return;

    char name[32] = {0};
    char path[64];
    std::snprintf(path, sizeof(path), "/proc/self/task/%lu/comm", tid);
    if (FILE* file = std::fopen(path, "r")) {
        if (std::fgets(name, sizeof(name), file) != nullptr) {
            for (char* p = name; *p != '\0'; ++p) {
                if (*p == '\n') *p = '\0';
            }
        }
        std::fclose(file);
    }
    LOGI("core", "已登记游戏线程：tid=%lu 名字=\"%s\"（托管方法只允许在这个线程上调）", tid,
         name[0] != '\0' ? name : "?");
}

bool Domain::OnGameThread() {
    const unsigned long registered = g_gameThread.load();
    return registered != 0 && registered == static_cast<unsigned long>(gettid());
}

unsigned long Domain::GameThreadId() {
    return g_gameThread.load();
}

Status Domain::RequireGameThread(std::string_view what) {
    if (OnGameThread()) return Status::Ok();
    return Status::Fail(std::string(what) + "只能在游戏线程(UnityMain)上进行：当前 tid=" +
                        std::to_string(static_cast<unsigned long>(gettid())) + "，已登记游戏线程 tid=" +
                        std::to_string(GameThreadId()));
}

}  // namespace pylon::il2cpp
