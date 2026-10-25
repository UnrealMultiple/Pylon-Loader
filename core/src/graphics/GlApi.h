#pragma once

#include <GLES3/gl3.h>

namespace pylon::graphics {

// 效果管线 + ImGui GLES 后端（二期）要用的全部 GLES3 函数。每个都是 GLES3 规范函数，
// 任何合规驱动都该有；缺了就整体关闭（load() 返回 false）。
#define PYLON_GL_FUNCS(X) \
    X(GetIntegerv) X(GetBooleanv) X(GetString) X(GetError) X(IsEnabled) X(Enable) X(Disable) \
    X(ColorMask) X(Viewport) X(UseProgram) X(ActiveTexture) X(BindTexture) \
    X(BindSampler) X(BindBuffer) X(BindVertexArray) X(BindFramebuffer) \
    X(GenTextures) X(TexImage2D) X(TexParameteri) X(GenFramebuffers) \
    X(FramebufferTexture2D) X(CheckFramebufferStatus) X(BlitFramebuffer) \
    X(CopyTexSubImage2D) X(GetFramebufferAttachmentParameteriv) \
    X(GenVertexArrays) X(GenBuffers) X(BufferData) X(VertexAttribPointer) \
    X(EnableVertexAttribArray) X(CreateShader) X(ShaderSource) X(CompileShader) \
    X(GetShaderiv) X(GetShaderInfoLog) X(DeleteShader) X(CreateProgram) \
    X(AttachShader) X(LinkProgram) X(GetProgramiv) X(GetProgramInfoLog) \
    X(GetUniformLocation) X(Uniform1i) X(Uniform1f) X(Uniform2f) X(Uniform3f) X(Uniform4f) \
    X(Uniform1fv) X(Uniform2fv) X(Uniform3fv) X(Uniform4fv) \
    X(DrawArrays) X(DrawElements) X(Clear) X(GenSamplers) X(SamplerParameteri) X(PixelStorei) \
    X(DeleteProgram) X(DeleteBuffers) X(DeleteVertexArrays) X(GenerateMipmap) \
    X(BlendFunc) X(BlendEquation) X(BlendFuncSeparate) X(BlendEquationSeparate) X(Scissor) X(GetAttribLocation) X(BindAttribLocation) \
    X(UniformMatrix4fv)

struct GlApi {
#define PYLON_GL_SLOT(name) decltype(&::gl##name) name = nullptr;
    PYLON_GL_FUNCS(PYLON_GL_SLOT)
#undef PYLON_GL_SLOT
    void* (*eglGetCurrentContext)() = nullptr;

    // 一次；之后只返回结果。线程安全由内部静态状态保证。
    bool load();
};

GlApi& gl();

}  // namespace pylon::graphics
