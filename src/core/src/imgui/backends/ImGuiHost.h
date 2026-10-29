#pragma once

#include <functional>

#include "../imgui.h"

namespace ImGuiHost
{

struct InitOptions
{
    bool secureCapture = false;
    std::function<void()> render;
};

bool Init(const InitOptions& opt);
void Clean();
void Tick();

/// 每帧渲染（与 PylonImgui_BeginFrame/EndFrame 对应）：Tick 只做生命周期/表面健康检查，渲染由 C# 显式调（"C++ 只提供 API"）；同帧内必须成对调用。
void BeginFrame();
void EndFrame();

bool IsInitialized();
ImVec2 GetDisplaySize();
void SetCaptureHiding(bool enabled);
void RequestSurfaceRebuild();

} // namespace ImGuiHost
