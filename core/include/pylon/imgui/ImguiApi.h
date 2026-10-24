// 层：内核层 / imgui —— PylonImgui_* 引擎面声明（P/Invoke 目标层）
//
// 分工：
//   · PylonImgui_* —— 引擎驾驶：悬浮窗（ANativeWindow + Vulkan）的生命周期、帧开始/结束、
//                     键盘活动守卫信号。这是"不属于 ImGui API"的部分，C# 每帧驱动用。
//   · ig*          —— ImGui API 全集（控件 + IO 输入），对齐 ImGui.NET / cimgui；
//                     C# 键盘桥用 ig* 的 IO 调用（igAddKeyEvent/igAddInputCharactersUTF8…）
//                     在托管端实现弹/收/增量同步，C++ 不为键盘写额外函数。
//
// 命名纪律：
//   · 布尔用 int32_t（0/1）；枚举/flags 用 int32_t；文本 UTF-8。
//   · 线程：全部游戏线程（UnityMain）调用。
#pragma once

#include <cstdint>

namespace pylon::imgui {

/// 导出标记。
#define PYLON_IMGUI_EXPORT extern "C" __attribute__((visibility("default")))

}  // namespace pylon::imgui

// ---------------------------------------------------------------------------
// 生命周期 / 帧（引擎面）
// ---------------------------------------------------------------------------

/// 启动悬浮窗（游戏线程，UI 就绪后）。1 成功 / 0 失败（原因见 PylonMod_LastError）。
PYLON_IMGUI_EXPORT int32_t PylonImgui_Start();
/// 停止悬浮窗（游戏退出/卸载前）。
PYLON_IMGUI_EXPORT void    PylonImgui_Stop();
/// 悬浮窗是否已初始化完成。
PYLON_IMGUI_EXPORT int32_t PylonImgui_IsInitialized();
/// 覆盖层显示尺寸（逻辑像素），out 可空。
PYLON_IMGUI_EXPORT void    PylonImgui_DisplaySize(float* width, float* height);

/// 每帧开始：表面健康检查（切后台恢复）+ Vulkan 取帧 + ImGui::NewFrame。同帧与 EndFrame 成对。
PYLON_IMGUI_EXPORT void    PylonImgui_BeginFrame();
/// 每帧结束：ImGui::Render + 呈现。
PYLON_IMGUI_EXPORT void    PylonImgui_EndFrame();

// ---------------------------------------------------------------------------
// UI 缩放（C# 设置界面用）：字体与控件尺寸一起缩放，1.0 = 默认
// ---------------------------------------------------------------------------

/// 记录当前样式为缩放基准（必须在主题应用之后调用一次；之后 SetUiScale 都从这个基准算）。
PYLON_IMGUI_EXPORT void    PylonImgui_CaptureUiScaleBase();
/// 设置整体 UI 比例（0.6–2.0）。同时缩放 style 度量与 FontScaleMain。
PYLON_IMGUI_EXPORT void    PylonImgui_SetUiScale(float scale);
/// 当前 UI 比例。
PYLON_IMGUI_EXPORT float   PylonImgui_GetUiScale();

// ---------------------------------------------------------------------------
// 键盘活动守卫（C# 键盘桥 → igSetKeyboardActive → ImGuiHost 读）
// ---------------------------------------------------------------------------

#include <atomic>
#include <chrono>

namespace pylon::imgui {

/// igSetKeyboardActive 写的时间戳（纳秒；0 = 无键盘活动）。
extern std::atomic<int64_t> g_keyboardActiveUntil;

/// 1.5s 窗口内视为键盘活跃（键盘收起动画的 adjustResize 噪声不该触发覆盖层重建）。
inline bool KeyboardRecentlyActive()
{
    const auto until = g_keyboardActiveUntil.load();
    return until > 0 && until > std::chrono::steady_clock::now().time_since_epoch().count();
}

}  // namespace pylon::imgui