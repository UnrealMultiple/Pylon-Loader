#include "imgui/overlay/Present.h"

#include <atomic>
#include <chrono>
#include <cstring>
#include <memory>
#include <mutex>
#include <thread>
#include <utility>
#include <vector>

#include "imgui.h"
#include "graphics/GlState.h"
#include "graphics/Gles.h"
#include "imgui/overlay/Session.h"
#include "imgui/overlay/Snapshot.h"
#include "imgui/overlay/Textures.h"
#include "imgui_impl_opengl3.h"
#include "pylon/Log.h"

namespace pylon::imgui {

namespace {

constexpr const char* kStage = "imgui";

constexpr int64_t kFrameLifetimeNs = 250'000'000;
constexpr int kTeardownWaitMs = 100;
constexpr int64_t kReportEverySwaps = 300;

struct Stats {
    int64_t drawn = 0;
    int64_t stale = 0;
    int64_t missing = 0;
    int64_t blank = 0;
    int vertices = 0;
    int commands = 0;
};

std::mutex g_mutex;
std::shared_ptr<const Snapshot> g_latest;

std::atomic<int> g_width{0};
std::atomic<int> g_height{0};
std::atomic<bool> g_inited{false};
std::atomic<bool> g_ready{false};
std::atomic<bool> g_teardownRequested{false};
std::atomic<bool> g_tornDown{false};

Stats g_stats;
int64_t g_swaps = 0;
bool g_firstFrame = false;

/// 纹理镜像：渲染线程自己的 GL 名字，按 (ImTextureData*, UniqueID) 键控。
/// 只在渲染线程（AtSwap → Draw）访问，不需要锁；活对象的读写全部发生在游戏线程捕获时，
/// 渲染线程只消费快照副本，不会碰到位未上传/已释放的纹理。
struct MirrorTex {
    ImTextureData* pointer;
    unsigned int uniqueId;
    GLuint name;
};

std::vector<MirrorTex> g_mirrors;

MirrorTex* FindMirror(ImTextureData* pointer) {
    for (auto& m : g_mirrors)
        if (m.pointer == pointer) return &m;
    return nullptr;
}

void SyncMirrors(const Snapshot& snapshot) {
    const auto& g = graphics::gles();
    for (const Snapshot::TextureSnap& snap : snapshot.Textures()) {
        MirrorTex* m = FindMirror(snap.pointer);
        if (snap.status == ImTextureStatus_WantDestroy) {
            if (m != nullptr) {
                g.DeleteTextures(1, &m->name);
                g_mirrors.erase(g_mirrors.begin() + (m - g_mirrors.data()));
            }
            continue;
        }
        if (m != nullptr && m->uniqueId != snap.uniqueId) {
            g.DeleteTextures(1, &m->name);
            m = nullptr;
        }
        if (m == nullptr) {
            GLuint name = 0;
            g.GenTextures(1, &name);
            g_mirrors.push_back(MirrorTex{snap.pointer, snap.uniqueId, name});
            m = &g_mirrors.back();
        }
        if (!snap.pixels.empty()) {
            graphics::ScopedState saved;
            g.BindTexture(GL_TEXTURE_2D, m->name);
            g.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
            g.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
            g.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
            g.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);
            g.TexImage2D(GL_TEXTURE_2D, 0, GL_RGBA, snap.width, snap.height, 0, GL_RGBA, GL_UNSIGNED_BYTE,
                         snap.pixels.data());
        }
    }
}

void Draw(const Snapshot& snapshot) {
    std::vector<std::unique_ptr<ImDrawList>> lists;
    lists.reserve(snapshot.Lists().size());

    SyncMirrors(snapshot);

    ImDrawData data;
    data.Valid = true;
    data.Textures = nullptr;
    data.DisplayPos = snapshot.DisplayPos();
    data.DisplaySize = snapshot.DisplaySize();
    data.FramebufferScale = snapshot.FramebufferScale();
    data.CmdListsCount = static_cast<int>(snapshot.Lists().size());
    data.CmdLists.resize(data.CmdListsCount);

    for (int i = 0; i < data.CmdListsCount; ++i) {
        const Snapshot::List& from = snapshot.Lists()[static_cast<size_t>(i)];
        auto list = std::make_unique<ImDrawList>(nullptr);
        list->VtxBuffer.resize(static_cast<int>(from.vertices.size()));
        if (!from.vertices.empty()) {
            std::memcpy(list->VtxBuffer.Data, from.vertices.data(), from.vertices.size() * sizeof(ImDrawVert));
        }
        list->IdxBuffer.resize(static_cast<int>(from.indices.size()));
        if (!from.indices.empty()) {
            std::memcpy(list->IdxBuffer.Data, from.indices.data(), from.indices.size() * sizeof(ImDrawIdx));
        }
        list->CmdBuffer.resize(static_cast<int>(from.commands.size()));
        if (!from.commands.empty()) {
            std::memcpy(list->CmdBuffer.Data, from.commands.data(), from.commands.size() * sizeof(ImDrawCmd));
            for (ImDrawCmd& cmd : list->CmdBuffer) {
                if (cmd.TexRef._TexData == nullptr) continue;
                const MirrorTex* m = FindMirror(cmd.TexRef._TexData);
                cmd.TexRef._TexData = nullptr;
                cmd.TexRef._TexID = m != nullptr ? (ImTextureID)(intptr_t)m->name : ImTextureID_Invalid;
            }
        }
        data.TotalVtxCount += list->VtxBuffer.Size;
        data.TotalIdxCount += list->IdxBuffer.Size;
        data.CmdLists[i] = list.get();
        lists.push_back(std::move(list));
    }

    ImGui_ImplOpenGL3_RenderDrawData(&data);
}

bool EnsureBackend() {
    if (g_inited.load()) return true;
    if (ImGui::GetCurrentContext() == nullptr) return false;
    if (!ImGui_ImplOpenGL3_Init("#version 300 es")) {
        LOGE(kStage, "ImGui_ImplOpenGL3_Init 失败");
        return false;
    }
    if (!ImGui_ImplOpenGL3_CreateDeviceObjects()) {
        LOGE(kStage, "覆盖层着色器/缓冲创建失败，覆盖层关闭");
        ImGui_ImplOpenGL3_Shutdown();
        return false;
    }
    g_inited.store(true);
    g_ready.store(true);
    LOGI(kStage, "GLES 覆盖层后端已就绪（画在游戏自己的上下文里）");
    return true;
}

bool ApplyTeardown() {
    if (!g_teardownRequested.exchange(false)) return false;
    if (g_inited.load()) {
        ImGui_ImplOpenGL3_Shutdown();
        g_inited.store(false);
        g_ready.store(false);
    }
    g_mirrors.clear();
    g_tornDown.store(true);
    return true;
}

std::shared_ptr<const Snapshot> Take(bool* stale) {
    std::lock_guard<std::mutex> lock(g_mutex);
    if (!g_latest) return nullptr;
    if (NowNs() - g_latest->TakenAt() > kFrameLifetimeNs) {
        g_latest.reset();
        *stale = true;
        return nullptr;
    }
    return g_latest;
}

void Report() {
    if (++g_swaps % kReportEverySwaps != 0) return;
    LOGI(kStage, "上屏统计（近 %lld 次 swap）：画 %lld，过期 %lld，无帧 %lld，空帧 %lld；最近一帧 %d 顶点 / %d 条命令",
         static_cast<long long>(kReportEverySwaps), static_cast<long long>(g_stats.drawn),
         static_cast<long long>(g_stats.stale), static_cast<long long>(g_stats.missing),
         static_cast<long long>(g_stats.blank), g_stats.vertices, g_stats.commands);
    g_stats.drawn = g_stats.stale = g_stats.missing = g_stats.blank = 0;
}

}

