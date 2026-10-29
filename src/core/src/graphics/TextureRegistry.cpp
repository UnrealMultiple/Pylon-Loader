// 层：内核层 / graphics —— 纹理注册实现（RGBA 来自 C#，GL 上传在渲染线程）
#include "graphics/TextureRegistry.h"
#include "graphics/GlState.h"

#include <cstring>
#include <memory>
#include <mutex>
#include <vector>

#include "pylon/Log.h"

namespace pylon::graphics {

namespace {

struct Entry {
    std::string key;
    bool linear = true, repeat = false;
    std::vector<uint8_t> pixels;  // 上传前存这；之后清空
    LoadedTexture gl;
    bool uploaded = false, failed = false;
};

std::mutex g_mutex;
std::vector<std::unique_ptr<Entry>> g_entries;  // id = kFirstModTexture - 下标

bool upload(Entry& e) {
    auto& g = gl();
    GlStateGuard saved;
    g.GenTextures(1, &e.gl.texture);
    g.BindBuffer(GL_PIXEL_UNPACK_BUFFER, 0);
    g.ActiveTexture(kScreenUnit);
    g.BindTexture(GL_TEXTURE_2D, e.gl.texture);
    // 解包状态是 Unity 的（它缓存）：把上传要用的先记下再改，传完还回去。
    const GLenum unpack[4] = {GL_UNPACK_ALIGNMENT, GL_UNPACK_ROW_LENGTH, GL_UNPACK_SKIP_ROWS, GL_UNPACK_SKIP_PIXELS};
    GLint previous[4] = {4, 0, 0, 0};
    for (int i = 0; i < 4; ++i) g.GetIntegerv(unpack[i], &previous[i]);
    g.PixelStorei(GL_UNPACK_ALIGNMENT, 4);
    for (int i = 1; i < 4; ++i) g.PixelStorei(unpack[i], 0);
    g.TexImage2D(GL_TEXTURE_2D, 0, GL_RGBA8, e.gl.width, e.gl.height, 0, GL_RGBA, GL_UNSIGNED_BYTE, e.pixels.data());
    for (int i = 0; i < 4; ++i) g.PixelStorei(unpack[i], previous[i]);

    const GLint filter = e.linear ? GL_LINEAR : GL_NEAREST;
    const GLint wrap = e.repeat ? GL_REPEAT : GL_CLAMP_TO_EDGE;
    g.GenSamplers(1, &e.gl.sampler);
    g.SamplerParameteri(e.gl.sampler, GL_TEXTURE_MIN_FILTER, filter);
    g.SamplerParameteri(e.gl.sampler, GL_TEXTURE_MAG_FILTER, filter);
    g.SamplerParameteri(e.gl.sampler, GL_TEXTURE_WRAP_S, wrap);
    g.SamplerParameteri(e.gl.sampler, GL_TEXTURE_WRAP_T, wrap);
    e.pixels.clear();
    e.pixels.shrink_to_fit();
    return true;
}

}  // namespace

int TextureRegistry::Load(const std::string& key, int width, int height, const uint8_t* rgba, bool linear, bool repeat) {
    if (key.empty() || width <= 0 || height <= 0 || rgba == nullptr) {
        LOGE("fx", "屏幕效果纹理：参数无效（key='%s' %dx%d rgba=%p）", key.c_str(), width, height,
             static_cast<const void*>(rgba));
        return 0;
    }
    {
        std::lock_guard<std::mutex> lock(g_mutex);
        for (size_t i = 0; i < g_entries.size(); ++i) {
            const Entry& e = *g_entries[i];
            if (e.key != key || e.linear != linear || e.repeat != repeat || e.failed) continue;
            return kFirstModTexture - static_cast<int>(i);
        }
    }

    auto e = std::make_unique<Entry>();
    e->key = key;
    e->linear = linear;
    e->repeat = repeat;
    e->gl.width = width;
    e->gl.height = height;
    e->pixels.assign(rgba, rgba + static_cast<size_t>(width) * height * 4);

    std::lock_guard<std::mutex> lock(g_mutex);
    g_entries.push_back(std::move(e));
    return kFirstModTexture - static_cast<int>(g_entries.size() - 1);
}

const LoadedTexture* TextureRegistry::Use(int id) {
    Entry* e = nullptr;
    {
        std::lock_guard<std::mutex> lock(g_mutex);
        const int index = kFirstModTexture - id;
        if (index < 0 || index >= static_cast<int>(g_entries.size())) return nullptr;
        e = g_entries[index].get();
        if (e->failed) return nullptr;
        if (e->uploaded) return &e->gl;
    }
    // 上传只发生在渲染线程，也只有它改 `uploaded`；pixels 在 Load 之后不再变。
    e->uploaded = upload(*e);
    return e->uploaded ? &e->gl : nullptr;
}

}  // namespace pylon::graphics
