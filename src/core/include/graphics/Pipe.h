// 层：内核层 / graphics —— 帧编排
//
// 三个入口对应一帧里的三个时刻：游戏线程 Publish、世界相机已画完（RunWorld）、
// 即将 eglSwapBuffers（RunScreen）。谁在这里都不做 GL 调用，绘制交给 fx::Pass。
#pragma once

#include <cstdint>

#include "graphics/GlState.h"
#include "graphics/Overlay.h"
#include "graphics/fx/Frame.h"

namespace pylon::graphics {

/// 相机序号。
enum class Camera : int { World = 0, Ui = 1, Other = 2 };

struct PipelineStats {
    int64_t frames = 0, draws = 0, boxPixels = 0, cpuNs = 0;
    int64_t gpuNs = 0, gpuSamples = 0;
    int64_t meshDraws = 0, fullscreenDraws = 0, meshVertices = 0;
    int64_t uniformBytes = 0, bufferDataBytes = 0;
    int64_t screenCopies = 0, screenCopyPixels = 0;
    int64_t worldFrames = 0, screenFrames = 0, markersIssued = 0, markerHits = 0;
    int64_t cameras[3] = {0, 0, 0};
    int fbo = 0, samples = 0;
    int viewport[4] = {0, 0, 0, 0};
    bool installed = false, worldLayer = false, overlay = false, gpuTimer = false;
};

class Pipe {
public:
    /// 游戏线程。幂等；false = 这套图形能力整体不可用（原因已写日志）。
    static bool Install();
    static bool Installed();

    /// 游戏线程：本帧要画的步骤。没有 Publish 的帧不画。
    static void Publish(fx::Frame&& frame);

    /// 游戏线程：世界层这一帧有东西要画。
    static bool WorldPending();

    static void NoteCamera(Camera camera);
    static void NoteMarkerIssued();

    /// 渲染线程。
    static void RunWorld(const GlState& state, const fx::RenderTarget& target);
    static void RunScreen(const GlState& state, int width, int height);

    static void SetOverlay(OverlayFn fn);
    static PipelineStats Stats();
};

}  // namespace pylon::graphics
