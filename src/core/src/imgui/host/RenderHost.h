// 层：内核层 / imgui —— 覆盖层渲染面，也是 C# 侧唯一的 P/Invoke 入口（PylonImgui_*）
//
// 边界：C++ 只负责"一块能画的表面 + Vulkan 取帧/呈现"。
// ImGui 上下文、字体、样式、布局、触摸与键盘全部在 C# 侧，这里不碰 ImGui 的状态机，
// 所以本文件不出现任何 ImGui 类型：换 UI 框架时 C++ 不用动。
//
// 约定：布尔用 int32_t（0/1），尺寸单位是物理像素，全部在游戏线程调用
// （AttachSurface 例外，它由 Java UI 线程回调）。调用前 C# 必须已 CreateContext。
#pragma once

#include <jni.h>
#include <cstdint>

#define PYLON_IMGUI_EXPORT extern "C" __attribute__((visibility("default")))

/// 绑定一块 Java Surface（传 jobject 的全局引用句柄）。反射/建 View 全在 C# 侧，
/// 这里只做 Surface → ANativeWindow 与后续 Vulkan 呈现。返回 1 = 已接管，0 = 表面无效。
PYLON_IMGUI_EXPORT int32_t PylonImgui_AttachSurface(jobject surface);

/// 解绑并立即销毁 Vulkan 对象（会等 GPU 空闲）。
PYLON_IMGUI_EXPORT void PylonImgui_DetachSurface();

/// 表面是否可画。
PYLON_IMGUI_EXPORT int32_t PylonImgui_IsReady();

/// 表面物理像素尺寸；未就绪写 0。
PYLON_IMGUI_EXPORT void PylonImgui_PixelSize(int32_t* width, int32_t* height);

/// 取帧 + 通知 ImGui Vulkan 后端开始新帧。返回 1 表示调用方应绘制并调用 EndFrame。
PYLON_IMGUI_EXPORT int32_t PylonImgui_BeginFrame();
PYLON_IMGUI_EXPORT void PylonImgui_EndFrame();

/// 软键盘弹出会让窗口 adjustResize：这段时间内不重建交换链，避免连续重建。
PYLON_IMGUI_EXPORT void PylonImgui_SetKeyboardGuardMs(int32_t ms);
