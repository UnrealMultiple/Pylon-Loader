// 层：内核层 / graphics::fx —— 执行实现
#include "graphics/fx/Pass.h"

#include <algorithm>
#include <chrono>
#include <atomic>
#include <cstring>
#include <limits>
#include <cmath>
#include <mutex>
#include <string>
#include <unordered_map>

#include "graphics/GlState.h"
#include "graphics/Guard.h"
#include "graphics/fx/GpuTimer.h"
#include "graphics/fx/Shaders.h"
#include "graphics/fx/Surfaces.h"
#include "graphics/fx/Textures.h"
#include "pylon/Log.h"

namespace pylon::graphics::fx {

namespace {

struct Resources {
    bool tried = false, ok = false;
    GLuint quadVao = 0, quadVbo = 0;
    GLuint meshVao = 0, meshVbo = 0;
    GLuint streamVaos[3] = {}, streamVbos[3] = {};
    size_t streamCapacity[3] = {};
    unsigned streamNext = 0;
    std::vector<MeshVertex> staging;
    GLuint sampler = 0;
    Surface screen;
    Surface history;
    GLsizei historyWidth = 0, historyHeight = 0;
    SurfacePool pool;
    GLint samples = 0;
};

Resources g_res;
PassStats g_stats;
UploadStats g_uploads;
std::mutex g_statsMutex;
// Opt in for device A/B; the established upload path remains the default.
std::atomic<bool> g_streamMeshes{false};

struct Box {
    GLint x = 0, y = 0, w = 0, h = 0;
    bool contains(const Box& other) const {
        return w > 0 && other.x >= x && other.y >= y && other.x + other.w <= x + w && other.y + other.h <= y + h;
    }
};

Box pixelBox(const float* box, GLint width, GLint height) {
    const float fw = static_cast<float>(width), fh = static_cast<float>(height);
    const GLint x0 = std::max(0, static_cast<GLint>(std::floor(box[0] * fw)) - 2);
    const GLint y0 = std::max(0, static_cast<GLint>(std::floor(box[1] * fh)) - 2);
    const GLint x1 = std::min(width, static_cast<GLint>(std::ceil(box[2] * fw)) + 2);
    const GLint y1 = std::min(height, static_cast<GLint>(std::ceil(box[3] * fh)) + 2);
    return {x0, y0, std::max(0, x1 - x0), std::max(0, y1 - y0)};
}

float seconds() {
    using namespace std::chrono;
    static const auto start = steady_clock::now();
    return duration<float>(steady_clock::now() - start).count();
}

void setUniform(GLint location, const Uniform& uniform) {
    if (location < 0 || uniform.values.empty()) return;
    const Gles& g = gles();
    const GLsizei count = static_cast<GLsizei>(uniform.values.size() / std::max(1, uniform.components));
    switch (uniform.components) {
        case 1: g.Uniform1fv(location, count, uniform.values.data()); break;
        case 2: g.Uniform2fv(location, count, uniform.values.data()); break;
        case 3: g.Uniform3fv(location, count, uniform.values.data()); break;
        default: g.Uniform4fv(location, count, uniform.values.data()); break;
    }
}

class Runner {
public:
    Runner(const Frame& frame, const RenderTarget& target) : frame_(frame), target_(target) {}

    bool Prepare() {
        const GLint width = target_.viewport[2], height = target_.viewport[3];
        if (!g_res.screen.Ensure(width, height)) return false;
        for (const TargetSpec& spec : frame_.targets) {
            const GLint w = std::max(1, static_cast<GLint>(std::lround(width * spec.scale)));
            const GLint h = std::max(1, static_cast<GLint>(std::lround(height * spec.scale)));
            Surface* surface = g_res.pool.Get(spec.name + "@" + std::to_string(spec.scale), w, h);
            if (surface == nullptr) return false;
            surfaces_[spec.id] = surface;
        }
        if (!frame_.history) return true;
        if (!g_res.history.Ensure(width, height)) return false;
        if (g_res.historyWidth != width || g_res.historyHeight != height) {
            // 第一帧没有"上一帧"：清黑，着色器读到的是确定的底色。
            const Gles& g = gles();
            g.BindFramebuffer(GL_FRAMEBUFFER, g_res.history.fbo());
            g.ClearColor(0.f, 0.f, 0.f, 0.f);
            g.Clear(GL_COLOR_BUFFER_BIT);
            g_res.historyWidth = width;
            g_res.historyHeight = height;
        }
        return true;
    }

