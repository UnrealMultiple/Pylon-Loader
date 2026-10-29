// 层：内核层 / mod —— 原生 hook 出口（托管侧的 detour provider 用它）
// DobbyHook(address, replace, &origin) 的第三个参数就是跳板（原函数的可调用入口），
// 也就是 Harmony 在 IL2CPP 上必需的东西。选 Dobby 的理由与用法纪律见 docs/11、docs/13。
#include <dlfcn.h>

#include <mutex>
#include <unordered_map>

#include "pylon/Log.h"
#include "pylon/mod/ModApi.h"

namespace {

using DobbyHookFn = int (*)(void* address, void* replace, void** origin);
using DobbyDestroyFn = int (*)(void* address);

DobbyHookFn g_hook = nullptr;
DobbyDestroyFn g_destroy = nullptr;

/// Dobby 只初始化一次，并且**先填好指针再发布成功状态**：
/// 早先是先置 `loaded=true` 再 dlsym，并发首调的后到者会看到"已加载但句柄为空"。
bool EnsureDobby() {
    static std::once_flag once;
    static bool ok = false;
    std::call_once(once, [] {
        void* lib = dlopen("libdobby.so", RTLD_NOW);
        if (lib == nullptr) {
            const char* err = dlerror();
            LOGE("hook", "libdobby.so 加载失败：%s", err != nullptr ? err : "未知");
            return;
        }
        g_hook = reinterpret_cast<DobbyHookFn>(dlsym(lib, "DobbyHook"));
        g_destroy = reinterpret_cast<DobbyDestroyFn>(dlsym(lib, "DobbyDestroy"));
        if (g_hook == nullptr) {
            LOGE("hook", "libdobby.so 里没有 DobbyHook");
            return;
        }
        ok = true;
        LOGI("hook", "Dobby 就绪（%p）", reinterpret_cast<void*>(g_hook));
    });
    return ok;
}

/// 每个目标地址的记账：跳板与"还有几个持有者"。同一地址重复挂钩会破坏第一次的跳板
/// （第二次是在已被 patch 的序言上做重定位）；卸载必须等所有持有者都放手 —— 托管侧
/// 可能缓存了跳板委托，提前撤销等于让它跳进已恢复的代码。
struct HookEntry {
    void* detour = nullptr;
    void* origin = nullptr;
    int holders = 0;
};

std::mutex g_mutex;
std::unordered_map<void*, HookEntry> g_hooks;

}  // namespace

PYLON_MOD_EXPORT int PylonMod_HookAvailable() { return EnsureDobby() ? 1 : 0; }

/// 成功时通过 trampoline 返回原函数入口。跳板必须非空，否则上层不能启用钩子（会跳 0 崩溃）。
/// @return 0 成功；-1 参数非法；-2 Dobby 不可用；-3 拿不到跳板（已撤销）；-4 该地址已挂别的替换函数
PYLON_MOD_EXPORT int PylonMod_HookApply(void* target, void* detour, void** trampoline) {
    if (target == nullptr || detour == nullptr || trampoline == nullptr) return -1;
    *trampoline = nullptr;
    if (!EnsureDobby()) return -2;

    std::lock_guard<std::mutex> lock(g_mutex);
    const auto existing = g_hooks.find(target);
    if (existing != g_hooks.end()) {
        if (existing->second.detour != detour) {
            LOGE("hook", "地址 %p 已挂别的替换函数（%p），拒绝重复挂钩", target,
                 existing->second.detour);
            return -4;
        }
        existing->second.holders++;
        *trampoline = existing->second.origin;
        LOGI("hook", "复用已有钩子 %p（持有者 %d）", target, existing->second.holders);
        return 0;
    }

    void* origin = nullptr;
    const int rc = g_hook(target, detour, &origin);
    if (rc != 0) {
        LOGE("hook", "DobbyHook 失败 rc=%d 目标=%p", rc, target);
        return rc;
    }
    if (origin == nullptr) {
        LOGE("hook", "Dobby 没给出跳板，撤销挂钩：%p", target);
        if (g_destroy != nullptr) g_destroy(target);
        return -3;
    }
    g_hooks[target] = HookEntry{detour, origin, 1};
    *trampoline = origin;
    LOGI("hook", "已挂钩 %p → %p（跳板 %p）", target, detour, origin);
    return 0;
}

PYLON_MOD_EXPORT int PylonMod_HookRemove(void* target) {
    if (target == nullptr) return -1;
    std::lock_guard<std::mutex> lock(g_mutex);
    const auto entry = g_hooks.find(target);
    if (entry == g_hooks.end()) return -1;  // 没挂过

    if (--entry->second.holders > 0) {
        LOGI("hook", "仍有 %d 个持有者，保留钩子 %p", entry->second.holders, target);
        return 0;
    }
    if (g_destroy == nullptr) return -2;
    const int rc = g_destroy(target);
    g_hooks.erase(entry);
    LOGI("hook", "已卸载钩子 %p rc=%d", target, rc);
    return rc;
}
