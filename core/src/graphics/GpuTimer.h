#pragma once

#include <cstdint>

namespace pylon::graphics {

enum class GpuChannel { Effect, Frame };

class GpuTimer {
public:
    // false = 已有别的测量开着（这段就不测了）。
    static bool Begin(GpuChannel channel);
    static void End(GpuChannel channel);
    static bool Available();
    // 累计（ns）与已回收的样本数。
    static void Totals(GpuChannel channel, int64_t& totalNs, int64_t& samples);
};

}  // namespace pylon::graphics
