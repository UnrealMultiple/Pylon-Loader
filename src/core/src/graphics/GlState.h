// 层：内核层 / graphics —— Unity 的 GL 状态
//
// 一套模型：Capture 的 hooks 维护镜像，我们画完按镜像原样还回去。
// 建/删 GL 对象时用 ScopedState：那一步碰的状态镜像里没有，直接问驱动。
#pragma once

#include "graphics/Gles.h"

namespace pylon::graphics {

struct GlState {
    GLuint program = 0, vertexArray = 0, drawFbo = 0, readFbo = 0;
    GLenum activeTexture = GL_TEXTURE0;
    GLint viewport[4] = {0, 0, 0, 0};
    GLboolean colorMask[4] = {GL_TRUE, GL_TRUE, GL_TRUE, GL_TRUE};
    bool blend = false, depth = false, stencil = false, cull = false, scissor = false;
    GLenum blendSrcRgb = GL_ONE, blendDstRgb = GL_ZERO, blendSrcAlpha = GL_ONE, blendDstAlpha = GL_ZERO;
    GLenum blendEqRgb = GL_FUNC_ADD, blendEqAlpha = GL_FUNC_ADD;

    /// 问驱动一次（安装时用；之后镜像由 hooks 维护）。
    static GlState Query();

    void Restore() const;
};

/// 建对象期间的现场保存（会问驱动一次，析构时原样还）。
class ScopedState {
public:
    ScopedState();
    ~ScopedState();
    ScopedState(const ScopedState&) = delete;
    ScopedState& operator=(const ScopedState&) = delete;

private:
    GLint program_ = 0, activeTexture_ = GL_TEXTURE0, texture_ = 0, sampler_ = 0;
    GLint arrayBuffer_ = 0, unpackBuffer_ = 0, vertexArray_ = 0;
    GLint drawFbo_ = 0, readFbo_ = 0, viewport_[4] = {0, 0, 0, 0};
    GLboolean colorMask_[4] = {GL_TRUE, GL_TRUE, GL_TRUE, GL_TRUE};
    GLboolean blend_ = GL_FALSE, depth_ = GL_FALSE, stencil_ = GL_FALSE;
    GLboolean cull_ = GL_FALSE, scissor_ = GL_FALSE;
};

}  // namespace pylon::graphics
