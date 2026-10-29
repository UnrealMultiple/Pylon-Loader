// 层：内核层 / boot —— il2cpp_init hook 实现（见头文件里的理由）
#include "boot/EngineInitHook.h"

#include <string>

#include "il2cpp/Il2CppRuntime.h"
#include "pylon/Log.h"
#include "pylon/mod/ModApi.h"

namespace pylon::boot {
namespace {

std::atomic<bool> g_done{false};
std::atomic<bool> g_installed{false};

using InitFn = int (*)(const char*);
InitFn g_original = nullptr;

int InitReplacement(const char* domain_name) {
    const int rc = g_original != nullptr ? g_original(domain_name) : -1;
    // 只有原函数返回之后才算"引擎越过初始化窗口"。
    g_done.store(true);
    LOGI("probe", "il2cpp_init 已返回（rc=%d）→ 现在可以安全地碰 IL2CPP API", rc);
    return rc;
}

}  // namespace

Status InstallEngineInitHook() {
    if (g_installed.load()) return Status::Ok();

    auto& api = il2cpp::Runtime::Instance().api();
    if (api.init == nullptr) {
        return Status::Fail("没有绑定 il2cpp_init（这个构建无法确定初始化时机）");
    }

    void* trampoline = nullptr;
    const int rc = PylonMod_HookApply(reinterpret_cast<void*>(api.init),
                                      reinterpret_cast<void*>(&InitReplacement), &trampoline);
    if (rc != 0 || trampoline == nullptr) {
        return Status::Fail("Dobby 挂钩 il2cpp_init 失败（rc=" + std::to_string(rc) + "）");
    }

    g_original = reinterpret_cast<InitFn>(trampoline);
    g_installed.store(true);
    LOGI("probe", "已挂钩 il2cpp_init：探测会等它返回再开始（不再靠固定静置猜时机）");
    return Status::Ok();
}

bool EngineInitDone() { return g_done.load(); }

bool EngineInitHookInstalled() { return g_installed.load(); }

}  // namespace pylon::boot
