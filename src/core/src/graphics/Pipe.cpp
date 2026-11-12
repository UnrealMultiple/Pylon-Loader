// 层：内核层 / graphics —— 编排实现
#include "graphics/Pipe.h"

#include <atomic>
#include <mutex>
#include <utility>

#include "graphics/CameraHook.h"
#include "graphics/Capture.h"
#include "graphics/Gles.h"
#include "graphics/Guard.h"
#include "graphics/Marker.h"
#include "graphics/fx/GpuTimer.h"
#include "graphics/fx/Pass.h"
#include "pylon/Log.h"

namespace pylon::graphics {

namespace {

std::mutex g_mutex;
fx::Frame g_world;
fx::Frame g_screen;

int g_state = 0;
bool g_worldLayer = false;
bool g_markerTried = false;
bool g_warnedWorldDrop = false;
bool g_loggedFirstPublish = false;

std::atomic<OverlayFn> g_overlay{nullptr};
std::atomic<int64_t> g_worldFrames{0}, g_screenFrames{0}, g_markersIssued{0}, g_markerHits{0};
std::atomic<int64_t> g_cameras[3];

fx::Frame take(fx::Frame& slot) {
    fx::Frame frame;
    std::lock_guard<std::mutex> lock(g_mutex);
    frame = std::move(slot);
    return frame;
}

}  // namespace

bool Pipe::Install() {
    if (g_state != 0) return g_state > 0;
    g_state = -1;
    if (Gles::Load() == nullptr) return false;
    if (!InstallCameraHook()) return false;
    if (!InstallGlHooks()) return false;
    g_state = 1;
    LOGI("fx", "渲染管线已安装");
    return true;
}

bool Pipe::Installed() {
    return g_state > 0;
}

void Pipe::Publish(fx::Frame&& frame) {
    if (!g_markerTried) {
        g_markerTried = true;
        g_worldLayer = CreateMarker();
    }

    fx::Frame world, screen;
    world.targets = frame.targets;
    screen.targets = frame.targets;
    world.history = frame.history;
    for (fx::Draw& draw : frame.draws) {
        if (draw.layer == fx::Layer::Screen) {
            screen.draws.push_back(std::move(draw));
        } else {
            world.draws.push_back(std::move(draw));
        }
    }
    const bool worldWork = !world.draws.empty() || world.history;
    if (!g_worldLayer && worldWork) {
        if (!g_warnedWorldDrop) {
            g_warnedWorldDrop = true;
            LOGW("fx", "世界层不可用：layer=World 的绘制被丢弃");
        }
        world.draws.clear();
        world.history = false;
    }
    if (!g_loggedFirstPublish && (worldWork || !screen.draws.empty())) {
        g_loggedFirstPublish = true;
        LOGI("fx", "首帧效果：世界 %zu 条 / 屏幕 %zu 条", world.draws.size(), screen.draws.size());
    }
    // 没有活干的通道要空掉：否则只有 target 声明也会被当成"有东西要画"。
    if (world.draws.empty() && !world.history) world = fx::Frame();
    if (screen.draws.empty()) screen = fx::Frame();

    std::lock_guard<std::mutex> lock(g_mutex);
    g_world = std::move(world);
    g_screen = std::move(screen);
}

bool Pipe::WorldPending() {
    std::lock_guard<std::mutex> lock(g_mutex);
    return !g_world.empty();
}

void Pipe::NoteCamera(Camera camera) {
    g_cameras[static_cast<int>(camera)]++;
}

void Pipe::NoteMarkerIssued() {
    g_markersIssued++;
}

void Pipe::RunWorld(const GlState& state, const fx::RenderTarget& target) {
    fx::Frame frame = take(g_world);
    if (frame.empty()) return;
    g_worldFrames++;
    g_markerHits++;
    fx::Pass::Execute(frame, target);
    OwnDraw own;
    state.Restore();
}

void Pipe::RunScreen(const GlState& state, int width, int height) {
    fx::Frame frame = take(g_screen);
    const OverlayFn overlay = g_overlay.load();
    if (frame.empty() && overlay == nullptr) return;

    fx::RenderTarget target;
    target.fbo = static_cast<int>(state.drawFbo);
    for (int i = 0; i < 4; ++i) target.viewport[i] = state.viewport[i];

    if (!frame.empty()) {
        g_screenFrames++;
        fx::Pass::Execute(frame, target);
    }

    if (overlay != nullptr && width > 0 && height > 0) {
        OwnDraw own;
        const Gles& g = gles();
        g.BindFramebuffer(GL_FRAMEBUFFER, 0);
        g.ColorMask(GL_TRUE, GL_TRUE, GL_TRUE, GL_TRUE);
        overlay(width, height);
    }
    {
        OwnDraw own;
        state.Restore();
    }
}

void Pipe::SetOverlay(OverlayFn fn) {
    g_overlay.store(fn);
}

PipelineStats Pipe::Stats() {
    const fx::PassStats pass = fx::Pass::Stats();
    PipelineStats stats;
    stats.frames = pass.frames;
    stats.draws = pass.draws;
    stats.boxPixels = pass.boxPixels;
    stats.cpuNs = pass.cpuNs;
    stats.gpuNs = pass.gpuNs;
    stats.gpuSamples = pass.gpuSamples;
    stats.meshDraws = pass.meshDraws;
    stats.fullscreenDraws = pass.fullscreenDraws;
    stats.meshVertices = pass.meshVertices;
    stats.uniformBytes = pass.uniformBytes;
    stats.bufferDataBytes = pass.bufferDataBytes;
    stats.screenCopies = pass.screenCopies;
    stats.screenCopyPixels = pass.screenCopyPixels;
    stats.samples = pass.samples;
    stats.fbo = pass.fbo;
    for (int i = 0; i < 4; ++i) stats.viewport[i] = pass.viewport[i];
    stats.worldFrames = g_worldFrames.load();
    stats.screenFrames = g_screenFrames.load();
    stats.markersIssued = g_markersIssued.load();
    stats.markerHits = g_markerHits.load();
    for (int i = 0; i < 3; ++i) stats.cameras[i] = g_cameras[i].load();
    stats.installed = g_state > 0;
    stats.worldLayer = g_worldLayer;
    stats.overlay = g_overlay.load() != nullptr;
    stats.gpuTimer = fx::GpuTimer::Available();
    return stats;
}

}  // namespace pylon::graphics