    int Run() {
        PrepareMeshes();
        const float now = seconds();
        int drawn = 0;
        for (const fx::Draw& draw : frame_.draws) {
            drawn += runDraw(draw, now) ? 1 : 0;
            // Advance for every mesh, including failed shaders/targets, so later offsets remain valid.
            if (draw.mesh) meshFirst_ += draw.verts.size();
        }
        if (frame_.history) SaveHistory();
        return drawn;
    }

private:
    void PrepareMeshes() {
        if (!g_streamMeshes.load(std::memory_order_relaxed)) return;
        size_t total = 0;
        for (const Draw& draw : frame_.draws) {
            if (!draw.mesh) continue;
            if (draw.verts.size() > static_cast<size_t>(std::numeric_limits<GLsizei>::max()) - total) return;
            total += draw.verts.size();
        }
        if (total == 0 || total > static_cast<size_t>(std::numeric_limits<GLsizeiptr>::max()) / sizeof(MeshVertex)) return;
        g_res.staging.resize(total);
        size_t offset = 0;
        for (const Draw& draw : frame_.draws) {
            if (!draw.mesh || draw.verts.empty()) continue;
            std::memcpy(g_res.staging.data() + offset, draw.verts.data(), draw.verts.size() * sizeof(MeshVertex));
            offset += draw.verts.size();
        }
        const Gles& g = gles();
        const unsigned slot = g_res.streamNext++ % 3;
        streamVao_ = g_res.streamVaos[slot];
        const size_t bytes = total * sizeof(MeshVertex);
        size_t& capacity = g_res.streamCapacity[slot];
        capacity = std::max(capacity, bytes);
        g.BindVertexArray(streamVao_);
        g.BindBuffer(GL_ARRAY_BUFFER, g_res.streamVbos[slot]);
        // Orphan even when rotating: three slots alone do not guarantee GPU completion.
        g.BufferData(GL_ARRAY_BUFFER, static_cast<GLsizeiptr>(capacity), nullptr, GL_STREAM_DRAW);
        g.BufferSubData(GL_ARRAY_BUFFER, 0, static_cast<GLsizeiptr>(bytes), g_res.staging.data());
        bufferDataCalls_++;
        bufferDataBytes_ += static_cast<int64_t>(capacity);
        bufferStorageBytes_ += static_cast<int64_t>(capacity);
        bufferSubDataCalls_++;
        bufferSubDataBytes_ += static_cast<int64_t>(bytes);
    }

    Surface* find(int id) {
        auto it = surfaces_.find(id);
        return it == surfaces_.end() ? nullptr : it->second;
    }

    struct Bound {
        GLuint texture = 0, sampler = 0;
        GLint width = 1, height = 1;
    };

    Bound texture(int id) {
        if (id == kTexScreen) return {g_res.screen.texture(), g_res.sampler, g_res.screen.width(), g_res.screen.height()};
        if (id == kTexHistory) {
            return {g_res.history.texture(), g_res.sampler, g_res.history.width(), g_res.history.height()};
        }
        if (id > 0) {
            const Surface* surface = find(id);
            return surface == nullptr ? Bound{}
                                      : Bound{surface->texture(), g_res.sampler, surface->width(), surface->height()};
        }
        if (id > kFirstTexture) return Bound{};
        const Texture* mod = TextureStore::Use(id);
        return mod == nullptr ? Bound{} : Bound{mod->texture, mod->sampler, mod->width, mod->height};
    }

    void bind(int unit, const Bound& texture) {
        if (texture.texture == 0) return;
        const Gles& g = gles();
        if (activeTexture_ != unit) {
            g.ActiveTexture(GL_TEXTURE0 + unit);
            activeTexture_ = unit;
        }
        if (boundTextures_[unit] != texture.texture) {
            g.BindTexture(GL_TEXTURE_2D, texture.texture);
            boundTextures_[unit] = texture.texture;
        }
        if (boundSamplers_[unit] != texture.sampler) {
            g.BindSampler(unit, texture.sampler);
            boundSamplers_[unit] = texture.sampler;
        }
    }

