// 层：内核层 / graphics —— GLES 函数表实现（dlsym libGLESv2/libEGL）
#include "graphics/GlApi.h"

#include <dlfcn.h>

#include "pylon/Log.h"

namespace pylon::graphics {

GlApi& gl() {
    static GlApi instance;
    return instance;
}

bool GlApi::load() {
    static int state = 0;  // 0 没试过，1 ok，-1 失败
    if (state != 0) return state > 0;
    state = -1;

    void* gles = dlopen("libGLESv2.so", RTLD_NOW);
    void* egl = dlopen("libEGL.so", RTLD_NOW);
    if (!gles || !egl) {
        LOGE("fx", "libGLESv2/libEGL 没打开；屏幕效果关闭");
        return false;
    }
#define PYLON_GL_BIND(name)                                                          \
    name = reinterpret_cast<decltype(name)>(dlsym(gles, "gl" #name));                \
    if (!name) {                                                                     \
        LOGE("fx", "gl" #name " 缺失；屏幕效果关闭");                                 \
        return false;                                                                \
    }
    PYLON_GL_FUNCS(PYLON_GL_BIND)
#undef PYLON_GL_BIND
    eglGetCurrentContext = reinterpret_cast<void* (*)()>(dlsym(egl, "eglGetCurrentContext"));
    if (!eglGetCurrentContext) return false;

    state = 1;
    return true;
}

}  // namespace pylon::graphics
