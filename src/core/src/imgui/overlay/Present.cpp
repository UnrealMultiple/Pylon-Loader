// 层：内核层 / imgui —— 上屏实现
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
#include "imgui/overlay/Session.h"
#include "imgui/overlay/Snapshot.h"
#include "imgui/overlay/Textures.h"
#include "imgui_impl_opengl3.h"
#include "pylon/Log.h"

namespace pylon::imgui {

namespace {

constexpr const char* kStage = "imgui";

/// 界面关掉后游戏线程不再发布：最后一帧在屏幕上最多待这么久。
constexpr int64_t kFrameLifetimeNs = 250'000'000;
/// 拆后端时等渲染线程动手的上限；它已经停了就把 GL 对象留给进程。
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

std::mutex g_mutex;  // 只护着"最新一帧"这一次指针交换
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

/// 把快照装成官方后端能直接画的数据：ImVector 用 ImGui 的分配器，只能在上下文是当前的
/// 线程上建，所以整块活在 AtSwap 里，画完即弃。
void Draw(const Snapshot& snapshot) {
    std::vector<std::unique_ptr<ImDrawList>> lists;
    lists.reserve(snapshot.Lists().size());

    // 字体图集由后端自己管：它会在 WantCreate 时建 GL 纹理并回填 TexID。
    ImVector<ImTextureData*> textures;
    if (!snapshot.Textures().empty()) {
        textures.resize(static_cast<int>(snapshot.Textures().size()));
        for (int i = 0; i < textures.Size; ++i) textures[i] = snapshot.Textures()[static_cast<size_t>(i)];
    }

    ImDrawData data;
    data.Valid = true;
    data.Textures = textures.empty() ? nullptr : &textures;
    data.DisplayPos = snapshot.DisplayPos();
    data.DisplaySize = snapshot.DisplaySize();
    data.FramebufferScale = snapshot.FramebufferScale();
    data.CmdListsCount = static_cast<int>(snapshot.Lists().size());
    data.CmdLists.resize(data.CmdListsCount);

    for (int i = 0; i < data.CmdListsCount; ++i) {
        const Snapshot::List& from = snapshot.Lists()[static_cast<size_t>(i)];
        auto list = std::make_unique<ImDrawList>(nullptr);  // 不属于任何上下文：只当缓冲容器
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
    // 自己建 device objects 并检查返回值：NewFrame 里失败是直接 assert 的，会把游戏一起带走。
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

/// 渲染线程：把游戏线程要求的拆除做掉（只有这里有 GL 上下文）。
bool ApplyTeardown() {
    if (!g_teardownRequested.exchange(false)) return false;
    if (g_inited.load()) {
        ImGui_ImplOpenGL3_Shutdown();
        g_inited.store(false);
        g_ready.store(false);
    }
    g_tornDown.store(true);
    return true;
}

/// 取最新一帧；同时是唯一判断"过期"的地方。
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
    g_stats.drawn = g_stats.stale = g_stats.missing = g_stats.blank = 0;  // 每条统计行都是这一段的增量
}

}  // namespace

void Present::AtSwap(int width, int height) {
    if (width <= 0 || height <= 0) return;
    g_width.store(width);
    g_height.store(height);

    ImGuiContext* context = Session::Context();
    if (context == nullptr) return;
    // 后端从当前上下文的 IO 里取自己的数据；游戏线程写的都是另外的字段。
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

    // 渲染线程没在跳：GL 对象随进程回收，后端直接标成不可用，别让它拿着已经销毁的上下文。
    g_teardownRequested.store(false);
    g_inited.store(false);
    g_ready.store(false);
    LOGW(kStage, "渲染线程已停：后端对象留给进程回收");
}

}  // namespace pylon::imgui
