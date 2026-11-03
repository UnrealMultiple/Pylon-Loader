// 层：内核层 / boot —— 挂钩 il2cpp_init，用它当"引擎初始化完成"的确定信号
// 不能靠固定静置：实测崩溃时刻精确跟着静置时间走（4s→第 6 秒崩、20s→第 20 秒、25s→第 24 秒），
// 即探测线程第一次调 IL2CPP API 时引擎还在 il2cpp_init 里。上游用 ShadowHook 的 pending
// hook 解决同一问题；我们用已有的 Dobby（Harmony 的 detour provider 也是它）。
#pragma once

#include <atomic>

#include "pylon/Status.h"

namespace pylon::boot {

/// 用 Dobby 挂钩 il2cpp_init（幂等）。必须在引擎调用它之前装（nativeStart 时即可）。
Status InstallEngineInitHook();

/// il2cpp_init 是否已经返回（唯一可信的"可以碰 IL2CPP"信号）。
bool EngineInitDone();

/// hook 是否装上了。没装上时探测应当**拒绝启动**而不是猜时间。
bool EngineInitHookInstalled();

}  // namespace pylon::boot
