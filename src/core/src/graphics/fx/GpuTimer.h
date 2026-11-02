// 层：内核层 / graphics::fx —— GPU 耗时（GL_EXT_disjoint_timer_query）
#pragma once

#include <cstdint>

namespace pylon::graphics::fx {

enum class Channel { Effect, Frame };

class GpuTimer {
public:
    /// 没有扩展就返回 false，之后所有调用都是空操作。
    static bool Available();

    /// 同一时刻只测一段；false = 这一段没测。
    static bool Begin(Channel channel);
    static void End(Channel channel);

    static void Totals(Channel channel, int64_t& totalNs, int64_t& samples);
};

}  // namespace pylon::graphics::fx
