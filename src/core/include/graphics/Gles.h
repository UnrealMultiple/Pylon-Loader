// 层：内核层 / graphics —— GLES + EGL 入口表
//
// 一次解析、一处失败处理：缺任何一个函数，整条图形管线在安装阶段就关掉，
// 运行期不再有"这个函数可能是空的"分支。
#pragma once

#include <EGL/egl.h>
#include <GLES3/gl3.h>

namespace pylon::graphics {

#define PYLON_GL_FUNCS(X)                                                     \
    X(GetIntegerv) X(GetBooleanv) X(GetString) X(IsEnabled)                    \
    X(GetFramebufferAttachmentParameteriv)                                      \
    X(Enable) X(Disable) X(ColorMask) X(Viewport) X(Scissor)                   \
    X(UseProgram) X(ActiveTexture) X(BindTexture) X(BindSampler)               \
    X(BindBuffer) X(BindVertexArray) X(BindFramebuffer)                        \
    X(GenTextures) X(TexImage2D) X(TexParameteri) X(DeleteTextures)            \
    X(GenSamplers) X(SamplerParameteri) X(DeleteSamplers) X(PixelStorei)       \
    X(GenFramebuffers) X(DeleteFramebuffers)                                   \
    X(FramebufferTexture2D) X(CheckFramebufferStatus) X(BlitFramebuffer)       \
    X(GenVertexArrays) X(GenBuffers) X(BufferData) X(BufferSubData)                             \
    X(VertexAttribPointer)                                                     \
    X(EnableVertexAttribArray)                                                 \
    X(CreateShader) X(ShaderSource) X(CompileShader) X(GetShaderiv)            \
    X(GetShaderInfoLog) X(DeleteShader) X(CreateProgram) X(AttachShader)       \
    X(LinkProgram) X(GetProgramiv) X(GetProgramInfoLog) X(DeleteProgram)       \
    X(GetUniformLocation) X(Uniform1i) X(Uniform1f) X(Uniform2f) X(Uniform3f)  \
    X(Uniform4f) X(Uniform1fv) X(Uniform2fv) X(Uniform3fv) X(Uniform4fv)       \
    X(DrawArrays) X(DrawElements) X(Clear) X(ClearColor) X(BlendFunc)           \
    X(BlendFuncSeparate) X(BlendEquation) X(BlendEquationSeparate)

/// 表面像素尺寸（EGL_WIDTH / EGL_HEIGHT）。
constexpr int kEglWidth = 0x3057;
constexpr int kEglHeight = 0x3056;

struct Gles {
#define PYLON_GL_SLOT(name) decltype(&::gl##name) name = nullptr;
    PYLON_GL_FUNCS(PYLON_GL_SLOT)
#undef PYLON_GL_SLOT

    EGLContext (*GetCurrentContext)() = nullptr;
    EGLBoolean (*SwapBuffers)(EGLDisplay display, EGLSurface surface) = nullptr;
    EGLBoolean (*QuerySurface)(EGLDisplay display, EGLSurface surface, EGLint attribute, EGLint* value) = nullptr;
    void* (*GetProcAddress)(const char* name) = nullptr;

    /// 缺函数返回 nullptr（缺谁已写日志）。幂等。
    static const Gles* Load();
};

/// 只在 Load() 成功之后调用。
const Gles& gles();

}  // namespace pylon::graphics
