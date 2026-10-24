#pragma once

#include "graphics/EffectTypes.h"

#include <cstdint>

namespace pylon::graphics {

/// 画到哪：世界相机的 framebuffer 和它的 viewport。
struct DrawTarget {
    int fbo = 0;
    int viewport[4] = {0, 0, 0, 0};
};

/// 装渲染 hooks（GL hooks + Camera.FireOnPostRender）。幂等。游戏线程、引擎稳定后调。
void installRenderHook();

/// FireOnPostRender 是否挂上了（Install 的结果查询）。
bool renderHookInstalled();

// marker：一张 17x3 的 RenderTexture，世界相机结束时的 Graphics.Blit 会以
// glViewport(0,0,17,3) 出现在渲染线程的 GL 队列里，用于定位"世界与 UI 之间"。
constexpr int kMarkerWidth = 17;
constexpr int kMarkerHeight = 3;

// 游戏线程，每帧（C# ScreenEffects.Submit）：本帧要画的步骤。没 submit 就不画。
void submitEffects(EffectFrame&& frame);

// 渲染线程（GlHooks.cpp）：取走世界层（marker 之后）或屏幕层（帧末）的帧。false = 没有。
bool takeWorldEffects(EffectFrame& out);
bool takeScreenEffects(EffectFrame& out);
void noteMarkerHit();

struct RenderStats {
    int64_t worldPostRender = 0, uiPostRender = 0, otherPostRender = 0;
    int64_t draws = 0, frames = 0, markersIssued = 0, markerHits = 0, screenFrames = 0;
    int64_t renderCpuNs = 0, markerCpuNs = 0, gpuNs = 0, gpuSamples = 0;    // 启动以来累加
    int64_t gpuFrameNs = 0, gpuFrameSamples = 0;                            // 整帧，隔帧测
    int64_t shadowChecks = 0, shadowMismatches = 0;                         // GL 状态镜像核对
    int64_t boxPixels = 0;                                                  // 拷贝+画的像素
    int32_t drawFbo = -1, readFbo = -1, viewport[4] = {0, 0, 0, 0};
    int32_t samples = -1, blitError = 0, copyFallbacks = 0;
    bool hookInstalled = false, hasContext = false, markerReady = false, gpuTimer = false;
};

RenderStats renderStats();

}  // namespace pylon::graphics
