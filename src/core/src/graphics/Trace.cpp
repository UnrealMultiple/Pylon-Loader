// 层：内核层 / graphics —— 调用序列记录（定长，不分配）
#include "graphics/Trace.h"

#include <atomic>
#include <cstdio>
#include <cstring>

#include "pylon/Log.h"

namespace pylon::graphics::trace {

namespace {

struct Rec {
    char kind[4];
    int args[4];
};

constexpr int kCapacity = 600;
constexpr size_t kLineBudget = 700;

Rec g_recs[kCapacity];
int g_count = 0;
int g_draws = 0;
bool g_on = false;
std::atomic<bool> g_requested{false};

void flush() {
    if (!g_on) return;
    g_on = false;

    char line[kLineBudget] = {0};
    size_t used = 0;
    int chunk = 1;
    for (int i = 0; i <= g_count; ++i) {
        char item[80];
        int written;
        if (i < g_count) {
            const Rec& r = g_recs[i];
            written = std::snprintf(item, sizeof item, " %s(%d,%d,%d,%d)", r.kind, r.args[0], r.args[1],
                                    r.args[2], r.args[3]);
        } else {
            written = std::snprintf(item, sizeof item, " D%d", g_draws);
        }
        if (written < 0) continue;
        if (used + static_cast<size_t>(written) + 1 > kLineBudget) {
            LOGI("fx", "GL[%d]:%s", chunk++, line);
            used = 0;
            line[0] = 0;
        }
        std::memcpy(line + used, item, static_cast<size_t>(written) + 1);
        used += static_cast<size_t>(written);
    }
    if (used > 0) LOGI("fx", "GL[%d]:%s", chunk, line);
}

}  // namespace

void RequestFrame() {
    g_requested.store(true);
}

void Begin() {
    flush();
    g_on = g_requested.exchange(false);
    g_count = 0;
    g_draws = 0;
}

void Record(const char* kind, int a, int b, int c, int d) {
    if (!g_on || g_count >= kCapacity) return;
    Rec& r = g_recs[g_count++];
    std::snprintf(r.kind, sizeof r.kind, "%s", kind);
    r.args[0] = a;
    r.args[1] = b;
    r.args[2] = c;
    r.args[3] = d;
}

void CountDraw() {
    if (g_on) g_draws++;
}

void Flush() {
    flush();
}

}  // namespace pylon::graphics::trace
