// 层：内核层 / graphics —— 导出 ABI（薄壳：校验参数 + 转进 graphics）
#include "pylon/graphics/ScreenEffectsApi.h"

#include <cstring>
#include <mutex>
#include <utility>

#include "graphics/Pipe.h"
#include "graphics/Trace.h"
#include "graphics/fx/Shaders.h"
#include "graphics/fx/Textures.h"

namespace pylon::graphics {
namespace {

std::mutex g_builderMutex;
fx::Frame g_builder;

fx::Draw* drawAt(int32_t handle) {
    if (handle <= 0 || handle > static_cast<int32_t>(g_builder.draws.size())) return nullptr;
    return &g_builder.draws[static_cast<size_t>(handle) - 1];
}

}  // namespace
}  // namespace pylon::graphics

using namespace pylon::graphics;

extern "C" {

PYLON_FX_EXPORT int32_t PylonFx_Install() {
    return Pipe::Install() ? 1 : 0;
}

PYLON_FX_EXPORT int32_t PylonFx_Available() {
    return Pipe::Installed() ? 1 : 0;
}

PYLON_FX_EXPORT int32_t PylonFx_ShaderRegister(const char* name, const char* source) {
    if (name == nullptr || source == nullptr) return 0;
    return fx::ShaderStore::Register(name, source, false);
}

PYLON_FX_EXPORT int32_t PylonFx_ShaderRegisterMesh(const char* name, const char* source) {
    if (name == nullptr || source == nullptr) return 0;
    return fx::ShaderStore::Register(name, source, true);
}

PYLON_FX_EXPORT int32_t PylonFx_ShaderStatus(int32_t id, int32_t* state, char* error, int32_t errorCap) {
    std::string text;
    const fx::ShaderState state_ = fx::ShaderStore::State(id, &text);
    if (state != nullptr) *state = static_cast<int32_t>(state_);
    if (error != nullptr && errorCap > 0) {
        std::strncpy(error, text.c_str(), static_cast<size_t>(errorCap) - 1);
        error[errorCap - 1] = 0;
    }
    return static_cast<int32_t>(state_);
}

PYLON_FX_EXPORT int32_t PylonFx_TextureLoad(const char* key, int32_t width, int32_t height, const uint8_t* rgba,
                                            int32_t linear, int32_t repeat, int32_t* outWidth, int32_t* outHeight) {
    const int id =
        fx::TextureStore::Load(key != nullptr ? key : "", width, height, rgba, linear != 0, repeat != 0);
    if (id != 0 && outWidth != nullptr) *outWidth = width;
    if (id != 0 && outHeight != nullptr) *outHeight = height;
    return id;
}

PYLON_FX_EXPORT int32_t PylonFx_SubmitBegin(int32_t history) {
    std::lock_guard<std::mutex> lock(g_builderMutex);
    g_builder = fx::Frame();
    g_builder.history = history != 0;
    return 1;
}

PYLON_FX_EXPORT int32_t PylonFx_SubmitAddTarget(int32_t id, const char* name, float scale, int32_t persistent) {
    if (id <= 0 || name == nullptr || scale <= 0.f || scale > 1.f) return 0;
    std::lock_guard<std::mutex> lock(g_builderMutex);
    g_builder.targets.push_back({id, name, scale, persistent != 0});
    return 1;
}

PYLON_FX_EXPORT int32_t PylonFx_SubmitAddDraw(int32_t shader, int32_t layer, int32_t dest, int32_t source,
                                              const float* box, const float* center, float radius, float intensity,
                                              float progress, float age, const float* color) {
    if (shader <= 0) return 0;
    std::lock_guard<std::mutex> lock(g_builderMutex);
    fx::Draw draw;
    draw.shader = shader;
    draw.layer = layer >= 1 ? fx::Layer::Screen : fx::Layer::World;
    draw.dest = dest;
    draw.source = source;
    if (box != nullptr) std::memcpy(draw.box, box, sizeof draw.box);
    if (center != nullptr) std::memcpy(draw.center, center, sizeof draw.center);
    draw.radius = radius;
    draw.intensity = intensity;
    draw.progress = progress;
    draw.age = age;
    if (color != nullptr) std::memcpy(draw.color, color, sizeof draw.color);
    g_builder.draws.push_back(std::move(draw));
    return static_cast<int32_t>(g_builder.draws.size());
}

PYLON_FX_EXPORT int32_t PylonFx_SubmitAddUniform(int32_t draw, const char* name, int32_t components,
                                                 const float* values, int32_t count) {
    if (name == nullptr || values == nullptr || components < 1 || components > 4 || count <= 0) return 0;
    std::lock_guard<std::mutex> lock(g_builderMutex);
    fx::Draw* target = drawAt(draw);
    if (target == nullptr) return 0;
    fx::Uniform uniform;
    uniform.name = name;
    uniform.components = components;
    uniform.values.assign(values, values + static_cast<size_t>(count) * components);
    target->uniforms.push_back(std::move(uniform));
    return 1;
}

PYLON_FX_EXPORT int32_t PylonFx_SubmitAddTexture(int32_t draw, const char* name, int32_t texture) {
    if (name == nullptr || texture == fx::kTexNone) return 0;
    std::lock_guard<std::mutex> lock(g_builderMutex);
    fx::Draw* target = drawAt(draw);
    if (target == nullptr) return 0;
    target->textures.push_back({name, texture});
    return 1;
}

PYLON_FX_EXPORT int32_t PylonFx_SubmitAddMesh(int32_t shader, int32_t layer, int32_t dest, int32_t blend,
                                              int32_t count, const float* verts) {
    if (shader <= 0 || verts == nullptr || count < 3 || count % 3 != 0) return 0;
    std::lock_guard<std::mutex> lock(g_builderMutex);
    fx::Draw draw;
    draw.mesh = true;
    draw.shader = shader;
    draw.layer = layer >= 1 ? fx::Layer::Screen : fx::Layer::World;
    draw.dest = dest;
    draw.blend = blend == 1 ? fx::Blend::Alpha : (blend == 2 ? fx::Blend::Additive : fx::Blend::None);
    draw.verts.resize(static_cast<size_t>(count));
    for (int32_t i = 0; i < count; ++i) {
        const float* p = verts + static_cast<size_t>(i) * 12;
        fx::MeshVertex& v = draw.verts[static_cast<size_t>(i)];
        v.x = p[0];
        v.y = p[1];
        v.u = p[2];
        v.v = p[3];
        v.r = p[4];
        v.g = p[5];
        v.b = p[6];
        v.a = p[7];
        v.w = p[8];
        v.t = p[9];
        v.aux0 = p[10];
        v.aux1 = p[11];
    }
    g_builder.draws.push_back(std::move(draw));
    return static_cast<int32_t>(g_builder.draws.size());
}

PYLON_FX_EXPORT void PylonFx_SubmitEnd() {
    fx::Frame frame;
    {
        std::lock_guard<std::mutex> lock(g_builderMutex);
        std::swap(frame, g_builder);
    }
    Pipe::Publish(std::move(frame));
}

PYLON_FX_EXPORT void PylonFx_Stats(PylonFxStats* out) {
    if (out == nullptr) return;
    const PipelineStats s = Pipe::Stats();
    *out = PylonFxStats();
    out->frames = s.frames;
    out->draws = s.draws;
    out->boxPixels = s.boxPixels;
    out->cpuNs = s.cpuNs;
    out->gpuNs = s.gpuNs;
    out->gpuSamples = s.gpuSamples;
    out->worldFrames = s.worldFrames;
    out->screenFrames = s.screenFrames;
    out->markersIssued = s.markersIssued;
    out->markerHits = s.markerHits;
    out->worldPostRender = s.cameras[0];
    out->uiPostRender = s.cameras[1];
    out->otherPostRender = s.cameras[2];
    out->drawFbo = s.fbo;
    for (int i = 0; i < 4; ++i) out->viewport[i] = s.viewport[i];
    out->samples = s.samples;
    out->installed = s.installed ? 1 : 0;
    out->worldLayer = s.worldLayer ? 1 : 0;
    out->overlay = s.overlay ? 1 : 0;
    out->gpuTimer = s.gpuTimer ? 1 : 0;
}

PYLON_FX_EXPORT void PylonFx_RequestTrace() {
    trace::RequestFrame();
}

}  // extern "C"
