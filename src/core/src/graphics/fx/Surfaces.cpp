// 层：内核层 / graphics::fx —— 离屏表面实现
#include "graphics/fx/Surfaces.h"

#include "graphics/Gles.h"
#include "graphics/GlState.h"
#include "pylon/Log.h"

namespace pylon::graphics::fx {

Surface::~Surface() {
    Release();
}

void Surface::Release() {
    if (texture_ == 0 && fbo_ == 0) return;
    const Gles& g = gles();
    if (fbo_ != 0) g.DeleteFramebuffers(1, &fbo_);
    if (texture_ != 0) g.DeleteTextures(1, &texture_);
    fbo_ = texture_ = 0;
    width_ = height_ = 0;
}

bool Surface::Ensure(GLsizei width, GLsizei height) {
    if (width <= 0 || height <= 0) return false;
    if (valid() && width_ == width && height_ == height) return true;

    const Gles& g = gles();
    ScopedState saved;
    if (texture_ == 0) g.GenTextures(1, &texture_);
    if (fbo_ == 0) g.GenFramebuffers(1, &fbo_);

    g.BindBuffer(GL_PIXEL_UNPACK_BUFFER, 0);
    g.ActiveTexture(GL_TEXTURE0);
    g.BindTexture(GL_TEXTURE_2D, texture_);
    g.TexImage2D(GL_TEXTURE_2D, 0, GL_RGBA8, width, height, 0, GL_RGBA, GL_UNSIGNED_BYTE, nullptr);
    g.BindFramebuffer(GL_DRAW_FRAMEBUFFER, fbo_);
    g.FramebufferTexture2D(GL_DRAW_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, texture_, 0);
    const GLenum status = g.CheckFramebufferStatus(GL_DRAW_FRAMEBUFFER);
    if (status != GL_FRAMEBUFFER_COMPLETE) {
        LOGE("fx", "离屏表面 %dx%d 的 FBO 不完整（0x%x）", width, height, status);
        width_ = height_ = 0;
        return false;
    }
    width_ = width;
    height_ = height;
    return true;
}

Surface* SurfacePool::Get(const std::string& key, GLsizei width, GLsizei height) {
    auto it = surfaces_.find(key);
    if (it == surfaces_.end()) {
        auto surface = std::make_unique<Surface>();
        it = surfaces_.emplace(key, std::move(surface)).first;
    }
    return it->second->Ensure(width, height) ? it->second.get() : nullptr;
}

void SurfacePool::Clear() {
    surfaces_.clear();
}

}  // namespace pylon::graphics::fx
