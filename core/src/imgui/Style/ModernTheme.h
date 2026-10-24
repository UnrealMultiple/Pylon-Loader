#pragma once

// ============================================================================
// ModernTheme.h
// 现代暗色 UI 主题（仿 UI · 暗色为主）。配色与度量抄自 “仿UI” 演示工程的
// 深色主题（Dark()）：近黑底 + 深色卡片 + 蓝紫强调色 #7C74FF + 大圆角柔和风。
// 统一通过 ImGuiStyle 设置配色与度量，让所有窗口 / 标签页 / 按钮 / 输入框 /
// 滑条等控件整体呈现 “暗色底 + 单一强调色 + 大圆角” 观感。
// 仅需在 ImGui 上下文创建后调用一次 Apply()。
// ============================================================================

#include "../imgui.h"

namespace ImGuiTheme {

// 0xRRGGBB -> ImVec4（alpha 可调）
static inline ImVec4 Hex(unsigned rgb, float a = 1.0f) {
    return ImVec4(
        ((rgb >> 16) & 0xFF) / 255.0f,
        ((rgb >> 8)  & 0xFF) / 255.0f,
        ( rgb        & 0xFF) / 255.0f,
        a);
}

inline void Apply() {
    ImGuiStyle& s = ImGui::GetStyle();
    ImVec4* c = s.Colors;

    // ---- 强调色（蓝紫 #7C74FF，抄自 仿UI 深色主题） ----
    const ImVec4 accent       = Hex(0x7C74FF);
    const ImVec4 accentHover  = Hex(0x8F88FF);
    const ImVec4 accentActive = Hex(0x6A5AE0);

    // ---- 基础表面（仿UI Dark：近黑底 0x0A0A0C / 卡片 0x15151B） ----
    const ImVec4 card       = Hex(0x15151B);
    const ImVec4 cardHi     = Hex(0x17171E);   // 标题栏/激活表面略亮
    const ImVec4 cardBorder = Hex(0xFFFFFF, 0.08f);

    // ---- 文本 ----
    c[ImGuiCol_Text]                 = Hex(0xF4F4F6);
    c[ImGuiCol_TextDisabled]         = Hex(0xF4F4F6, 0.45f);
    c[ImGuiCol_TextLink]             = accentHover;
    c[ImGuiCol_TextSelectedBg]       = Hex(0x7C74FF, 0.40f);

    // ---- 窗口 / 子窗口 / 弹窗 ----
    c[ImGuiCol_WindowBg]             = card;
    c[ImGuiCol_ChildBg]              = ImVec4(0.0f, 0.0f, 0.0f, 0.0f);   // 子窗口透明，露出背景特效
    c[ImGuiCol_PopupBg]              = Hex(0x1C1C26);
    c[ImGuiCol_Border]               = Hex(0xFFFFFF, 0.12f);   // 输入框/下拉/复选框的细描边（窗口/弹窗边框尺寸为 0 不受影响）
    c[ImGuiCol_BorderShadow]         = ImVec4(0.0f, 0.0f, 0.0f, 0.0f);

    // ---- 标题栏 / 菜单栏 ----
    c[ImGuiCol_TitleBg]              = card;
    c[ImGuiCol_TitleBgActive]        = cardHi;
    c[ImGuiCol_TitleBgCollapsed]     = card;
    c[ImGuiCol_MenuBarBg]            = card;

    // ---- 输入框 / 复选框 / 滑条底框（淡紫半透明，玻璃质感） ----
    c[ImGuiCol_FrameBg]              = Hex(0x7C74FF, 0.14f);
    c[ImGuiCol_FrameBgHovered]       = Hex(0x7C74FF, 0.22f);
    c[ImGuiCol_FrameBgActive]        = Hex(0x7C74FF, 0.28f);
    c[ImGuiCol_CheckMark]            = ImVec4(1.0f, 1.0f, 1.0f, 1.0f);
    c[ImGuiCol_SliderGrab]           = accent;
    c[ImGuiCol_SliderGrabActive]     = accentHover;

    // ---- 按钮（暗色底，悬停泛紫；主操作请用 UI::AccentButton 实心紫） ----
    c[ImGuiCol_Button]               = Hex(0x20242F);
    c[ImGuiCol_ButtonHovered]        = Hex(0x2B3250);
    c[ImGuiCol_ButtonActive]         = Hex(0x353D63);

    // ---- 折叠头 / 树节点 / 可选项 / 菜单项 ----
    c[ImGuiCol_Header]               = Hex(0x7C74FF, 0.18f);
    c[ImGuiCol_HeaderHovered]        = Hex(0x7C74FF, 0.26f);
    c[ImGuiCol_HeaderActive]         = Hex(0x7C74FF, 0.32f);

    // ---- 分隔线 ----
    c[ImGuiCol_Separator]            = cardBorder;
    c[ImGuiCol_SeparatorHovered]     = Hex(0x8F88FF, 0.50f);
    c[ImGuiCol_SeparatorActive]      = accent;

    // ---- 滚动条（更窄更亮，对齐 HTML 的 4~5px 紫滚动条） ----
    c[ImGuiCol_ScrollbarBg]          = ImVec4(0.0f, 0.0f, 0.0f, 0.0f);
    c[ImGuiCol_ScrollbarGrab]        = Hex(0x8F88FF, 0.32f);
    c[ImGuiCol_ScrollbarGrabHovered] = Hex(0x8F88FF, 0.52f);
    c[ImGuiCol_ScrollbarGrabActive]  = Hex(0xA99CFF, 0.68f);

    // ---- 标签页（透明底 + 选中淡紫 + 上沿强调条） ----
    c[ImGuiCol_Tab]                          = ImVec4(0.0f, 0.0f, 0.0f, 0.0f);
    c[ImGuiCol_TabHovered]                   = Hex(0x7C74FF, 0.20f);
    c[ImGuiCol_TabSelected]                  = Hex(0x7C74FF, 0.16f);
    c[ImGuiCol_TabSelectedOverline]          = accent;
    c[ImGuiCol_TabDimmed]                    = ImVec4(0.0f, 0.0f, 0.0f, 0.0f);
    c[ImGuiCol_TabDimmedSelected]            = Hex(0x7C74FF, 0.12f);
    c[ImGuiCol_TabDimmedSelectedOverline]    = Hex(0x7C74FF, 0.55f);

    // ---- 表格（表头淡紫 + 更亮边框 + 行条纹） ----
    c[ImGuiCol_TableHeaderBg]        = Hex(0x7C74FF, 0.14f);
    c[ImGuiCol_TableBorderStrong]    = Hex(0x8F88FF, 0.35f);
    c[ImGuiCol_TableBorderLight]     = Hex(0xFFFFFF, 0.08f);
    c[ImGuiCol_TableRowBg]           = ImVec4(0.0f, 0.0f, 0.0f, 0.0f);
    c[ImGuiCol_TableRowBgAlt]        = Hex(0xFFFFFF, 0.045f);

    // ---- 其它 ----
    c[ImGuiCol_PlotLines]            = accent;
    c[ImGuiCol_PlotLinesHovered]     = accentHover;
    c[ImGuiCol_PlotHistogram]        = accent;
    c[ImGuiCol_PlotHistogramHovered] = accentHover;
    c[ImGuiCol_ResizeGrip]           = Hex(0x7C74FF, 0.25f);
    c[ImGuiCol_ResizeGripHovered]    = accent;
    c[ImGuiCol_ResizeGripActive]     = accentHover;
    c[ImGuiCol_DragDropTarget]       = Hex(0x7C74FF, 0.90f);
    c[ImGuiCol_NavCursor]            = accent;
    c[ImGuiCol_ModalWindowDimBg]     = Hex(0x000000, 0.55f);

    // ---- 度量 / 圆角 / 间距（大圆角 + 宽松间距，抄自 仿UI ApplyStyle） ----
    s.WindowPadding        = ImVec2(20.0f, 16.0f);
    s.WindowRounding       = 24.0f;
    s.WindowBorderSize     = 0.0f;
    s.WindowTitleAlign     = ImVec2(0.5f, 0.5f);
    s.ChildRounding        = 16.0f;
    s.ChildBorderSize      = 0.0f;
    s.PopupRounding        = 14.0f;
    s.PopupBorderSize      = 0.0f;
    s.FramePadding         = ImVec2(14.0f, 10.0f);
    s.FrameRounding        = 12.0f;
    s.FrameBorderSize      = 1.0f;     // 输入框/下拉/复选框等带细描边（自定义 UI::* 控件不受影响）
    s.ItemSpacing          = ImVec2(12.0f, 12.0f);
    s.ItemInnerSpacing     = ImVec2(8.0f, 6.0f);
    s.CellPadding          = ImVec2(10.0f, 6.0f);
    s.IndentSpacing        = 26.0f;
    s.ScrollbarSize        = 6.0f;
    s.ScrollbarRounding    = 6.0f;
    s.GrabMinSize          = 18.0f;
    s.GrabRounding         = 12.0f;
    s.TabRounding          = 10.0f;
    s.TabBorderSize        = 0.0f;
    s.TabMinWidthBase      = 60.0f;
    s.TabBarBorderSize     = 0.0f;
    s.TabBarOverlineSize   = 3.0f;
    s.SeparatorTextBorderSize = 2.0f;
    s.ButtonTextAlign      = ImVec2(0.5f, 0.5f);

    // ---- 抗锯齿 ----
    s.AntiAliasedLines      = true;
    s.AntiAliasedLinesUseTex = true;
    s.AntiAliasedFill       = true;
}

// ============================================================================
// 运行时整体 UI 缩放（设置页可调）
// userScale = 1.0 对应 ImGuiSetup 里 ScaleAllSizes(1.25) 的默认触屏尺寸。
// 每次变化时按比例缩放 style 度量，并用 style.FontScaleMain 缩放字体渲染。
// 注意：本仓库是 ImGui 1.92+，io.FontGlobalScale 已废弃（会绕过动态字库的
// 字形重烘焙，导致缩放后文字乱码）；字体整体缩放必须用 style.FontScaleMain。
// 幂等：在每帧绘制前调用一次即可；仅当数值变化时才真正改动。
// ============================================================================
inline void ApplyUIScale(float userScale) {
    ImGuiStyle& style = ImGui::GetStyle();
    static float s_applied = 1.0f;          // 已应用的 userScale
    if (userScale == s_applied) return;
    const float ratio = userScale / s_applied;
    style.ScaleAllSizes(ratio);
    style.FontScaleMain *= ratio;
    s_applied = userScale;
}

} // namespace ImGuiTheme
