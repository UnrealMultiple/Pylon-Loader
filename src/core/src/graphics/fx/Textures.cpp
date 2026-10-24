// 层：内核层 / graphics::fx —— 纹理上传
#include "graphics/fx/Textures.h"

#include <memory>
#include <mutex>
#include <vector>

#include "graphics/GlState.h"
#include "graphics/fx/Frame.h"
#include "pylon/Log.h"

namespace pylon::graphics::fx {

namespace {

struct Entry {
    std::string key;
    bool linear = true, repeat = false;
    std::vector<uint8_t> pixels;  // 上传后清空
    Texture texture;
    bool uploaded = false, failed = false;
};

std::mutex g_mutex;
std::vector<std::unique_ptr<Entry>> g_entries;  // id = kFirstTexture - 下标

bool upload(Entry& entry) {
    const Gles& g = gles();
    ScopedState saved;
    g.GenTextures(1, &entry.texture.texture);
    g.GenSamplers(1, &entry.texture.sampler);

    g.BindBuffer(GL_PIXEL_UNPACK_BUFFER, 0);
    g.ActiveTexture(GL_TEXTURE0);
    g.BindTexture(GL_TEXTURE_2D, entry.texture.texture);

    // 解包状态是 Unity 的（它缓存），先记下再改，传完还回去。
    const GLenum names[4] = {GL_UNPACK_ALIGNMENT, GL_UNPACK_ROW_LENGTH, GL_UNPACK_SKIP_ROWS, GL_UNPACK_SKIP_PIXELS};
    GLint previous[4] = {4, 0, 0, 0};
    for (int i = 0; i < 4; ++i) g.GetIntegerv(names[i], &previous[i]);
    g.PixelStorei(GL_UNPACK_ALIGNMENT, 4);
    for (int i = 1; i < 4; ++i) g.PixelStorei(names[i], 0);
    g.TexImage2D(GL_TEXTURE_2D, 0, GL_RGBA8, entry.texture.width, entry.texture.height, 0, GL_RGBA,
                 GL_UNSIGNED_BYTE, entry.pixels.data());
    for (int i = 0; i < 4; ++i) g.PixelStorei(names[i], previous[i]);

    const GLint filter = entry.linear ? GL_LINEAR : GL_NEAREST;
    const GLint wrap = entry.repeat ? GL_REPEAT : GL_CLAMP_TO_EDGE;
    g.SamplerParameteri(entry.texture.sampler, GL_TEXTURE_MIN_FILTER, filter);
    g.SamplerParameteri(entry.texture.sampler, GL_TEXTURE_MAG_FILTER, filter);
    g.SamplerParameteri(entry.texture.sampler, GL_TEXTURE_WRAP_S, wrap);
    g.SamplerParameteri(entry.texture.sampler, GL_TEXTURE_WRAP_T, wrap);

    entry.pixels.clear();
    entry.pixels.shrink_to_fit();
    return true;
}

}  // namespace

int TextureStore::Load(const std::string& key, int width, int height, const uint8_t* rgba, bool linear,
                       bool repeat) {
    if (key.empty() || width <= 0 || height <= 0 || rgba == nullptr) {
        LOGE("fx", "纹理参数无效（key='%s' %dx%d）", key.c_str(), width, height);
        return 0;
    }
    {
        std::lock_guard<std::mutex> lock(g_mutex);
        for (size_t i = 0; i < g_entries.size(); ++i) {
            const Entry& e = *g_entries[i];
            if (e.key == key && e.linear == linear && e.repeat == repeat && !e.failed) {
                return kFirstTexture - static_cast<int>(i);
            }
        }
    }

    auto entry = std::make_unique<Entry>();
    entry->key = key;
    entry->linear = linear;
    entry->repeat = repeat;
    entry->texture.width = width;
    entry->texture.height = height;
    entry->pixels.assign(rgba, rgba + static_cast<size_t>(width) * height * 4);

    std::lock_guard<std::mutex> lock(g_mutex);
    g_entries.push_back(std::move(entry));
    return kFirstTexture - static_cast<int>(g_entries.size() - 1);
}

const Texture* TextureStore::Use(int id) {
    Entry* entry = nullptr;
    {
        std::lock_guard<std::mutex> lock(g_mutex);
        const int index = kFirstTexture - id;
        if (index < 0 || index >= static_cast<int>(g_entries.size())) return nullptr;
        entry = g_entries[index].get();
        if (entry->failed) return nullptr;
        if (entry->uploaded) return &entry->texture;
    }
    entry->uploaded = upload(*entry);
    entry->failed = !entry->uploaded;
    return entry->uploaded ? &entry->texture : nullptr;
}

}  // namespace pylon::graphics::fx
