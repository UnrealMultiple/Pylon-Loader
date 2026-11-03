// 层：内核层 / graphics —— Dobby 挂钩的薄封装
#pragma once

#include <cstddef>

#include "pylon/Log.h"
#include "pylon/mod/ModApi.h"

namespace pylon::graphics {

/// 挂钩成功时 `original` 拿到跳板（原函数）。失败已写日志，调用方决定整体是否继续。
template <class Fn>
bool Hook(Fn target, Fn detour, Fn* original) {
    if (target == nullptr) {
        LOGE("fx", "挂钩目标为空");
        return false;
    }
    void* trampoline = nullptr;
    const int rc = PylonMod_HookApply(reinterpret_cast<void*>(target), reinterpret_cast<void*>(detour), &trampoline);
    if (rc != 0 || trampoline == nullptr) {
        LOGE("fx", "挂钩失败（rc=%d，目标 %p）", rc, reinterpret_cast<void*>(target));
        return false;
    }
    *original = reinterpret_cast<Fn>(trampoline);
    return true;
}

}  // namespace pylon::graphics
