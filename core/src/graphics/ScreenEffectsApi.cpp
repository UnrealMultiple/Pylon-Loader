// 层：内核层 / graphics —— 导出 ABI 实现（薄壳：参数校验 + 转进 graphics 模块）
#include "pylon/graphics/ScreenEffectsApi.h"

#include <cstring>
#include <mutex>

#include "graphics/EffectTypes.h"
#include "graphics/GlApi.h"
#include "graphics/GlHooks.h"
#include "graphics/RenderHook.h"
#include "graphics/ShaderRegistry.h"
#include "graphics/TextureRegistry.h"
#include "pylon/Log.h"

namespace pylon::graphics {
namespace {

std::mutex g_submitMutex;  // builder 状态（游戏线程独占，锁只防误用）
EffectFrame g_submitFrame;

}  // namespace
}  // namespace pylon::graphics

using namespace pylon::graphics;

extern "C" {

PYLON_FX_EXPORT int32_t PylonFx_Install() {
    static bool tried = false;
    static bool ok = false;
    if (tried) return ok ? 1 : 0;
    tried = true;
    installRenderHook();
    ok = renderHookInstalled();
    return ok ? 1 : 0;
}

PYLON_FX_EXPORT int32_t PylonFx_Available() {
    return gl().load() && renderHookInstalled() ? 1 : 0;
}

PYLON_FX_EXPORT int32_t PylonFx_ShaderRegister(const char* name, const char* source) {
    if (!name || !source) return 0;
    return ShaderRegistry::Register(name, source);
}

PYLON_FX_EXPORT int32_t PylonFx_ShaderStatus(int32_t id, int32_t* state, char* error, int32_t errorCap) {
    std::string text;
    const ShaderState s = ShaderRegistry::Status(id, &text);
    if (state) *state = static_cast<int32_t>(s);
    if (error && errorCap > 0) {
        std::strncpy(error, text.c_str(), static_cast<size_t>(errorCap) - 1);
        error[errorCap - 1] = 0;
    }
    return static_cast<int32_t>(s);
}

PYLON_FX_EXPORT int32_t PylonFx_TextureLoad(const char* key, int32_t width, int32_t height,
                                            const uint8_t* rgba, int32_t linear, int32_t repeat,
                                            int32_t* outWidth, int32_t* outHeight) {
    const int id = TextureRegistry::Load(key ? key : "", width, height, rgba, linear != 0, repeat != 0);
    if (id != 0 && outWidth) *outWidth = width;
    if (id != 0 && outHeight) *outHeight = height;
    return id;
}

PYLON_FX_EXPORT int32_t PylonFx_SubmitBegin(int32_t history) {
    std::lock_guard<std::mutex> lock(g_submitMutex);
    g_submitFrame = EffectFrame();
    g_submitFrame.history = history != 0;
    return 1;
}

PYLON_FX_EXPORT int32_t PylonFx_SubmitAddTarget(int32_t id, const char* name, float scale, int32_t persistent) {
    if (id <= 0 || !name || scale <= 0.f || scale > 1.f) return 0;
    std::lock_guard<std::mutex> lock(g_submitMutex);
    g_submitFrame.targets.push_back({id, name, scale, persistent != 0});
    return 1;
}

PYLON_FX_EXPORT int32_t PylonFx_SubmitAddDraw(int32_t shader, int32_t layer, int32_t dest, int32_t source,
                                              const float* box, const float* center, float radius,
                                              float intensity, float progress, float age, const float* color) {
    if (shader <= 0) return 0;
    std::lock_guard<std::mutex> lock(g_submitMutex);
    EffectDraw d;
    d.shader = shader;
    d.layer = layer >= 1 ? EffectLayer::Screen : EffectLayer::World;
    d.dest = dest;
    d.source = source;
    if (box) std::memcpy(d.box, box, sizeof d.box);
    if (center) std::memcpy(d.center, center, sizeof d.center);
    d.radius = radius;
    d.intensity = intensity;
    d.progress = progress;
    d.age = age;
    if (color) std::memcpy(d.color, color, sizeof d.color);
    g_submitFrame.draws.push_back(std::move(d));
    return static_cast<int32_t>(g_submitFrame.draws.size());
}

PYLON_FX_EXPORT int32_t PylonFx_SubmitAddUniform(int32_t draw, const char* name, int32_t components,
                                                 const float* values, int32_t count) {
    if (draw <= 0 || !name || !values || components < 1 || components > 4 || count <= 0) return 0;
    std::lock_guard<std::mutex> lock(g_submitMutex);
    if (draw > static_cast<int32_t>(g_submitFrame.draws.size())) return 0;
    UniformValue u;
    u.name = name;
    u.components = components;
    u.v.assign(values, values + static_cast<size_t>(count) * components);
    g_submitFrame.draws[draw - 1].uniforms.push_back(std::move(u));
    return 1;
}

PYLON_FX_EXPORT int32_t PylonFx_SubmitAddTexture(int32_t draw, const char* name, int32_t texture) {
    if (draw <= 0 || !name || texture == kTexNone) return 0;
    std::lock_guard<std::mutex> lock(g_submitMutex);
    if (draw > static_cast<int32_t>(g_submitFrame.draws.size())) return 0;
    g_submitFrame.draws[draw - 1].textures.push_back({name, texture});
    return 1;
}

PYLON_FX_EXPORT void PylonFx_SubmitEnd() {
    EffectFrame frame;
    {
        std::lock_guard<std::mutex> lock(g_submitMutex);
        std::swap(frame, g_submitFrame);
    }
    submitEffects(std::move(frame));
}

PYLON_FX_EXPORT void PylonFx_Stats(PylonFxStats* out) {
    if (!out) return;
    const RenderStats s = renderStats();
    out->worldPostRender = s.worldPostRender;
    out->uiPostRender = s.uiPostRender;
    out->otherPostRender = s.otherPostRender;
    out->draws = s.draws;
    out->frames = s.frames;
    out->markersIssued = s.markersIssued;
    out->markerHits = s.markerHits;
    out->screenFrames = s.screenFrames;
    out->renderCpuNs = s.renderCpuNs;
    out->markerCpuNs = s.markerCpuNs;
    out->gpuNs = s.gpuNs;
    out->gpuSamples = s.gpuSamples;
    out->gpuFrameNs = s.gpuFrameNs;
    out->gpuFrameSamples = s.gpuFrameSamples;
    out->shadowChecks = s.shadowChecks;
    out->shadowMismatches = s.shadowMismatches;
    out->boxPixels = s.boxPixels;
    out->drawFbo = s.drawFbo;
    out->readFbo = s.readFbo;
    for (int i = 0; i < 4; ++i) out->viewport[i] = s.viewport[i];
    out->samples = s.samples;
    out->blitError = s.blitError;
    out->copyFallbacks = s.copyFallbacks;
    out->hookInstalled = s.hookInstalled ? 1 : 0;
    out->hasContext = s.hasContext ? 1 : 0;
    out->markerReady = s.markerReady ? 1 : 0;
    out->gpuTimer = s.gpuTimer ? 1 : 0;
}

PYLON_FX_EXPORT void PylonFx_RequestTrace() {
    requestGlTrace();
}

}  // extern "C"
