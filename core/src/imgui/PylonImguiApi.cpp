#include "pylon/imgui/ImguiApi.h"

#include <atomic>
#include <chrono>

#include "../imgui/backends/ImGuiHost.h"
#include "pylon/Log.h"

namespace pylon::imgui {

// 键盘活动守卫时间戳：C# 键盘桥在键盘显示/收起时调 igSetKeyboardActive(1/0)，
// C++ 在 1.5s 窗口内视为"键盘活跃"，ImGuiHost 跳过 surface 重建（否则键盘收起
// 动画触发 adjustResize → 覆盖层重建 → 闪一帧游戏画面）。
std::atomic<int64_t> g_keyboardActiveUntil{0};

}  // namespace pylon::imgui

// ---------------------------------------------------------------------------
// 生命周期 / 帧（引擎面）
// ---------------------------------------------------------------------------

PYLON_IMGUI_EXPORT int32_t PylonImgui_Start()
{
    // render 回调留空：C# 侧显式调 BeginFrame/EndFrame（"C++ 只提供 api"的分工）。
    ImGuiHost::InitOptions options;
    options.render = nullptr;
    if (!ImGuiHost::Init(options))
    {
        LOGE("imgui", "PylonImgui_Start: ImGuiHost::Init failed");
        return 0;
    }
    return 1;
}

PYLON_IMGUI_EXPORT void PylonImgui_Stop() { ImGuiHost::Clean(); }

PYLON_IMGUI_EXPORT int32_t PylonImgui_IsInitialized()
{
    return ImGuiHost::IsInitialized() ? 1 : 0;
}

PYLON_IMGUI_EXPORT void PylonImgui_DisplaySize(float* width, float* height)
{
    const ImVec2 size = ImGuiHost::GetDisplaySize();
    if (width) *width = size.x;
    if (height) *height = size.y;
}

PYLON_IMGUI_EXPORT void PylonImgui_BeginFrame()
{
    // 先跑生命周期/表面健康检查（Tick）：切后台回来覆盖层 Surface 被销毁后，
    // 靠它检测活动变化/焦点恢复并重建 Vulkan 表面。之前只调 BeginFrame 不调
    // Tick，切后台回来悬浮窗就消失了。
    ImGuiHost::Tick();
    ImGuiHost::BeginFrame();
}

PYLON_IMGUI_EXPORT void PylonImgui_EndFrame()
{
    ImGuiHost::EndFrame();
}

// ---------------------------------------------------------------------------
// UI 缩放（C# 设置界面用）
// ---------------------------------------------------------------------------

namespace {
// ScaleAllSizes 是"有损"的（内部 ImTrunc 舍入），所以不能反复在当前 style 上乘比例。
// 记一份主题应用后的基准，每次缩放都从基准重算，避免拖滑条时尺寸越缩越偏。
ImGuiStyle g_uiScaleBase;
bool g_uiScaleBaseCaptured = false;
float g_uiScale = 1.0f;

float ClampUiScale(float scale)
{
    if (!(scale > 0.0f)) return 1.0f;
    if (scale < 0.6f) return 0.6f;
    if (scale > 2.0f) return 2.0f;
    return scale;
}
}  // namespace

PYLON_IMGUI_EXPORT void PylonImgui_CaptureUiScaleBase()
{
    if (ImGui::GetCurrentContext() == nullptr) return;
    g_uiScaleBase = ImGui::GetStyle();
    g_uiScaleBaseCaptured = true;
    g_uiScale = 1.0f;
}

PYLON_IMGUI_EXPORT void PylonImgui_SetUiScale(float scale)
{
    if (ImGui::GetCurrentContext() == nullptr) return;
    if (!g_uiScaleBaseCaptured) {
        g_uiScaleBase = ImGui::GetStyle();
        g_uiScaleBaseCaptured = true;
    }

    scale = ClampUiScale(scale);
    ImGuiStyle scaled = g_uiScaleBase;
    scaled.ScaleAllSizes(scale);
    scaled.FontScaleMain = scale;
    ImGui::GetStyle() = scaled;
    g_uiScale = scale;
}

PYLON_IMGUI_EXPORT float PylonImgui_GetUiScale() { return g_uiScale; }
