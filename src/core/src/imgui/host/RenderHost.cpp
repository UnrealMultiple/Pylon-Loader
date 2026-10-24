#include "RenderHost.h"

#include "graphics/Pipe.h"
#include "imgui.h"
#include "imgui/overlay/Present.h"
#include "imgui/overlay/Session.h"

extern "C" {

int32_t PylonImgui_IsReady()
{
    return pylon::imgui::Present::Ready() ? 1 : 0;
}

void PylonImgui_AttachContext(void* context)
{
    pylon::imgui::Session::Attach(context);
    // 覆盖层挂在渲染管线的帧末：图形层只管回调，不认识 ImGui。
    pylon::graphics::Pipe::SetOverlay(&pylon::imgui::Present::AtSwap);
}

void PylonImgui_PixelSize(int32_t* width, int32_t* height)
{
    pylon::imgui::Present::Size(width, height);
}

int32_t PylonImgui_BeginFrame()
{
    return pylon::imgui::Session::Begin() ? 1 : 0;
}

void PylonImgui_EndFrame()
{
    pylon::imgui::Session::End();
}

void PylonImgui_Reset()
{
    pylon::imgui::Session::Reset();
}

}  // extern "C"
