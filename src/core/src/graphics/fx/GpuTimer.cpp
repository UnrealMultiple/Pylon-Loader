// 层：内核层 / graphics::fx —— GPU 耗时实现
#include "graphics/fx/GpuTimer.h"

#include <atomic>
#include <cstring>

#include "graphics/Gles.h"
#include "pylon/Log.h"

namespace pylon::graphics::fx {

namespace {

constexpr GLenum kTimeElapsed = 0x88BF;
constexpr GLenum kQueryResult = 0x8866;
constexpr GLenum kQueryAvailable = 0x8867;
constexpr GLenum kGpuDisjoint = 0x8FBB;

using GenQueriesFn = void (*)(GLsizei, GLuint*);
using BeginQueryFn = void (*)(GLenum, GLuint);
using EndQueryFn = void (*)(GLenum);
using GetQueryuivFn = void (*)(GLuint, GLenum, GLuint*);
using GetQueryui64vFn = void (*)(GLuint, GLenum, uint64_t*);

struct Api {
    GenQueriesFn gen = nullptr;
    BeginQueryFn begin = nullptr;
    EndQueryFn end = nullptr;
    GetQueryuivFn getuiv = nullptr;
    GetQueryui64vFn getui64v = nullptr;
};

constexpr int kSlots = 8;

struct Track {
    GLuint queries[kSlots] = {};
    bool pending[kSlots] = {};
    int next = 0;
    int running = -1;
    std::atomic<int64_t> totalNs{0};
    std::atomic<int64_t> samples{0};
};

Api g_api;
Track g_tracks[2];
bool g_open = false;
std::atomic<int> g_state{0};  // 0 未探测 / 1 可用 / 2 不可用（只有渲染线程会探测）

Track& track(Channel channel) {
    return g_tracks[channel == Channel::Effect ? 0 : 1];
}

bool init() {
    if (g_state.load() != 0) return g_state.load() == 1;
    g_state.store(2);
    const Gles& g = gles();
    const char* extensions = reinterpret_cast<const char*>(g.GetString(GL_EXTENSIONS));
    if (extensions == nullptr || std::strstr(extensions, "GL_EXT_disjoint_timer_query") == nullptr) {
        LOGI("fx", "驱动没有 GL_EXT_disjoint_timer_query：不测 GPU 耗时");
        return false;
    }
    if (g.GetProcAddress == nullptr) return false;
    g_api.gen = reinterpret_cast<GenQueriesFn>(g.GetProcAddress("glGenQueriesEXT"));
    g_api.begin = reinterpret_cast<BeginQueryFn>(g.GetProcAddress("glBeginQueryEXT"));
    g_api.end = reinterpret_cast<EndQueryFn>(g.GetProcAddress("glEndQueryEXT"));
    g_api.getuiv = reinterpret_cast<GetQueryuivFn>(g.GetProcAddress("glGetQueryObjectuivEXT"));
    g_api.getui64v = reinterpret_cast<GetQueryui64vFn>(g.GetProcAddress("glGetQueryObjectui64vEXT"));
    if (g_api.gen == nullptr || g_api.begin == nullptr || g_api.end == nullptr || g_api.getuiv == nullptr ||
        g_api.getui64v == nullptr) {
        return false;
    }
    for (Track& t : g_tracks) g_api.gen(kSlots, t.queries);
    g_state.store(1);
    return true;
}

void collect(Track& t) {
    GLint disjoint = 0;
    gles().GetIntegerv(kGpuDisjoint, &disjoint);
    for (int i = 0; i < kSlots; ++i) {
        if (!t.pending[i]) continue;
        GLuint ready = 0;
        g_api.getuiv(t.queries[i], kQueryAvailable, &ready);
        if (!ready) continue;
        uint64_t ns = 0;
        g_api.getui64v(t.queries[i], kQueryResult, &ns);
        t.pending[i] = false;
        if (disjoint) continue;  // 调频/抢占打断的这段不作数
        t.totalNs += static_cast<int64_t>(ns);
        t.samples++;
    }
}

}  // namespace

// 可能被任意线程问（统计走托管线程，那里没有 GL 上下文）：只读结论，不碰 GL。
bool GpuTimer::Available() {
    return g_state.load() == 1;
}

bool GpuTimer::Begin(Channel channel) {
    if (g_open || !init()) return false;
    Track& t = track(channel);
    collect(t);
    if (t.pending[t.next]) return false;  // 这个槽还没回来
    t.running = t.next;
    t.next = (t.next + 1) % kSlots;
    g_api.begin(kTimeElapsed, t.queries[t.running]);
    g_open = true;
    return true;
}

void GpuTimer::End(Channel channel) {
    Track& t = track(channel);
    if (t.running < 0) return;
    g_api.end(kTimeElapsed);
    t.pending[t.running] = true;
    t.running = -1;
    g_open = false;
}

void GpuTimer::Totals(Channel channel, int64_t& totalNs, int64_t& samples) {
    const Track& t = track(channel);
    totalNs = t.totalNs.load();
    samples = t.samples.load();
}

}  // namespace pylon::graphics::fx
