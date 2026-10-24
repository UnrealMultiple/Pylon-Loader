#pragma once

#include "graphics/GlApi.h"

#include <cstddef>

namespace pylon::graphics {

/// 效果会动的 GL 状态（Unity 留下、效果要还回去的那点东西）。
struct GlShadow {
    GLuint program = 0, vertexArray = 0, drawFbo = 0, readFbo = 0;
    GLenum activeTexture = GL_TEXTURE0;
    GLint viewport[4] = {0, 0, 0, 0};
    bool blend = false, depth = false, stencil = false, cull = false, scissor = false;
    GLboolean colorMask[4] = {GL_TRUE, GL_TRUE, GL_TRUE, GL_TRUE};
};

/// 真值：直接问驱动。
GlShadow queryGlState();

/// 两个状态差几项；项名写进 `diff`（截断到 size）。
int compareGlState(const GlShadow& a, const GlShadow& b, char* diff, size_t size);

}  // namespace pylon::graphics
