// 层：内核层 / graphics —— 入口表实现
#include "graphics/Gles.h"

#include <dlfcn.h>

#include "pylon/Log.h"

namespace pylon::graphics {

namespace {

Gles g_instance;
int g_state = 0;  // 0 未尝试 / 1 成功 / -1 失败

template <class Fn>
bool bind(Fn& slot, void* library, const char* name) {
    slot = reinterpret_cast<Fn>(dlsym(library, name));
    if (slot != nullptr) return true;
    LOGE("fx", "缺少 %s；屏幕效果与覆盖层一起关闭", name);
    return false;
}

}  // namespace

const Gles* Gles::Load() {
    if (g_state != 0) return g_state > 0 ? &g_instance : nullptr;
    g_state = -1;

    void* gles = dlopen("libGLESv2.so", RTLD_NOW);
    void* egl = dlopen("libEGL.so", RTLD_NOW);
    if (gles == nullptr || egl == nullptr) {
        LOGE("fx", "libGLESv2/libEGL 打不开；屏幕效果与覆盖层一起关闭");
        return nullptr;
    }

#define PYLON_GL_BIND(name)                                     \
    if (!bind(g_instance.name, gles, "gl" #name)) return nullptr;
    PYLON_GL_FUNCS(PYLON_GL_BIND)
#undef PYLON_GL_BIND

    if (!bind(g_instance.GetCurrentContext, egl, "eglGetCurrentContext") ||
        !bind(g_instance.SwapBuffers, egl, "eglSwapBuffers") ||
        !bind(g_instance.QuerySurface, egl, "eglQuerySurface")) {
        return nullptr;
    }
    g_instance.GetProcAddress = reinterpret_cast<void* (*)(const char*)>(dlsym(egl, "eglGetProcAddress"));

    g_state = 1;
    return &g_instance;
}

const Gles& gles() {
    return g_instance;
}

}  // namespace pylon::graphics
