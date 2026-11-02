// 层：内核层 / imgui —— 覆盖层对托管侧的唯一 C 出口（PylonImgui_*）
//
// 边界：托管侧拥有 ImGui 上下文、字体、样式、布局与输入；内核只负责"把这一帧画到
// 游戏自己的 GLES 表面上"。所以这里没有任何 Android Surface 概念。
//
// 线程：AttachContext / PixelSize / BeginFrame / EndFrame / Reset 都在游戏线程；
// 绘制发生在渲染线程的 eglSwapBuffers 之前（见 overlay/Present）。
#pragma once

#include <cstdint>

#define PYLON_IMGUI_EXPORT extern "C" __attribute__((visibility("default")))

/// 覆盖层是否可画：后端已在渲染线程初始化，且见过至少一次表面尺寸。
PYLON_IMGUI_EXPORT int32_t PylonImgui_IsReady();

/// 把托管侧刚 CreateContext 出来的上下文交给内核（渲染线程要挂同一个）。
PYLON_IMGUI_EXPORT void PylonImgui_AttachContext(void* context);

/// 表面物理像素尺寸；未就绪写 0。
PYLON_IMGUI_EXPORT void PylonImgui_PixelSize(int32_t* width, int32_t* height);

/// 开始一帧：返回 1 = 调用方可以 NewFrame。成功后必须配对 EndFrame。
PYLON_IMGUI_EXPORT int32_t PylonImgui_BeginFrame();

/// 结束一帧：把这一帧拷成快照交给渲染线程，下个 eglSwapBuffers 之前上屏。
PYLON_IMGUI_EXPORT void PylonImgui_EndFrame();

/// 撤销覆盖层。必须在 DestroyContext 之前调用：后端的 GL 对象要趁上下文还在才能销毁。
PYLON_IMGUI_EXPORT void PylonImgui_Reset();
