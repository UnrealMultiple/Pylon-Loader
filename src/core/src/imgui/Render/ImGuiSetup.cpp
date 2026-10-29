#include "ImGuiSetup.h"

#include <android/native_window.h>

#include "../Font/Font.h"
#include "../imgui.h"
#include "../Style/ModernTheme.h"

namespace ImGuiSetup
{

static bool contextReady = false;

void EnsureContext(ANativeWindow *platformWindow)
{
    if (contextReady)
    {
            return;
    }

    IMGUI_CHECKVERSION();
    ImGui::CreateContext();

    ImGuiIO &io = ImGui::GetIO();
    io.IniFilename = nullptr;
    io.ConfigFlags |= ImGuiConfigFlags_NavEnableKeyboard;
    io.ConfigInputTrickleEventQueue = false;
    // 触摸端：手指在按/放之间会有轻微位移，默认 6px 的拖动阈值会让 Tab/按钮被判成拖动，
    // 造成“要点两下才生效”。调大阈值让单击更稳定（窗口拖动需 >此距离才触发）。
    io.MouseDragThreshold = 14.0f;
    // 只允许拖动标题栏来移动窗口。这样在窗口内容区上下滑动只会产生滚动（见
    // imgui_impl_android 的 swipe-to-scroll），不会把整个窗口拖走。
    io.ConfigWindowsMoveFromTitleBarOnly = true;
    io.Fonts->Clear();
    ImFontConfig fontConfig;
    fontConfig.OversampleH = 1;
    fontConfig.OversampleV = 1;
    fontConfig.PixelSnapH = true;
    fontConfig.FontDataOwnedByAtlas = false;
    io.Fonts->AddFontFromMemoryTTF(const_cast<unsigned int*>(Window_data),static_cast<int>(Window_size),45.0f,&fontConfig,io.Fonts->GetGlyphRangesChineseFull());

    ImGuiTheme::Apply();
    ImGuiStyle &style = ImGui::GetStyle();
    // 触屏端整体放大 UI（字体已在 45px，此处同步放大间距/圆角/控件高度）
    style.ScaleAllSizes(1.25f);

    contextReady = true;
}

void TeardownContext()
{
    if (!contextReady)
        return;

    ImGui::DestroyContext();

    contextReady = false;
}

} // namespace ImGuiSetup
