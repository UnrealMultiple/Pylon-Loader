// 层：内核层 / imgui —— 渲染线程的上屏
//
// 画在游戏自己的 GLES 上下文里，eglSwapBuffers 之前落到默认 framebuffer：
// 没有第二块 Surface、没有交换链，也不需要另一套上下文。
#pragma once

#include <cstdint>
#include <memory>

namespace pylon::imgui {

class Snapshot;

class Present {
public:
    /// 渲染线程。调用方保证这批 GL 调用不进游戏的状态镜像（Pipe 用 OwnDraw 包住）。
    static void AtSwap(int width, int height);

    /// 任意线程：后端已在渲染线程建好，且见过表面尺寸。
    static bool Ready();
    static void Size(int32_t* width, int32_t* height);

    /// 游戏线程：交出最新一帧（nullptr = 清屏）。
    static void Submit(std::shared_ptr<const Snapshot> frame);

    /// 游戏线程：请渲染线程销毁后端的 GL 对象。
    static void Teardown();
};

}  // namespace pylon::imgui