    /// 屏幕拷贝按盒子增量刷新：画面没变、盒子也在已拷范围内就不重做。
    void refreshCopy(const Box& box) {
        if (!dirty_ && copied_.contains(box)) return;
        const Gles& g = gles();
        g.BindFramebuffer(GL_READ_FRAMEBUFFER, target_.fbo);
        g.BindFramebuffer(GL_DRAW_FRAMEBUFFER, g_res.screen.fbo());
        const GLint vx = target_.viewport[0], vy = target_.viewport[1];
        g.BlitFramebuffer(vx + box.x, vy + box.y, vx + box.x + box.w, vy + box.y + box.h, box.x, box.y,
                          box.x + box.w, box.y + box.h, GL_COLOR_BUFFER_BIT, GL_NEAREST);
        screenCopies_++;
        screenCopyPixels_ += static_cast<int64_t>(box.w) * box.h;
        copied_ = box;
        dirty_ = false;
    }

    bool readsScreen(const Draw& draw) const {
        if (draw.source == kTexScreen || draw.source == kTexHistory) return true;
        for (const TextureBinding& binding : draw.textures) {
            if (binding.texture == kTexScreen || binding.texture == kTexHistory) return true;
        }
        return false;
    }

    void setBuiltins(Program& program, const Draw& draw, float now) {
        const Gles& g = gles();
        const BuiltinUniforms& b = program.builtin;
        const float vw = static_cast<float>(target_.viewport[2]), vh = static_cast<float>(target_.viewport[3]);
        if (b.screenTexel >= 0) g.Uniform2f(b.screenTexel, 1.f / vw, 1.f / vh);
        if (b.view >= 0) {
            g.Uniform4f(b.view, static_cast<float>(target_.viewport[0]), static_cast<float>(target_.viewport[1]), vw, vh);
        }
        if (b.resolution >= 0) g.Uniform2f(b.resolution, vw, vh);
        if (b.aspect >= 0) g.Uniform1f(b.aspect, vw / vh);
        if (b.time >= 0) g.Uniform1f(b.time, now);
        if (b.age >= 0) g.Uniform1f(b.age, draw.age);
        if (b.progress >= 0) g.Uniform1f(b.progress, draw.progress);
        if (b.intensity >= 0) g.Uniform1f(b.intensity, draw.intensity);
        if (b.center >= 0) g.Uniform2f(b.center, draw.center[0], draw.center[1]);
        if (b.radius >= 0) g.Uniform1f(b.radius, draw.radius);
        if (b.color >= 0) g.Uniform4f(b.color, draw.color[0], draw.color[1], draw.color[2], draw.color[3]);
        for (const Uniform& uniform : draw.uniforms) {
            uniformBytes_ += static_cast<int64_t>(uniform.values.size() * sizeof(float));
            setUniform(ShaderStore::Uniform(program, uniform.name), uniform);
        }
    }

    void bindModTextures(Program& program, const Draw& draw) {
        const Gles& g = gles();
        int unit = 0;
        for (const TextureBinding& binding : draw.textures) {
            const GLint location = ShaderStore::Uniform(program, binding.name);
            if (location < 0 || unit >= kUnitModCount) continue;
            const int slot = kUnitMod - unit++;
            bind(slot, texture(binding.texture));
            g.Uniform1i(location, slot);
        }
    }

    bool runDraw(const fx::Draw& draw, float now) {
        Program* program = ShaderStore::Use(draw.shader);
        if (program == nullptr) return false;
        Surface* dest = draw.dest > 0 ? find(draw.dest) : nullptr;
        if (draw.dest > 0 && dest == nullptr) return false;
        if (draw.mesh) return runMesh(draw, *program, dest, now);

        const GLint dw = dest != nullptr ? dest->width() : target_.viewport[2];
        const GLint dh = dest != nullptr ? dest->height() : target_.viewport[3];
        const Box box = pixelBox(draw.box, dw, dh);
        if (box.w <= 0 || box.h <= 0) return false;
        fullscreenDraws_++;

        const Gles& g = gles();
        g.Disable(GL_BLEND);
        // The common shader prelude declares uScreen for every fragment shader,
        // including geometry-only Xihe passes that never sample it. Only an
        // explicit screen/history binding requires a copy; the old builtin check
        // forced a full screen blit for every primitive draw.
        if (readsScreen(draw) || program->readsScreen) {
            refreshCopy(pixelBox(draw.box, target_.viewport[2], target_.viewport[3]));
        }

        if (dest != nullptr) {
            g.BindFramebuffer(GL_FRAMEBUFFER, dest->fbo());
            g.Viewport(box.x, box.y, box.w, box.h);
        } else {
            g.BindFramebuffer(GL_FRAMEBUFFER, target_.fbo);
            g.Viewport(target_.viewport[0] + box.x, target_.viewport[1] + box.y, box.w, box.h);
        }
        g.UseProgram(program->id);

        bind(kUnitScreen, texture(kTexScreen));
        bind(kUnitSource, texture(draw.source));
        bindModTextures(*program, draw);
        setBuiltins(*program, draw, now);

        const float fw = static_cast<float>(dw), fh = static_cast<float>(dh);
        if (program->builtin.box >= 0) {
            g.Uniform4f(program->builtin.box, box.x / fw, box.y / fh, (box.x + box.w) / fw, (box.y + box.h) / fh);
        }
        const Bound source = texture(draw.source);
        if (program->builtin.sourceTexel >= 0 && source.texture != 0) {
            g.Uniform2f(program->builtin.sourceTexel, 1.f / static_cast<float>(source.width),
                        1.f / static_cast<float>(source.height));
        }

        g.BindVertexArray(g_res.quadVao);
        g.DrawArrays(GL_TRIANGLE_STRIP, 0, 4);
        if (dest == nullptr) dirty_ = true;
        pixels_ += static_cast<int64_t>(box.w) * box.h;
        return true;
    }

