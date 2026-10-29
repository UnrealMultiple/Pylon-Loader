#pragma once

#include "graphics/RenderHook.h"

#include <string>

namespace pylon::graphics {

/// 装 GL hooks（幂等，installRenderHook 里调）。
void installGlHooks();

/// 请下一整帧 GL 调用，结果进日志（"trace: ..."）。
void requestGlTrace();

/// 状态镜像的核对计数（GlShadow.h）。
void fillGlHookStats(RenderStats& stats);

}  // namespace pylon::graphics
