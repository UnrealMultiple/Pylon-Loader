#include <shadowhook.h>
#include <mutex>
#include <unordered_map>
#include "pylon/Log.h"
#include "pylon/mod/ModApi.h"

namespace {
bool EnsureHook() {
    static std::once_flag once;
    static bool ok = false;
    std::call_once(once, [] {
        const int rc = shadowhook_init(SHADOWHOOK_MODE_UNIQUE, false);
        if (rc != 0) {
            LOGE("hook", "shadowhook_init 失败 rc=%d：%s", rc, shadowhook_to_errmsg(rc));
            return;
        }
        ok = true;
        LOGI("hook", "ShadowHook 就绪（%s，短函数安全，内核内嵌）", shadowhook_get_version());
    });
    return ok;
}


struct HookEntry {
    void* detour = nullptr;
    void* origin = nullptr;
    void* stub = nullptr;  // shadowhook_unhook 用
    int holders = 0;
};

std::mutex g_mutex;
std::unordered_map<void*, HookEntry> g_hooks;

}  // namespace

PYLON_MOD_EXPORT int PylonMod_HookAvailable() { return EnsureHook() ? 1 : 0; }


static int ApplyHook(void* target, void* detour, void** trampoline, void** publishedOrigin) {
    if (target == nullptr || detour == nullptr || trampoline == nullptr) return -1;
    if (trampoline != publishedOrigin) *trampoline = nullptr;
    if (!EnsureHook()) return -2;

    std::lock_guard<std::mutex> lock(g_mutex);
    const auto existing = g_hooks.find(target);
    if (existing != g_hooks.end()) {
        if (existing->second.detour != detour) {
            LOGE("hook", "地址 %p 已挂别的替换函数（%p），拒绝重复挂钩", target,
                 existing->second.detour);
            return -4;
        }
        if (publishedOrigin != nullptr && *publishedOrigin != nullptr &&
            *publishedOrigin != existing->second.origin) return -4;
        existing->second.holders++;
        *trampoline = existing->second.origin;
        if (publishedOrigin != nullptr) *publishedOrigin = existing->second.origin;
        LOGI("hook", "复用已有钩子 %p（持有者 %d）", target, existing->second.holders);
        return 0;
    }

    if (publishedOrigin != nullptr && *publishedOrigin != nullptr) return -4;
    void* origin = nullptr;
    void** originOut = publishedOrigin != nullptr ? publishedOrigin : &origin;
    void* stub = shadowhook_hook_func_addr(target, detour, originOut);
    if (publishedOrigin != nullptr) origin = *publishedOrigin;
    if (stub == nullptr) {
        const int err = shadowhook_get_errno();
        LOGE("hook", "shadowhook 挂钩失败 errno=%d：%s 目标=%p", err, shadowhook_to_errmsg(err), target);
        return -5;
    }
    if (origin == nullptr) {
        LOGE("hook", "ShadowHook 没给出跳板，撤销挂钩：%p", target);
        shadowhook_unhook(stub);
        return -3;
    }

    g_hooks[target] = HookEntry{detour, origin, stub, 1};
    *trampoline = origin;
    if (publishedOrigin != nullptr) *publishedOrigin = origin;
    LOGI("hook", "已挂钩 %p → %p（跳板 %p）", target, detour, origin);
    return 0;
}

PYLON_MOD_EXPORT int PylonMod_HookApply(void* target, void* detour, void** trampoline) {
    return ApplyHook(target, detour, trampoline, nullptr);
}

PYLON_MOD_EXPORT int PylonMod_HookApplyPublished(void* target, void* detour,
                                                  void** originSlot, void** trampoline) {
    return ApplyHook(target, detour, trampoline, originSlot);
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

    const int rc = shadowhook_unhook(entry->second.stub);
    if (rc != 0) {
        // Keep the stub and holder retryable; forgetting a still-installed patch loses ownership.
        entry->second.holders = 1;
        LOGE("hook", "shadowhook_unhook 失败 rc=%d,保留记账以便重试", rc);
        return rc;
    }
    g_hooks.erase(entry);
    LOGI("hook", "已卸载钩子 %p rc=%d", target, rc);
    return rc;
}