    bool runMesh(const fx::Draw& draw, Program& program, Surface* dest, float now) {
        if (draw.verts.empty()) return false;
        const Gles& g = gles();

        if (dest != nullptr) {
            g.BindFramebuffer(GL_FRAMEBUFFER, dest->fbo());
            g.Viewport(0, 0, dest->width(), dest->height());
        } else {
            g.BindFramebuffer(GL_FRAMEBUFFER, target_.fbo);
            g.Viewport(target_.viewport[0], target_.viewport[1], target_.viewport[2], target_.viewport[3]);
        }
        g.UseProgram(program.id);
        g.Disable(GL_DEPTH_TEST);
        g.Disable(GL_STENCIL_TEST);
        g.Disable(GL_CULL_FACE);
        g.Disable(GL_SCISSOR_TEST);

        bindModTextures(program, draw);
        setBuiltins(program, draw, now);

        switch (draw.blend) {
            case Blend::Alpha:
                g.Enable(GL_BLEND);
                g.BlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
                break;
            case Blend::Premultiplied:
                g.Enable(GL_BLEND);
                g.BlendFunc(GL_ONE, GL_ONE_MINUS_SRC_ALPHA);
                break;
            case Blend::Additive:
                g.Enable(GL_BLEND);
                g.BlendFunc(GL_SRC_ALPHA, GL_ONE);
                break;
            default:
                g.Disable(GL_BLEND);
                break;
        }

        if (streamVao_ != 0) {
            g.BindVertexArray(streamVao_);
            g.DrawArrays(GL_TRIANGLES, static_cast<GLint>(meshFirst_), static_cast<GLsizei>(draw.verts.size()));
        } else {
            g.BindVertexArray(g_res.meshVao);
            g.BindBuffer(GL_ARRAY_BUFFER, g_res.meshVbo);
            const auto bytes = static_cast<int64_t>(draw.verts.size() * sizeof(MeshVertex));
            g.BufferData(GL_ARRAY_BUFFER, static_cast<GLsizeiptr>(bytes), draw.verts.data(), GL_STREAM_DRAW);
            bufferDataBytes_ += bytes;
            bufferStorageBytes_ += bytes;
            bufferDataCalls_++;
            g.DrawArrays(GL_TRIANGLES, 0, static_cast<GLsizei>(draw.verts.size()));
        }
        meshDraws_++;
        meshVertices_ += static_cast<int64_t>(draw.verts.size());
        if (dest == nullptr) dirty_ = true;
        pixels_ += static_cast<int64_t>(draw.verts.size());
        return true;
    }

    void SaveHistory() {
        const GLint width = target_.viewport[2], height = target_.viewport[3];
        if (!g_res.history.Ensure(width, height)) return;
        const Gles& g = gles();
        g.BindFramebuffer(GL_READ_FRAMEBUFFER, target_.fbo);
        g.BindFramebuffer(GL_DRAW_FRAMEBUFFER, g_res.history.fbo());
        g.BlitFramebuffer(target_.viewport[0], target_.viewport[1], target_.viewport[0] + width,
                          target_.viewport[1] + height, 0, 0, width, height, GL_COLOR_BUFFER_BIT, GL_NEAREST);
    }

