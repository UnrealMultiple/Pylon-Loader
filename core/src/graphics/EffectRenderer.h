#pragma once

#include "graphics/EffectTypes.h"
#include "graphics/GlShadow.h"
#include "graphics/RenderHook.h"

namespace pylon::graphics {

class EffectRenderer {
public:
    // `state`：执行前 Unity 的状态（GlShadow），执行后还回去。
    static void Execute(const EffectFrame& frame, const DrawTarget& target, const GlShadow& state);
    static void FillStats(RenderStats& stats);
};

}  // namespace pylon::graphics
