// 层：内核层 / graphics::fx —— 一帧效果的执行
#pragma once

#include <cstdint>

#include "graphics/fx/Frame.h"

namespace pylon::graphics::fx {

struct PassStats {
    int64_t frames = 0, draws = 0, boxPixels = 0, cpuNs = 0;
    int64_t gpuNs = 0, gpuSamples = 0;
    int fbo = 0, samples = 0;
    int viewport[4] = {0, 0, 0, 0};
    bool ready = false;
};

class Pass {
public:
    static void Execute(const Frame& frame, const RenderTarget& target);

    static PassStats Stats();
};

}  // namespace pylon::graphics::fx
