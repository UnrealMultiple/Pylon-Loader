// 层：内核层 / graphics —— 捕获实现
#include "graphics/Capture.h"

#include <EGL/egl.h>
#include <sys/prctl.h>

#include <cstring>

#include "graphics/GlState.h"
#include "graphics/Gles.h"
#include "graphics/Guard.h"
#include "graphics/Hook.h"
#include "graphics/Marker.h"
#include "graphics/Pipe.h"
#include "graphics/Trace.h"
#include "graphics/fx/GpuTimer.h"
#include "pylon/Log.h"

namespace pylon::graphics {

thread_local bool g_ownDraw = false;

namespace {

using SwapFn = EGLBoolean (*)(EGLDisplay, EGLSurface);
using BindFramebufferFn = void (*)(GLenum, GLuint);
using ViewportFn = void (*)(GLint, GLint, GLsizei, GLsizei);
using ClearFn = void (*)(GLbitfield);
using DrawElementsFn = void (*)(GLenum, GLsizei, GLenum, const void*);
using DrawArraysFn = void (*)(GLenum, GLint, GLsizei);
using BlitFn = void (*)(GLint, GLint, GLint, GLint, GLint, GLint, GLint, GLint, GLbitfield, GLenum);
using UintFn = void (*)(GLuint);
using EnumFn = void (*)(GLenum);
using ColorMaskFn = void (*)(GLboolean, GLboolean, GLboolean, GLboolean);
using BlendFuncFn = void (*)(GLenum, GLenum);
using BlendFuncSeparateFn = void (*)(GLenum, GLenum, GLenum, GLenum);
using BlendEquationFn = void (*)(GLenum);
using BlendEquationSeparateFn = void (*)(GLenum, GLenum);

SwapFn g_swap = nullptr;
BindFramebufferFn g_bindFramebuffer = nullptr;
ViewportFn g_viewport = nullptr;
ClearFn g_clear = nullptr;
DrawElementsFn g_drawElements = nullptr;
DrawArraysFn g_drawArrays = nullptr;
BlitFn g_blit = nullptr;
UintFn g_useProgram = nullptr;
UintFn g_bindVertexArray = nullptr;
EnumFn g_activeTexture = nullptr;
EnumFn g_enable = nullptr;
EnumFn g_disable = nullptr;
ColorMaskFn g_colorMask = nullptr;
BlendFuncFn g_blendFunc = nullptr;
BlendFuncSeparateFn g_blendFuncSeparate = nullptr;
BlendEquationFn g_blendEquation = nullptr;
BlendEquationSeparateFn g_blendEquationSeparate = nullptr;

GlState g_state;
bool g_synced = false;
fx::RenderTarget g_lastDraw;
int g_frame = 0;

thread_local int t_unity = -1;

bool isUnityThread() {
    if (t_unity < 0) {
        char name[17] = {0};  // PR_GET_NAME 最多 16 字节
        prctl(PR_GET_NAME, name);
        t_unity = std::strncmp(name, "UnityGfxDevice", 14) == 0 || std::strcmp(name, "UnityMain") == 0;
    }
    return t_unity == 1;
}

// 镜像按需同步：第一次被记的调用之前先问一次驱动，之后由 hook 维护。
bool tracked() {
    if (g_ownDraw || !isUnityThread()) return false;
    if (!g_synced) {
        g_state = GlState::Query();
        g_synced = true;
    }
    return true;
}

void noteDraw() {
    g_lastDraw.fbo = static_cast<int>(g_state.drawFbo);
    for (int i = 0; i < 4; ++i) g_lastDraw.viewport[i] = g_state.viewport[i];
}

const GlState& unityState() {
    return g_state;
}

bool* capSlot(GLenum cap) {
    switch (cap) {
        case GL_BLEND: return &g_state.blend;
        case GL_DEPTH_TEST: return &g_state.depth;
        case GL_STENCIL_TEST: return &g_state.stencil;
        case GL_CULL_FACE: return &g_state.cull;
        case GL_SCISSOR_TEST: return &g_state.scissor;
        default: return nullptr;
    }
}

EGLBoolean hookSwap(EGLDisplay display, EGLSurface surface) {
    if (!isUnityThread()) return g_swap(display, surface);

    trace::Begin();
    trace::Record("SWAP");

    int width = 0, height = 0;
    const Gles& g = gles();
    g.QuerySurface(display, surface, kEglWidth, &width);
    g.QuerySurface(display, surface, kEglHeight, &height);

    fx::GpuTimer::End(fx::Channel::Frame);
    Pipe::RunScreen(unityState(), width, height);
    const EGLBoolean result = g_swap(display, surface);
    if (++g_frame % 2 == 0) fx::GpuTimer::Begin(fx::Channel::Frame);

    trace::Flush();
    return result;
}

void hookBindFramebuffer(GLenum target, GLuint fbo) {
    if (tracked()) {
        if (target != GL_READ_FRAMEBUFFER) g_state.drawFbo = fbo;
        if (target != GL_DRAW_FRAMEBUFFER) g_state.readFbo = fbo;
        trace::Record("FB", static_cast<int>(target), static_cast<int>(fbo));
    }
    g_bindFramebuffer(target, fbo);
}

void hookViewport(GLint x, GLint y, GLsizei width, GLsizei height) {
    if (tracked()) {
        trace::Record("VP", x, y, width, height);
        if (width == kMarkerWidth && height == kMarkerHeight) {
            trace::Record("MARK");
            Pipe::RunWorld(g_state, g_lastDraw);
        }
        g_state.viewport[0] = x;
        g_state.viewport[1] = y;
        g_state.viewport[2] = width;
        g_state.viewport[3] = height;
    }
    g_viewport(x, y, width, height);
}

void hookUseProgram(GLuint program) {
    if (tracked()) g_state.program = program;
    g_useProgram(program);
}

void hookBindVertexArray(GLuint vao) {
    if (tracked()) g_state.vertexArray = vao;
    g_bindVertexArray(vao);
}

void hookActiveTexture(GLenum unit) {
    if (tracked()) g_state.activeTexture = unit;
    g_activeTexture(unit);
}

void hookEnable(GLenum cap) {
    if (tracked()) {
        if (bool* slot = capSlot(cap)) *slot = true;
    }
    g_enable(cap);
}

void hookDisable(GLenum cap) {
    if (tracked()) {
        if (bool* slot = capSlot(cap)) *slot = false;
    }
    g_disable(cap);
}

void hookColorMask(GLboolean r, GLboolean gr, GLboolean b, GLboolean a) {
    if (tracked()) {
        g_state.colorMask[0] = r;
        g_state.colorMask[1] = gr;
        g_state.colorMask[2] = b;
        g_state.colorMask[3] = a;
    }
    g_colorMask(r, gr, b, a);
}

void hookBlendFunc(GLenum source, GLenum destination) {
    if (tracked()) {
        g_state.blendSrcRgb = g_state.blendSrcAlpha = source;
        g_state.blendDstRgb = g_state.blendDstAlpha = destination;
    }
    g_blendFunc(source, destination);
}

void hookBlendFuncSeparate(GLenum sourceRgb, GLenum destinationRgb, GLenum sourceAlpha, GLenum destinationAlpha) {
    if (tracked()) {
        g_state.blendSrcRgb = sourceRgb;
        g_state.blendDstRgb = destinationRgb;
        g_state.blendSrcAlpha = sourceAlpha;
        g_state.blendDstAlpha = destinationAlpha;
    }
    g_blendFuncSeparate(sourceRgb, destinationRgb, sourceAlpha, destinationAlpha);
}

void hookBlendEquation(GLenum mode) {
    if (tracked()) g_state.blendEqRgb = g_state.blendEqAlpha = mode;
    g_blendEquation(mode);
}

void hookBlendEquationSeparate(GLenum rgb, GLenum alpha) {
    if (tracked()) {
        g_state.blendEqRgb = rgb;
        g_state.blendEqAlpha = alpha;
    }
    g_blendEquationSeparate(rgb, alpha);
}

void hookClear(GLbitfield mask) {
    if (tracked()) trace::Record("CL", static_cast<int>(mask));
    g_clear(mask);
}

void hookDrawElements(GLenum mode, GLsizei count, GLenum type, const void* indices) {
    if (tracked()) {
        noteDraw();
        trace::CountDraw();
    }
    g_drawElements(mode, count, type, indices);
}

void hookDrawArrays(GLenum mode, GLint first, GLsizei count) {
    if (tracked()) {
        noteDraw();
        trace::CountDraw();
    }
    g_drawArrays(mode, first, count);
}

void hookBlit(GLint sx0, GLint sy0, GLint sx1, GLint sy1, GLint dx0, GLint dy0, GLint dx1, GLint dy1,
              GLbitfield mask, GLenum filter) {
    if (tracked()) trace::Record("BL", sx0, sy0, dx1, dy1);
    g_blit(sx0, sy0, sx1, sy1, dx0, dy0, dx1, dy1, mask, filter);
}

}  // namespace

bool InstallGlHooks() {
    const Gles* g = Gles::Load();
    if (g == nullptr) return false;

    bool ok = true;
    ok &= Hook(g->SwapBuffers, &hookSwap, &g_swap);
    ok &= Hook(g->BindFramebuffer, &hookBindFramebuffer, &g_bindFramebuffer);
    ok &= Hook(g->Viewport, &hookViewport, &g_viewport);
    ok &= Hook(g->UseProgram, &hookUseProgram, &g_useProgram);
    ok &= Hook(g->BindVertexArray, &hookBindVertexArray, &g_bindVertexArray);
    ok &= Hook(g->ActiveTexture, &hookActiveTexture, &g_activeTexture);
    ok &= Hook(g->Enable, &hookEnable, &g_enable);
    ok &= Hook(g->Disable, &hookDisable, &g_disable);
    ok &= Hook(g->ColorMask, &hookColorMask, &g_colorMask);
    ok &= Hook(g->BlendFunc, &hookBlendFunc, &g_blendFunc);
    ok &= Hook(g->BlendFuncSeparate, &hookBlendFuncSeparate, &g_blendFuncSeparate);
    ok &= Hook(g->BlendEquation, &hookBlendEquation, &g_blendEquation);
    ok &= Hook(g->BlendEquationSeparate, &hookBlendEquationSeparate, &g_blendEquationSeparate);
    ok &= Hook(g->Clear, &hookClear, &g_clear);
    ok &= Hook(g->DrawElements, &hookDrawElements, &g_drawElements);
    ok &= Hook(g->DrawArrays, &hookDrawArrays, &g_drawArrays);
    ok &= Hook(g->BlitFramebuffer, &hookBlit, &g_blit);
    LOGI("fx", "GL hooks %s", ok ? "已装" : "有失败");
    return ok;
}

}  // namespace pylon::graphics
