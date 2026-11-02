// 层：内核层 / imgui —— 纹理闸门实现
#include "imgui/overlay/Textures.h"

#include <deque>
#include <mutex>
#include <utility>
#include <vector>

#include "graphics/GlState.h"
#include "graphics/Gles.h"

namespace pylon::imgui {

namespace {

using graphics::gles;
using graphics::ScopedState;

constexpr int kPoolTarget = 32;

struct Upload {
    GLuint name = 0;
    int width = 0;
    int height = 0;
    std::vector<uint8_t> pixels;
};

std::mutex g_mutex;
std::deque<GLuint> g_freeNames;  // 渲染线程生成、游戏线程领走
std::deque<GLuint> g_retired;    // 游戏线程排队、渲染线程删
std::deque<Upload> g_uploads;    // 同上
bool g_ready = false;

void UploadOne(const Upload& upload) {
    const auto& g = gles();
    g.BindTexture(GL_TEXTURE_2D, upload.name);
    g.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
    g.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
    g.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
    g.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);
    g.TexImage2D(GL_TEXTURE_2D, 0, GL_RGBA, upload.width, upload.height, 0, GL_RGBA, GL_UNSIGNED_BYTE,
                 upload.pixels.data());
}

}  // namespace

bool Textures::Ready() {
    std::lock_guard<std::mutex> lock(g_mutex);
    return g_ready;
}

uint64_t Textures::Create(const uint8_t* rgba, int width, int height) {
    if (rgba == nullptr || width <= 0 || height <= 0) return 0;

    std::lock_guard<std::mutex> lock(g_mutex);
    if (!g_ready || g_freeNames.empty()) return 0;

    Upload upload;
    upload.name = g_freeNames.front();
    g_freeNames.pop_front();
    upload.width = width;
    upload.height = height;
    upload.pixels.assign(rgba, rgba + static_cast<size_t>(width) * static_cast<size_t>(height) * 4u);
    const uint64_t id = upload.name;
    g_uploads.push_back(std::move(upload));
    return id;
}

void Textures::Destroy(uint64_t id) {
    if (id == 0) return;
    std::lock_guard<std::mutex> lock(g_mutex);
    g_retired.push_back(static_cast<GLuint>(id));
}

void Textures::Apply() {
    std::deque<Upload> uploads;
    std::deque<GLuint> retired;
    std::vector<GLuint> fresh;
    {
        std::lock_guard<std::mutex> lock(g_mutex);
        const size_t missing = g_freeNames.size() < static_cast<size_t>(kPoolTarget)
                                   ? static_cast<size_t>(kPoolTarget) - g_freeNames.size()
                                   : 0;
        fresh.assign(missing, 0);
        std::swap(uploads, g_uploads);
        std::swap(retired, g_retired);
        g_ready = true;
    }

    ScopedState saved;  // 只借一下 GL 状态，改完原样还回去
    const auto& g = gles();
    if (!fresh.empty()) {
        g.GenTextures(static_cast<GLsizei>(fresh.size()), fresh.data());
        std::lock_guard<std::mutex> lock(g_mutex);
        for (GLuint name : fresh) {
            if (name != 0) g_freeNames.push_back(name);
        }
    }
    for (GLuint name : retired) g.DeleteTextures(1, &name);
    for (const Upload& upload : uploads) UploadOne(upload);
}

}  // namespace pylon::imgui