void Present::AtSwap(int width, int height) {
    if (width <= 0 || height <= 0) return;
    g_width.store(width);
    g_height.store(height);

    ImGuiContext* context = Session::Context();
    if (context == nullptr) return;
    ImGui::SetCurrentContext(context);

    if (ApplyTeardown()) return;
    if (!EnsureBackend()) return;

    Textures::Apply();

    bool stale = false;
    const std::shared_ptr<const Snapshot> frame = Take(&stale);
    const bool drawable = frame != nullptr && frame->Vertices() > 0;
    if (stale) g_stats.stale++;
    else if (frame == nullptr) g_stats.missing++;
    else if (!drawable) g_stats.blank++;

    if (drawable) {
        g_stats.drawn++;
        g_stats.vertices = frame->Vertices();
        g_stats.commands = frame->Commands();
        if (!g_firstFrame) {
            g_firstFrame = true;
            LOGI(kStage, "首帧上屏：界面 %.0fx%.0f，表面 %dx%d", frame->DisplaySize().x, frame->DisplaySize().y,
                 width, height);
        }
        Draw(*frame);
    }
    Report();
}

bool Present::Ready() {
    return g_ready.load() && g_width.load() > 0 && g_height.load() > 0;
}

void Present::Size(int32_t* width, int32_t* height) {
    if (width != nullptr) *width = g_width.load();
    if (height != nullptr) *height = g_height.load();
}

void Present::Submit(std::shared_ptr<const Snapshot> frame) {
    std::lock_guard<std::mutex> lock(g_mutex);
    g_latest = std::move(frame);
}

void Present::Teardown() {
    if (!g_inited.load()) return;
    g_tornDown.store(false);
    g_teardownRequested.store(true);

    const int64_t deadline = NowNs() + static_cast<int64_t>(kTeardownWaitMs) * 1'000'000;
    while (!g_tornDown.load() && NowNs() < deadline) {
        std::this_thread::sleep_for(std::chrono::milliseconds(2));
    }
    if (g_tornDown.load()) return;

    g_teardownRequested.store(false);
    g_inited.store(false);
    g_ready.store(false);
    LOGW(kStage, "渲染线程已停：后端对象留给进程回收");
}

}
