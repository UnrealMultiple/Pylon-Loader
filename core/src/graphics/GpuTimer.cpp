// 层：内核层 / graphics —— GPU 耗时实现
#include "graphics/GpuTimer.h"
#include "graphics/GlApi.h"

#include <atomic>
#include <cstring>
#include <dlfcn.h>

#include "pylon/Log.h"

namespace pylon::graphics {

namespace {

constexpr GLenum kTimeElapsed = 0x88BF;      // GL_TIME_ELAPSED_EXT
constexpr GLenum kQueryResult = 0x8866;      // GL_QUERY_RESULT_EXT
constexpr GLenum kQueryAvailable = 0x8867;   // GL_QUERY_RESULT_AVAILABLE_EXT
constexpr GLenum kGpuDisjoint = 0x8FBB;      // GL_GPU_DISJOINT_EXT

using GenQueriesFn = void (*)(GLsizei, GLuint*);
using BeginQueryFn = void (*)(GLenum, GLuint);
using EndQueryFn = void (*)(GLenum);
using GetQueryuivFn = void (*)(GLuint, GLenum, GLuint*);
using GetQueryui64vFn = void (*)(GLuint, GLenum, uint64_t*);

GenQueriesFn g_gen = nullptr;
BeginQueryFn g_begin = nullptr;
EndQueryFn g_end = nullptr;
GetQueryuivFn g_getuiv = nullptr;
GetQueryui64vFn g_getui64v = nullptr;

constexpr int kSlots = 8;

struct Channel {
    GLuint queries[kSlots] = {};
    bool pending[kSlots] = {};
    int next = 0;
    int running = -1;
    std::atomic<int64_t> totalNs{0};
    std::atomic<int64_t> samples{0};
};

Channel g_channels[2];
bool g_open = false;  // 有条时间查询开着（任何通道）
int g_state = 0;      // 0 没试过，1 ok，-1 没扩展

Channel& channel(GpuChannel c) {
    return g_channels[c == GpuChannel::Effect ? 0 : 1];
}

bool init() {
    if (g_state != 0) return g_state > 0;
    g_state = -1;
    auto& g = gl();
    const char* ext = reinterpret_cast<const char*>(g.GetString(GL_EXTENSIONS));
    if (!ext || !std::strstr(ext, "GL_EXT_disjoint_timer_query")) {
        LOGI("fx", "屏幕效果：GPU 没有 GL_EXT_disjoint_timer_query；不测 GPU 耗时");
        return false;
    }
    void* egl = dlopen("libEGL.so", RTLD_NOW);
    auto proc = egl ? reinterpret_cast<void* (*)(const char*)>(dlsym(egl, "eglGetProcAddress")) : nullptr;
    if (!proc) return false;
    g_gen = reinterpret_cast<GenQueriesFn>(proc("glGenQueriesEXT"));
    g_begin = reinterpret_cast<BeginQueryFn>(proc("glBeginQueryEXT"));
    g_end = reinterpret_cast<EndQueryFn>(proc("glEndQueryEXT"));
    g_getuiv = reinterpret_cast<GetQueryuivFn>(proc("glGetQueryObjectuivEXT"));
    g_getui64v = reinterpret_cast<GetQueryui64vFn>(proc("glGetQueryObjectui64vEXT"));
    if (!g_gen || !g_begin || !g_end || !g_getuiv || !g_getui64v) return false;
    for (Channel& c : g_channels) g_gen(kSlots, c.queries);
    g_state = 1;
    return true;
}

void collect(Channel& c) {
    GLint disjoint = 0;
    gl().GetIntegerv(kGpuDisjoint, &disjoint);
    for (int i = 0; i < kSlots; ++i) {
        if (!c.pending[i]) continue;
        GLuint ready = 0;
        g_getuiv(c.queries[i], kQueryAvailable, &ready);
        if (!ready) continue;
        uint64_t ns = 0;
        g_getui64v(c.queries[i], kQueryResult, &ns);
        c.pending[i] = false;
        // Disjoint（调频、抢占）：这次测量不作数。
        if (disjoint) continue;
        c.totalNs += static_cast<int64_t>(ns);
        c.samples++;
    }
}

}  // namespace

bool GpuTimer::Begin(GpuChannel which) {
    if (g_open || !init()) return false;
    Channel& c = channel(which);
    collect(c);
    if (c.pending[c.next]) return false;  // 槽还没回来：这帧不测
    c.running = c.next;
    c.next = (c.next + 1) % kSlots;
    g_begin(kTimeElapsed, c.queries[c.running]);
    g_open = true;
    return true;
}

void GpuTimer::End(GpuChannel which) {
    Channel& c = channel(which);
    if (c.running < 0) return;
    g_end(kTimeElapsed);
    c.pending[c.running] = true;
    c.running = -1;
    g_open = false;
}

bool GpuTimer::Available() {
    return g_state > 0;
}

void GpuTimer::Totals(GpuChannel which, int64_t& totalNs, int64_t& samples) {
    Channel& c = channel(which);
    totalNs = c.totalNs.load();
    samples = c.samples.load();
}

}  // namespace pylon::graphics
