// 层：内核层 / graphics —— 状态还原
#include "graphics/GlState.h"

namespace pylon::graphics {

namespace {

void setCap(GLenum cap, bool on) {
    if (on) gles().Enable(cap);
    else gles().Disable(cap);
}

}  // namespace

GlState GlState::Query() {
    const Gles& g = gles();
    GlState s;
    GLint value = 0;
    g.GetIntegerv(GL_CURRENT_PROGRAM, &value);
    s.program = static_cast<GLuint>(value);
    g.GetIntegerv(GL_VERTEX_ARRAY_BINDING, &value);
    s.vertexArray = static_cast<GLuint>(value);
    g.GetIntegerv(GL_DRAW_FRAMEBUFFER_BINDING, &value);
    s.drawFbo = static_cast<GLuint>(value);
    g.GetIntegerv(GL_READ_FRAMEBUFFER_BINDING, &value);
    s.readFbo = static_cast<GLuint>(value);
    g.GetIntegerv(GL_ACTIVE_TEXTURE, &value);
    s.activeTexture = static_cast<GLenum>(value);
    g.GetIntegerv(GL_VIEWPORT, s.viewport);
    g.GetBooleanv(GL_COLOR_WRITEMASK, s.colorMask);
    g.GetIntegerv(GL_BLEND_SRC_RGB, &value);
    s.blendSrcRgb = static_cast<GLenum>(value);
    g.GetIntegerv(GL_BLEND_DST_RGB, &value);
    s.blendDstRgb = static_cast<GLenum>(value);
    g.GetIntegerv(GL_BLEND_SRC_ALPHA, &value);
    s.blendSrcAlpha = static_cast<GLenum>(value);
    g.GetIntegerv(GL_BLEND_DST_ALPHA, &value);
    s.blendDstAlpha = static_cast<GLenum>(value);
    g.GetIntegerv(GL_BLEND_EQUATION_RGB, &value);
    s.blendEqRgb = static_cast<GLenum>(value);
    g.GetIntegerv(GL_BLEND_EQUATION_ALPHA, &value);
    s.blendEqAlpha = static_cast<GLenum>(value);
    s.blend = g.IsEnabled(GL_BLEND);
    s.depth = g.IsEnabled(GL_DEPTH_TEST);
    s.stencil = g.IsEnabled(GL_STENCIL_TEST);
    s.cull = g.IsEnabled(GL_CULL_FACE);
    s.scissor = g.IsEnabled(GL_SCISSOR_TEST);
    return s;
}

void GlState::Restore() const {
    const Gles& g = gles();
    if (drawFbo == readFbo) {
        g.BindFramebuffer(GL_FRAMEBUFFER, drawFbo);
    } else {
        g.BindFramebuffer(GL_DRAW_FRAMEBUFFER, drawFbo);
        g.BindFramebuffer(GL_READ_FRAMEBUFFER, readFbo);
    }
    g.Viewport(viewport[0], viewport[1], viewport[2], viewport[3]);
    g.UseProgram(program);
    g.BindVertexArray(vertexArray);
    g.ActiveTexture(activeTexture);
    g.ColorMask(colorMask[0], colorMask[1], colorMask[2], colorMask[3]);
    setCap(GL_BLEND, blend);
    setCap(GL_DEPTH_TEST, depth);
    setCap(GL_STENCIL_TEST, stencil);
    setCap(GL_CULL_FACE, cull);
    setCap(GL_SCISSOR_TEST, scissor);
    g.BlendFuncSeparate(blendSrcRgb, blendDstRgb, blendSrcAlpha, blendDstAlpha);
    g.BlendEquationSeparate(blendEqRgb, blendEqAlpha);
}

ScopedState::ScopedState() {
    const Gles& g = gles();
    g.GetIntegerv(GL_CURRENT_PROGRAM, &program_);
    g.GetIntegerv(GL_ACTIVE_TEXTURE, &activeTexture_);
    g.ActiveTexture(GL_TEXTURE0);
    g.GetIntegerv(GL_TEXTURE_BINDING_2D, &texture_);
    g.GetIntegerv(GL_SAMPLER_BINDING, &sampler_);
    g.GetIntegerv(GL_ARRAY_BUFFER_BINDING, &arrayBuffer_);
    g.GetIntegerv(GL_PIXEL_UNPACK_BUFFER_BINDING, &unpackBuffer_);
    g.GetIntegerv(GL_VERTEX_ARRAY_BINDING, &vertexArray_);
    g.GetIntegerv(GL_DRAW_FRAMEBUFFER_BINDING, &drawFbo_);
    g.GetIntegerv(GL_READ_FRAMEBUFFER_BINDING, &readFbo_);
    g.GetIntegerv(GL_VIEWPORT, viewport_);
    g.GetBooleanv(GL_COLOR_WRITEMASK, colorMask_);
    blend_ = g.IsEnabled(GL_BLEND);
    depth_ = g.IsEnabled(GL_DEPTH_TEST);
    stencil_ = g.IsEnabled(GL_STENCIL_TEST);
    cull_ = g.IsEnabled(GL_CULL_FACE);
    scissor_ = g.IsEnabled(GL_SCISSOR_TEST);
}

ScopedState::~ScopedState() {
    const Gles& g = gles();
    g.BindFramebuffer(GL_DRAW_FRAMEBUFFER, drawFbo_);
    g.BindFramebuffer(GL_READ_FRAMEBUFFER, readFbo_);
    g.Viewport(viewport_[0], viewport_[1], viewport_[2], viewport_[3]);
    g.UseProgram(program_);
    g.BindVertexArray(vertexArray_);
    g.BindBuffer(GL_ARRAY_BUFFER, arrayBuffer_);
    g.BindBuffer(GL_PIXEL_UNPACK_BUFFER, unpackBuffer_);
    g.ActiveTexture(GL_TEXTURE0);
    g.BindTexture(GL_TEXTURE_2D, texture_);
    g.BindSampler(0, sampler_);
    g.ActiveTexture(activeTexture_);
    g.ColorMask(colorMask_[0], colorMask_[1], colorMask_[2], colorMask_[3]);
    setCap(GL_BLEND, blend_ != GL_FALSE);
    setCap(GL_DEPTH_TEST, depth_ != GL_FALSE);
    setCap(GL_STENCIL_TEST, stencil_ != GL_FALSE);
    setCap(GL_CULL_FACE, cull_ != GL_FALSE);
    setCap(GL_SCISSOR_TEST, scissor_ != GL_FALSE);
}

}  // namespace pylon::graphics