    const Frame& frame_;
    RenderTarget target_;
    std::unordered_map<int, Surface*> surfaces_;
    Box copied_;
    bool dirty_ = true;
    int64_t pixels_ = 0;
    int64_t meshDraws_ = 0;
    int64_t fullscreenDraws_ = 0;
    int64_t meshVertices_ = 0;
    int64_t uniformBytes_ = 0;
    int64_t bufferDataBytes_ = 0;
    int64_t screenCopies_ = 0;
    int64_t screenCopyPixels_ = 0;
    GLuint streamVao_ = 0;
    size_t meshFirst_ = 0;
    int64_t bufferSubDataBytes_ = 0, bufferStorageBytes_ = 0;
    int64_t bufferDataCalls_ = 0, bufferSubDataCalls_ = 0;
    int activeTexture_ = -1;
    GLuint boundTextures_[16] = {};
    GLuint boundSamplers_[16] = {};

public:
    int64_t bufferSubDataBytes() const { return bufferSubDataBytes_; }
    int64_t bufferStorageBytes() const { return bufferStorageBytes_; }
    int64_t bufferDataCalls() const { return bufferDataCalls_; }
    int64_t bufferSubDataCalls() const { return bufferSubDataCalls_; }
    int64_t pixels() const { return pixels_; }
    int64_t meshDraws() const { return meshDraws_; }
    int64_t fullscreenDraws() const { return fullscreenDraws_; }
    int64_t meshVertices() const { return meshVertices_; }
    int64_t uniformBytes() const { return uniformBytes_; }
    int64_t bufferDataBytes() const { return bufferDataBytes_; }
    int64_t screenCopies() const { return screenCopies_; }
    int64_t screenCopyPixels() const { return screenCopyPixels_; }
};

bool initResources() {
    if (g_res.tried) return g_res.ok;
    g_res.tried = true;
    const Gles& g = gles();
    ScopedState saved;

    static const float quad[8] = {-1.f, -1.f, 1.f, -1.f, -1.f, 1.f, 1.f, 1.f};
    g.GenVertexArrays(1, &g_res.quadVao);
    g.BindVertexArray(g_res.quadVao);
    g.GenBuffers(1, &g_res.quadVbo);
    g.BindBuffer(GL_ARRAY_BUFFER, g_res.quadVbo);
    g.BufferData(GL_ARRAY_BUFFER, sizeof quad, quad, GL_STATIC_DRAW);
    g.VertexAttribPointer(0, 2, GL_FLOAT, GL_FALSE, 0, nullptr);
    g.EnableVertexAttribArray(0);

    const GLsizei stride = 12 * sizeof(float);
    g.GenVertexArrays(1, &g_res.meshVao);
    g.BindVertexArray(g_res.meshVao);
    g.GenBuffers(1, &g_res.meshVbo);
    g.BindBuffer(GL_ARRAY_BUFFER, g_res.meshVbo);
    g.VertexAttribPointer(0, 2, GL_FLOAT, GL_FALSE, stride, nullptr);
    g.VertexAttribPointer(1, 2, GL_FLOAT, GL_FALSE, stride, reinterpret_cast<const void*>(2 * sizeof(float)));
    g.VertexAttribPointer(2, 4, GL_FLOAT, GL_FALSE, stride, reinterpret_cast<const void*>(4 * sizeof(float)));
    g.VertexAttribPointer(3, 4, GL_FLOAT, GL_FALSE, stride, reinterpret_cast<const void*>(8 * sizeof(float)));
    g.EnableVertexAttribArray(0);
    g.EnableVertexAttribArray(1);
    g.EnableVertexAttribArray(2);
    g.EnableVertexAttribArray(3);

    g.GenVertexArrays(3, g_res.streamVaos);
    g.GenBuffers(3, g_res.streamVbos);
    for (int i = 0; i < 3; ++i) {
        g.BindVertexArray(g_res.streamVaos[i]);
        g.BindBuffer(GL_ARRAY_BUFFER, g_res.streamVbos[i]);
        g.VertexAttribPointer(0, 2, GL_FLOAT, GL_FALSE, stride, nullptr);
        g.VertexAttribPointer(1, 2, GL_FLOAT, GL_FALSE, stride, reinterpret_cast<const void*>(2 * sizeof(float)));
        g.VertexAttribPointer(2, 4, GL_FLOAT, GL_FALSE, stride, reinterpret_cast<const void*>(4 * sizeof(float)));
        g.VertexAttribPointer(3, 4, GL_FLOAT, GL_FALSE, stride, reinterpret_cast<const void*>(8 * sizeof(float)));
        for (GLuint attribute = 0; attribute < 4; ++attribute) g.EnableVertexAttribArray(attribute);
    }

    g.GenSamplers(1, &g_res.sampler);
    g.SamplerParameteri(g_res.sampler, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
    g.SamplerParameteri(g_res.sampler, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
    g.SamplerParameteri(g_res.sampler, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
    g.SamplerParameteri(g_res.sampler, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);

    const char* version = reinterpret_cast<const char*>(g.GetString(GL_VERSION));
    const char* renderer = reinterpret_cast<const char*>(g.GetString(GL_RENDERER));
    GLint samples = 0;
    g.GetIntegerv(GL_SAMPLES, &samples);
    g_res.samples = samples;
    g_res.ok = true;
    LOGI("fx", "效果管线就绪：GL '%s' / '%s'，MSAA %d", version != nullptr ? version : "?",
         renderer != nullptr ? renderer : "?", samples);
    return true;
}

}  // namespace

void Pass::Execute(const Frame& frame, const RenderTarget& target) {
    if (frame.empty() || target.viewport[2] <= 0 || target.viewport[3] <= 0) return;
    if (!initResources()) return;

    OwnDraw own;
    const auto started = std::chrono::steady_clock::now();
    const Gles& g = gles();
    g.Disable(GL_SCISSOR_TEST);
    g.Disable(GL_BLEND);
    g.Disable(GL_DEPTH_TEST);
    g.Disable(GL_STENCIL_TEST);
    g.Disable(GL_CULL_FACE);
    g.ColorMask(GL_TRUE, GL_TRUE, GL_TRUE, GL_TRUE);
    GpuTimer::Begin(Channel::Effect);

    Runner runner(frame, target);
    int drawn = 0;
    if (runner.Prepare()) drawn = runner.Run();

    GpuTimer::End(Channel::Effect);
    const int64_t cpuNs = std::chrono::duration_cast<std::chrono::nanoseconds>(
                              std::chrono::steady_clock::now() - started)
                              .count();

    std::lock_guard<std::mutex> lock(g_statsMutex);
    g_stats.frames++;
    g_stats.draws += drawn;
    g_stats.boxPixels += runner.pixels();
    g_stats.cpuNs += cpuNs;
    g_stats.meshDraws += runner.meshDraws();
    g_stats.fullscreenDraws += runner.fullscreenDraws();
    g_stats.meshVertices += runner.meshVertices();
    g_stats.uniformBytes += runner.uniformBytes();
    g_stats.bufferDataBytes += runner.bufferDataBytes();
    g_stats.screenCopies += runner.screenCopies();
    g_stats.screenCopyPixels += runner.screenCopyPixels();
    g_uploads.bufferSubDataBytes += runner.bufferSubDataBytes();
    g_uploads.bufferStorageBytes += runner.bufferStorageBytes();
    g_uploads.bufferDataCalls += runner.bufferDataCalls();
    g_uploads.bufferSubDataCalls += runner.bufferSubDataCalls();
    g_stats.fbo = target.fbo;
    for (int i = 0; i < 4; ++i) g_stats.viewport[i] = target.viewport[i];
}

void Pass::SetMeshStreaming(bool enabled) {
    g_streamMeshes.store(enabled, std::memory_order_relaxed);
}

UploadStats Pass::Uploads() {
    UploadStats copy;
    std::lock_guard<std::mutex> lock(g_statsMutex);
    copy.bufferSubDataBytes = g_uploads.bufferSubDataBytes;
    copy.bufferStorageBytes = g_uploads.bufferStorageBytes;
    copy.bufferDataCalls = g_uploads.bufferDataCalls;
    copy.bufferSubDataCalls = g_uploads.bufferSubDataCalls;
    return copy;
}

PassStats Pass::Stats() {
    PassStats copy;
    {
        std::lock_guard<std::mutex> lock(g_statsMutex);
        copy = g_stats;
    }
    copy.samples = g_res.samples;
    copy.ready = g_res.ok;
    GpuTimer::Totals(Channel::Effect, copy.gpuNs, copy.gpuSamples);
    return copy;
}

}  // namespace pylon::graphics::fx
