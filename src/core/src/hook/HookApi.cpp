// 层：内核层 / mod —— 原生 hook 出口（托管侧的 detour provider 用它）。
// 后端 ShadowHook：入口只写一条 4 字节 B，能挂 8 字节的短函数；
// Dobby 的 16 字节补丁会写穿相邻函数（D-002）。
#include <dlfcn.h>

#include <mutex>
#include <unordered_map>

#include "pylon/Log.h"
#include "pylon/mod/ModApi.h"

namespace {

using ShInitFn = int (*)(int mode, bool debuggable);
using ShHookFn = void* (*)(void* func_addr, void* new_addr, void** orig_addr);
using ShUnhookFn = int (*)(void* stub);
using ShErrnoFn = int (*)();
using ShErrMsgFn = const char* (*)(int error_number);
using ShVersionFn = const char* (*)();

ShInitFn g_shInit = nullptr;
ShHookFn g_shHook = nullptr;
ShUnhookFn g_shUnhook = nullptr;
ShErrnoFn g_shErrno = nullptr;
ShErrMsgFn g_shErrMsg = nullptr;
ShVersionFn g_shVersion = nullptr;

template <typename T>
T Sym(void* lib, const char* name) { return reinterpret_cast<T>(dlsym(lib, name)); }

/// 只初始化一次，并且**先填好指针再发布成功状态**：
/// 早先是先置 loaded=true 再 dlsym，并发首调的后到者会看到"已加载但句柄为空"。
bool EnsureHook() {
    static std::once_flag once;
    static bool ok = false;
    std::call_once(once, [] {
        void* lib = dlopen("libshadowhook.so", RTLD_NOW);
        if (lib == nullptr) {
            const char* err = dlerror();
            LOGE("hook", "libshadowhook.so 加载失败：%s", err != nullptr ? err : "未知");
            return;
        }

        g_shInit = Sym<ShInitFn>(lib, "shadowhook_init");
        g_shHook = Sym<ShHookFn>(lib, "shadowhook_hook_func_addr");
        g_shUnhook = Sym<ShUnhookFn>(lib, "shadowhook_unhook");
        g_shErrno = Sym<ShErrnoFn>(lib, "shadowhook_get_errno");
        g_shErrMsg = Sym<ShErrMsgFn>(lib, "shadowhook_to_errmsg");
        g_shVersion = Sym<ShVersionFn>(lib, "shadowhook_get_version");

        if (g_shInit == nullptr || g_shHook == nullptr || g_shUnhook == nullptr ||
            g_shErrno == nullptr || g_shErrMsg == nullptr) {
            LOGE("hook", "libshadowhook.so 缺少必需符号");
            return;
        }

        // 1 = SHADOWHOOK_MODE_UNIQUE：同一地址只接受一次挂钩，重复挂钩由上面的 holders 计数挡掉
        // （托管侧同地址的第二个钩子走它自己的链，不再下来）。
        const int rc = g_shInit(1, false);
        if (rc != 0) {
            LOGE("hook", "shadowhook_init 失败 rc=%d：%s", rc, g_shErrMsg(rc));
            return;
        }

        ok = true;
        LOGI("hook", "ShadowHook 就绪（%s，短函数安全）", g_shVersion != nullptr ? g_shVersion() : "?");
    });
    return ok;
}

/// 每个目标地址的记账：跳板与"还有几个持有者"。同一地址重复挂钩会破坏第一次的跳板
/// （第二次是在已被 patch 的序言上做重定位）；卸载必须等所有持有者都放手 —— 托管侧
/// 可能缓存了跳板委托，提前撤销等于让它跳进已恢复的代码。
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

/// 成功时通过 trampoline 返回原函数入口。跳板必须非空，否则上层不能启用钩子（会跳 0 崩溃）。
/// @return 0 成功；-1 参数非法；-2 ShadowHook 不可用；-3 拿不到跳板（已撤销）；
///         -4 该地址已挂别的替换函数；-5 后端挂钩失败（详见日志）
PYLON_MOD_EXPORT int PylonMod_HookApply(void* target, void* detour, void** trampoline) {
    if (target == nullptr || detour == nullptr || trampoline == nullptr) return -1;
    *trampoline = nullptr;
    if (!EnsureHook()) return -2;

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
    void* stub = g_shHook(target, detour, &origin);
    if (stub == nullptr) {
        const int err = g_shErrno();
        LOGE("hook", "shadowhook 挂钩失败 errno=%d：%s 目标=%p", err, g_shErrMsg(err), target);
        return -5;
    }
    if (origin == nullptr) {
        LOGE("hook", "ShadowHook 没给出跳板，撤销挂钩：%p", target);
        g_shUnhook(stub);
        return -3;
    }

    g_hooks[target] = HookEntry{detour, origin, stub, 1};
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

    const int rc = g_shUnhook(entry->second.stub);
    if (rc != 0) LOGE("hook", "shadowhook_unhook 失败 rc=%d", rc);
    g_hooks.erase(entry);
    LOGI("hook", "已卸载钩子 %p rc=%d", target, rc);
    return rc;
}
