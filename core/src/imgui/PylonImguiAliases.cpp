#include "pylon/imgui/ImguiApi.h"

#include <atomic>
#include <chrono>

#include "../imgui/imgui.h"
#include "../imgui/imgui_internal.h"
#include "../imgui/Graphics/VulkanTexture.h"

// ---------------------------------------------------------------------------
// 窗口
// ---------------------------------------------------------------------------

PYLON_IMGUI_EXPORT int32_t igBegin(const char* name, int32_t* p_open, int32_t flags)
{
    bool opened = p_open ? (*p_open != 0) : true;
    const bool visible = ImGui::Begin(name ? name : "", p_open ? &opened : nullptr, flags);
    if (p_open) *p_open = opened ? 1 : 0;
    return visible ? 1 : 0;
}
PYLON_IMGUI_EXPORT void    igEnd() { ImGui::End(); }

PYLON_IMGUI_EXPORT int32_t igBeginChild_Str(const char* str_id, float size_x, float size_y,
                                            int32_t child_flags, int32_t window_flags)
{
    return ImGui::BeginChild(str_id, ImVec2(size_x, size_y), child_flags, window_flags) ? 1 : 0;
}
PYLON_IMGUI_EXPORT void    igEndChild() { ImGui::EndChild(); }

PYLON_IMGUI_EXPORT void    igSetNextWindowPos(float x, float y, int32_t cond) { ImGui::SetNextWindowPos(ImVec2(x, y), static_cast<ImGuiCond>(cond)); }
PYLON_IMGUI_EXPORT void    igSetNextWindowBgAlpha(float alpha) { ImGui::SetNextWindowBgAlpha(alpha); }
PYLON_IMGUI_EXPORT void    igSetNextWindowSize(float w, float h, int32_t cond) { ImGui::SetNextWindowSize(ImVec2(w, h), static_cast<ImGuiCond>(cond)); }
PYLON_IMGUI_EXPORT void    igGetWindowPos(float* x, float* y) { const ImVec2 p = ImGui::GetWindowPos(); if (x) *x = p.x; if (y) *y = p.y; }
PYLON_IMGUI_EXPORT void    igSetWindowPos(float x, float y) { ImGui::SetWindowPos(ImVec2(x, y)); }
PYLON_IMGUI_EXPORT void    igGetWindowSize(float* w, float* h) { const ImVec2 s = ImGui::GetWindowSize(); if (w) *w = s.x; if (h) *h = s.y; }
PYLON_IMGUI_EXPORT float   igGetScrollX() { return ImGui::GetScrollX(); }
PYLON_IMGUI_EXPORT float   igGetScrollY() { return ImGui::GetScrollY(); }
PYLON_IMGUI_EXPORT void    igSetScrollHereY(float ratio) { ImGui::SetScrollHereY(ratio); }
PYLON_IMGUI_EXPORT void    igSetScrollY(float y) { ImGui::SetScrollY(y); }
PYLON_IMGUI_EXPORT int32_t igIsWindowAppearing() { return ImGui::IsWindowAppearing() ? 1 : 0; }
PYLON_IMGUI_EXPORT int32_t igIsWindowCollapsed() { return ImGui::IsWindowCollapsed() ? 1 : 0; }

// ---------------------------------------------------------------------------
// 布局 / 光标
// ---------------------------------------------------------------------------

PYLON_IMGUI_EXPORT void    igSameLine(float offset_from_start_x, float spacing) { ImGui::SameLine(offset_from_start_x, spacing); }
PYLON_IMGUI_EXPORT void    igNewLine() { ImGui::NewLine(); }
PYLON_IMGUI_EXPORT void    igSpacing() { ImGui::Spacing(); }
PYLON_IMGUI_EXPORT void    igDummy(float w, float h) { ImGui::Dummy(ImVec2(w, h)); }
PYLON_IMGUI_EXPORT void    igSeparator() { ImGui::Separator(); }
PYLON_IMGUI_EXPORT void    igSeparatorText(const char* label) { ImGui::SeparatorText(label ? label : ""); }
PYLON_IMGUI_EXPORT void    igIndent(float indent_w) { ImGui::Indent(indent_w); }
PYLON_IMGUI_EXPORT void    igUnindent(float indent_w) { ImGui::Unindent(indent_w); }
PYLON_IMGUI_EXPORT void    igBeginGroup() { ImGui::BeginGroup(); }
PYLON_IMGUI_EXPORT void    igEndGroup() { ImGui::EndGroup(); }
PYLON_IMGUI_EXPORT void    igAlignTextToFramePadding() { ImGui::AlignTextToFramePadding(); }
PYLON_IMGUI_EXPORT float   igGetCursorPosX() { return ImGui::GetCursorPosX(); }
PYLON_IMGUI_EXPORT float   igGetCursorPosY() { return ImGui::GetCursorPosY(); }
PYLON_IMGUI_EXPORT void    igSetCursorPos(float x, float y) { ImGui::SetCursorPos(ImVec2(x, y)); }
PYLON_IMGUI_EXPORT void    igGetContentRegionAvail(float* w, float* h) { const ImVec2 s = ImGui::GetContentRegionAvail(); if (w) *w = s.x; if (h) *h = s.y; }
PYLON_IMGUI_EXPORT float   igGetFrameHeight() { return ImGui::GetFrameHeight(); }
PYLON_IMGUI_EXPORT float   igGetFrameHeightWithSpacing() { return ImGui::GetFrameHeightWithSpacing(); }
PYLON_IMGUI_EXPORT float   igGetTextLineHeight() { return ImGui::GetTextLineHeight(); }

// ---------------------------------------------------------------------------
// 文本（内容一律当普通文本，%s 转义）
// ---------------------------------------------------------------------------

PYLON_IMGUI_EXPORT void igText(const char* fmt) { ImGui::TextUnformatted(fmt ? fmt : ""); }
PYLON_IMGUI_EXPORT void igTextColored(float r, float g, float b, float a, const char* fmt)
{
    ImGui::TextColored(ImVec4(r, g, b, a), "%s", fmt ? fmt : "");
}
PYLON_IMGUI_EXPORT void igTextDisabled(const char* fmt) { ImGui::TextDisabled("%s", fmt ? fmt : ""); }
PYLON_IMGUI_EXPORT void igTextWrapped(const char* fmt) { ImGui::TextWrapped("%s", fmt ? fmt : ""); }
PYLON_IMGUI_EXPORT void igLabelText(const char* label, const char* fmt)
{
    ImGui::LabelText(label ? label : "", "%s", fmt ? fmt : "");
}
PYLON_IMGUI_EXPORT void igBulletText(const char* fmt) { ImGui::BulletText("%s", fmt ? fmt : ""); }

// ---------------------------------------------------------------------------
// 按钮 / 选择
// ---------------------------------------------------------------------------

PYLON_IMGUI_EXPORT int32_t igButton(const char* label, float w, float h)
{
    return ImGui::Button(label ? label : "", ImVec2(w, h)) ? 1 : 0;
}
PYLON_IMGUI_EXPORT int32_t igSmallButton(const char* label)
{
    return ImGui::SmallButton(label ? label : "") ? 1 : 0;
}
PYLON_IMGUI_EXPORT int32_t igInvisibleButton(const char* id, float w, float h, int32_t flags)
{
    return ImGui::InvisibleButton(id ? id : "", ImVec2(w, h), flags) ? 1 : 0;
}
PYLON_IMGUI_EXPORT void igGetCursorScreenPos(float* x, float* y)
{
    const ImVec2 p = ImGui::GetCursorScreenPos();
    if (x) *x = p.x;
    if (y) *y = p.y;
}
PYLON_IMGUI_EXPORT void igSetCursorScreenPos(float x, float y)
{
    ImGui::SetCursorScreenPos(ImVec2(x, y));
}
PYLON_IMGUI_EXPORT float igGetWindowWidth() { return ImGui::GetWindowWidth(); }
PYLON_IMGUI_EXPORT float igGetWindowHeight() { return ImGui::GetWindowHeight(); }
PYLON_IMGUI_EXPORT int32_t igIsMouseHoveringRect(float x0, float y0, float x1, float y1, int32_t allowWhenDisabled)
{
    return ImGui::IsMouseHoveringRect(ImVec2(x0, y0), ImVec2(x1, y1), allowWhenDisabled != 0) ? 1 : 0;
}
/// 按指定字号计算文本尺寸（Chrome 用：字号与几何同比例缩放）。
PYLON_IMGUI_EXPORT void igCalcTextSizeA(float fontSize, const char* text, float* out_w, float* out_h)
{
    const ImVec2 s = ImGui::GetFont()->CalcTextSizeA(fontSize, FLT_MAX, 0.0f, text ? text : "");
    if (out_w) *out_w = s.x;
    if (out_h) *out_h = s.y;
}
/// 按指定字号绘制文本（Chrome 用：标题/副标题/导航标签按比例字号）。
PYLON_IMGUI_EXPORT void igDL_AddTextFont(void* dl, float x, float y, uint32_t col, const char* utf8, float fontSize)
{
    ((ImDrawList*)dl)->AddText(ImGui::GetFont(), fontSize, ImVec2(x, y), col, utf8 ? utf8 : "");
}
/// 带 ImDrawFlags 的圆角矩形（侧栏 RoundCornersBottom 等）。
PYLON_IMGUI_EXPORT void igDL_AddRectFilledEx(void* dl, float x0, float y0, float x1, float y1,
                                             uint32_t col, float rounding, int32_t flags)
{
    ((ImDrawList*)dl)->AddRectFilled(ImVec2(x0, y0), ImVec2(x1, y1), col, rounding, flags);
}
/// 带 ImDrawFlags 的描边矩形。
PYLON_IMGUI_EXPORT void igDL_AddRectEx(void* dl, float x0, float y0, float x1, float y1,
                                       uint32_t col, float rounding, int32_t flags, float thickness)
{
    ((ImDrawList*)dl)->AddRect(ImVec2(x0, y0), ImVec2(x1, y1), col, rounding, flags, thickness);
}
/// 实心四边形（品牌菱形 logo 用）。
PYLON_IMGUI_EXPORT void igDL_AddQuadFilled(void* dl, float x0, float y0, float x1, float y1,
                                           float x2, float y2, float x3, float y3, uint32_t col)
{
    ((ImDrawList*)dl)->AddQuadFilled(ImVec2(x0, y0), ImVec2(x1, y1), ImVec2(x2, y2), ImVec2(x3, y3), col);
}
/// 实心凸多边形（四角星 logo / 星星用）；xy 为连续的 [x0,y0, x1,y1, ...]，最多 16 点。
PYLON_IMGUI_EXPORT void igDL_AddConvexPolyFilled(void* dl, const float* xy, int32_t count, uint32_t col)
{
    ImVec2 pts[16];
    const int n = count > 16 ? 16 : count;
    for (int i = 0; i < n; i++) { pts[i] = ImVec2(xy[i * 2], xy[i * 2 + 1]); }
    ((ImDrawList*)dl)->AddConvexPolyFilled(pts, n, col);
}
PYLON_IMGUI_EXPORT int32_t igCheckbox(const char* label, int32_t* v)
{
    if (!v) return 0;
    bool b = *v != 0;
    if (!ImGui::Checkbox(label ? label : "", &b)) return 0;
    *v = b ? 1 : 0;
    return 1;
}
PYLON_IMGUI_EXPORT int32_t igRadioButton(const char* label, int32_t* v, int32_t v_button)
{
    if (!v || !ImGui::RadioButton(label ? label : "", *v == v_button)) return 0;
    *v = v_button;
    return 1;
}
PYLON_IMGUI_EXPORT void igProgressBar(float fraction, float w, float h, const char* overlay)
{
    ImGui::ProgressBar(fraction, ImVec2(w, h), overlay);
}
PYLON_IMGUI_EXPORT void igBullet() { ImGui::Bullet(); }

// ---------------------------------------------------------------------------
// 数值输入 / 拖动 / 滑块
// ---------------------------------------------------------------------------

PYLON_IMGUI_EXPORT int32_t igInputText(const char* label, char* buf, int32_t buf_size)
{
    if (!buf || buf_size <= 0) return 0;
    return ImGui::InputText(label ? label : "", buf, static_cast<size_t>(buf_size)) ? 1 : 0;
}
PYLON_IMGUI_EXPORT int32_t igInputInt(const char* label, int32_t* v, int32_t step, int32_t step_fast)
{
    if (!v) return 0;
    return ImGui::InputInt(label ? label : "", v, step, step_fast) ? 1 : 0;
}
PYLON_IMGUI_EXPORT int32_t igInputFloat(const char* label, float* v, float step, float step_fast, const char* format)
{
    if (!v) return 0;
    return ImGui::InputFloat(label ? label : "", v, step, step_fast, format) ? 1 : 0;
}
PYLON_IMGUI_EXPORT int32_t igDragFloat(const char* label, float* v, float speed, float v_min, float v_max, const char* format)
{
    if (!v) return 0;
    return ImGui::DragFloat(label ? label : "", v, speed, v_min, v_max, format) ? 1 : 0;
}
PYLON_IMGUI_EXPORT int32_t igDragInt(const char* label, int32_t* v, float speed, int32_t v_min, int32_t v_max, const char* format)
{
    if (!v) return 0;
    return ImGui::DragInt(label ? label : "", v, speed, v_min, v_max, format) ? 1 : 0;
}
PYLON_IMGUI_EXPORT int32_t igSliderFloat(const char* label, float* v, float v_min, float v_max, const char* format)
{
    if (!v) return 0;
    return ImGui::SliderFloat(label ? label : "", v, v_min, v_max, format) ? 1 : 0;
}
PYLON_IMGUI_EXPORT int32_t igSliderInt(const char* label, int32_t* v, int32_t v_min, int32_t v_max, const char* format)
{
    if (!v) return 0;
    return ImGui::SliderInt(label ? label : "", v, v_min, v_max, format) ? 1 : 0;
}

// ---------------------------------------------------------------------------
// 下拉 / 列表
// ---------------------------------------------------------------------------

PYLON_IMGUI_EXPORT int32_t igCombo(const char* label, int32_t* current, const char** items, int32_t items_count)
{
    if (!current || !items || items_count <= 0) return 0;
    return ImGui::Combo(label ? label : "", current, items, items_count) ? 1 : 0;
}
PYLON_IMGUI_EXPORT int32_t igListBox(const char* label, int32_t* current, const char** items, int32_t items_count, int32_t height_in_items)
{
    if (!current || !items || items_count <= 0) return 0;
    return ImGui::ListBox(label ? label : "", current, items, items_count, height_in_items) ? 1 : 0;
}

// ---------------------------------------------------------------------------
// 颜色
// ---------------------------------------------------------------------------

PYLON_IMGUI_EXPORT int32_t igColorEdit4(const char* label, float* col, int32_t flags)
{
    if (!col) return 0;
    return ImGui::ColorEdit4(label ? label : "", col, flags) ? 1 : 0;
}
PYLON_IMGUI_EXPORT int32_t igColorPicker4(const char* label, float* col, int32_t flags)
{
    if (!col) return 0;
    return ImGui::ColorPicker4(label ? label : "", col, flags) ? 1 : 0;
}

// ---------------------------------------------------------------------------
// 树 / 折叠
// ---------------------------------------------------------------------------

PYLON_IMGUI_EXPORT int32_t igTreeNode(const char* label)
{
    return ImGui::TreeNodeEx(label ? label : "", ImGuiTreeNodeFlags_DefaultOpen, "%s", label ? label : "") ? 1 : 0;
}
PYLON_IMGUI_EXPORT int32_t igCollapsingHeader(const char* label)
{
    return ImGui::CollapsingHeader(label ? label : "") ? 1 : 0;
}
PYLON_IMGUI_EXPORT void    igTreePush(const char* str_id) { ImGui::TreePush(str_id ? str_id : ""); }
PYLON_IMGUI_EXPORT void    igTreePop() { ImGui::TreePop(); }

// ---------------------------------------------------------------------------
// 表格
// ---------------------------------------------------------------------------

PYLON_IMGUI_EXPORT int32_t igBeginTable(const char* str_id, int32_t column, int32_t flags)
{
    return ImGui::BeginTable(str_id ? str_id : "", column, flags) ? 1 : 0;
}
PYLON_IMGUI_EXPORT void    igTableSetupColumn(const char* label, int32_t flags, float init_width, int32_t column)
{
    ImGui::TableSetupColumn(label ? label : "", flags, init_width, column);
}
PYLON_IMGUI_EXPORT void    igTableHeadersRow() { ImGui::TableHeadersRow(); }
PYLON_IMGUI_EXPORT void    igTableNextRow(float min_row_height) { ImGui::TableNextRow(0, min_row_height); }
PYLON_IMGUI_EXPORT int32_t igTableNextColumn() { return ImGui::TableNextColumn() ? 1 : 0; }
PYLON_IMGUI_EXPORT void    igEndTable() { ImGui::EndTable(); }

// ---------------------------------------------------------------------------
// 标签页
// ---------------------------------------------------------------------------

PYLON_IMGUI_EXPORT int32_t igBeginTabBar(const char* str_id, int32_t flags) { return ImGui::BeginTabBar(str_id ? str_id : "", flags) ? 1 : 0; }
PYLON_IMGUI_EXPORT int32_t igBeginTabItem(const char* label, int32_t* p_open, int32_t flags)
{
    bool opened = p_open ? (*p_open != 0) : true;
    const bool visible = ImGui::BeginTabItem(label ? label : "", p_open ? &opened : nullptr, flags);
    if (p_open) *p_open = opened ? 1 : 0;
    return visible ? 1 : 0;
}
PYLON_IMGUI_EXPORT void    igEndTabItem() { ImGui::EndTabItem(); }
PYLON_IMGUI_EXPORT void    igEndTabBar() { ImGui::EndTabBar(); }

// ---------------------------------------------------------------------------
// 弹窗 / 菜单 / 工具提示
// ---------------------------------------------------------------------------

PYLON_IMGUI_EXPORT void    igOpenPopup(const char* str_id, int32_t flags) { ImGui::OpenPopup(str_id ? str_id : "", flags); }
PYLON_IMGUI_EXPORT int32_t igBeginPopupModal(const char* name, int32_t* p_open, int32_t flags)
{
    bool opened = p_open ? (*p_open != 0) : true;
    const bool visible = ImGui::BeginPopupModal(name ? name : "", p_open ? &opened : nullptr, flags);
    if (p_open) *p_open = opened ? 1 : 0;
    return visible ? 1 : 0;
}
PYLON_IMGUI_EXPORT void    igEndPopup() { ImGui::EndPopup(); }
PYLON_IMGUI_EXPORT int32_t igBeginPopup(const char* str_id, int32_t flags) { return ImGui::BeginPopup(str_id ? str_id : "", flags) ? 1 : 0; }
PYLON_IMGUI_EXPORT void    igCloseCurrentPopup() { ImGui::CloseCurrentPopup(); }
PYLON_IMGUI_EXPORT int32_t igBeginPopupContextItem(const char* str_id, int32_t flags) { return ImGui::BeginPopupContextItem(str_id ? str_id : "", flags) ? 1 : 0; }
PYLON_IMGUI_EXPORT void    igSetTooltip(const char* fmt) { ImGui::SetTooltip("%s", fmt ? fmt : ""); }

// ---------------------------------------------------------------------------
// ID / 样式栈
// ---------------------------------------------------------------------------

PYLON_IMGUI_EXPORT void    igPushID(int32_t id) { ImGui::PushID(id); }
PYLON_IMGUI_EXPORT void    igPushIDStr(const char* str_id) { ImGui::PushID(str_id ? str_id : ""); }
PYLON_IMGUI_EXPORT void    igPopID() { ImGui::PopID(); }
PYLON_IMGUI_EXPORT void    igSetNextItemWidth(float item_width) { ImGui::SetNextItemWidth(item_width); }
PYLON_IMGUI_EXPORT void    igPushItemWidth(float item_width) { ImGui::PushItemWidth(item_width); }
PYLON_IMGUI_EXPORT void    igPopItemWidth() { ImGui::PopItemWidth(); }
/// PushStyleVar 按变量类型分派：Alpha/DisabledAlpha/各 Rounding/BorderSize/Scrollbar*/Grab*/Tab*/IndentSpacing/
/// TreeLines*/SeparatorTextBorderSize 等是 float；WindowPadding/WindowMinSize/WindowTitleAlign/FramePadding/
/// ItemSpacing/ItemInnerSpacing/CellPadding/ButtonTextAlign/SelectableTextAlign 等是 ImVec2。
/// 混用类型会让 ImGui 断言崩（"Calling PushStyleVar() variant with wrong type!"）。
PYLON_IMGUI_EXPORT void    igPushStyleVar(int32_t idx, float v0, float v1)
{
    switch (idx)
    {
        // float 型
        case ImGuiStyleVar_Alpha:                   ImGui::PushStyleVar(ImGuiStyleVar_Alpha, v0); break;
        case ImGuiStyleVar_DisabledAlpha:           ImGui::PushStyleVar(ImGuiStyleVar_DisabledAlpha, v0); break;
        case ImGuiStyleVar_WindowRounding:          ImGui::PushStyleVar(ImGuiStyleVar_WindowRounding, v0); break;
        case ImGuiStyleVar_WindowBorderSize:        ImGui::PushStyleVar(ImGuiStyleVar_WindowBorderSize, v0); break;
        case ImGuiStyleVar_ChildRounding:           ImGui::PushStyleVar(ImGuiStyleVar_ChildRounding, v0); break;
        case ImGuiStyleVar_ChildBorderSize:         ImGui::PushStyleVar(ImGuiStyleVar_ChildBorderSize, v0); break;
        case ImGuiStyleVar_PopupRounding:           ImGui::PushStyleVar(ImGuiStyleVar_PopupRounding, v0); break;
        case ImGuiStyleVar_PopupBorderSize:         ImGui::PushStyleVar(ImGuiStyleVar_PopupBorderSize, v0); break;
        case ImGuiStyleVar_FrameRounding:           ImGui::PushStyleVar(ImGuiStyleVar_FrameRounding, v0); break;
        case ImGuiStyleVar_FrameBorderSize:         ImGui::PushStyleVar(ImGuiStyleVar_FrameBorderSize, v0); break;
        case ImGuiStyleVar_IndentSpacing:           ImGui::PushStyleVar(ImGuiStyleVar_IndentSpacing, v0); break;
        case ImGuiStyleVar_ScrollbarSize:           ImGui::PushStyleVar(ImGuiStyleVar_ScrollbarSize, v0); break;
        case ImGuiStyleVar_ScrollbarRounding:       ImGui::PushStyleVar(ImGuiStyleVar_ScrollbarRounding, v0); break;
        case ImGuiStyleVar_GrabMinSize:             ImGui::PushStyleVar(ImGuiStyleVar_GrabMinSize, v0); break;
        case ImGuiStyleVar_GrabRounding:            ImGui::PushStyleVar(ImGuiStyleVar_GrabRounding, v0); break;
        case ImGuiStyleVar_TabRounding:             ImGui::PushStyleVar(ImGuiStyleVar_TabRounding, v0); break;
        case ImGuiStyleVar_TabBorderSize:           ImGui::PushStyleVar(ImGuiStyleVar_TabBorderSize, v0); break;
        case ImGuiStyleVar_TabBarBorderSize:        ImGui::PushStyleVar(ImGuiStyleVar_TabBarBorderSize, v0); break;
        case ImGuiStyleVar_TabBarOverlineSize:      ImGui::PushStyleVar(ImGuiStyleVar_TabBarOverlineSize, v0); break;
        case ImGuiStyleVar_SeparatorTextBorderSize: ImGui::PushStyleVar(ImGuiStyleVar_SeparatorTextBorderSize, v0); break;
        default:
            // ImVec2 型（v0=x, v1=y）
            ImGui::PushStyleVar(static_cast<ImGuiStyleVar>(idx), ImVec2(v0, v1));
            break;
    }
}
PYLON_IMGUI_EXPORT void    igPopStyleVar(int32_t count) { ImGui::PopStyleVar(count); }
PYLON_IMGUI_EXPORT void    igPushStyleColor(int32_t idx, float r, float g, float b, float a) { ImGui::PushStyleColor(static_cast<ImGuiCol>(idx), ImVec4(r, g, b, a)); }
PYLON_IMGUI_EXPORT void    igPopStyleColor(int32_t count) { ImGui::PopStyleColor(count); }

// ---------------------------------------------------------------------------
// 禁用
// ---------------------------------------------------------------------------

PYLON_IMGUI_EXPORT void igBeginDisabled(int32_t disabled) { ImGui::BeginDisabled(disabled != 0); }
PYLON_IMGUI_EXPORT void igEndDisabled() { ImGui::EndDisabled(); }

// ---------------------------------------------------------------------------
// item 状态
// ---------------------------------------------------------------------------

PYLON_IMGUI_EXPORT int32_t igIsItemHovered(int32_t flags) { return ImGui::IsItemHovered(flags) ? 1 : 0; }
PYLON_IMGUI_EXPORT int32_t igIsItemActive() { return ImGui::IsItemActive() ? 1 : 0; }
PYLON_IMGUI_EXPORT int32_t igIsItemActivated() { return ImGui::IsItemActivated() ? 1 : 0; }
PYLON_IMGUI_EXPORT int32_t igIsItemDeactivated() { return ImGui::IsItemDeactivated() ? 1 : 0; }
PYLON_IMGUI_EXPORT int32_t igIsItemClicked(int32_t mouse_button) { return ImGui::IsItemClicked(mouse_button) ? 1 : 0; }
PYLON_IMGUI_EXPORT int32_t igIsItemFocused() { return ImGui::IsItemFocused() ? 1 : 0; }
PYLON_IMGUI_EXPORT int32_t igIsItemVisible() { return ImGui::IsItemVisible() ? 1 : 0; }
PYLON_IMGUI_EXPORT int32_t igIsItemEdited() { return ImGui::IsItemEdited() ? 1 : 0; }
PYLON_IMGUI_EXPORT int32_t igIsAnyItemActive() { return ImGui::IsAnyItemActive() ? 1 : 0; }
PYLON_IMGUI_EXPORT void    igGetItemRectMin(float* x, float* y) { const ImVec2 p = ImGui::GetItemRectMin(); if (x) *x = p.x; if (y) *y = p.y; }
PYLON_IMGUI_EXPORT void    igGetItemRectMax(float* x, float* y) { const ImVec2 p = ImGui::GetItemRectMax(); if (x) *x = p.x; if (y) *y = p.y; }

// ---------------------------------------------------------------------------
// 输入（触摸 → ImGui 鼠标事件；框架驱动调，坐标已做覆盖层换算）
// ---------------------------------------------------------------------------

PYLON_IMGUI_EXPORT void igAddMousePosEvent(float x, float y)
{
    ImGui::GetIO().AddMousePosEvent(x, y);
}
PYLON_IMGUI_EXPORT void igAddMouseButtonEvent(int32_t button, int32_t down)
{
    ImGui::GetIO().AddMouseButtonEvent(button, down != 0);
}
PYLON_IMGUI_EXPORT void igAddMouseWheelEvent(float wx, float wy)
{
    ImGui::GetIO().AddMouseWheelEvent(wx, wy);
}

PYLON_IMGUI_EXPORT void igAddInputCharactersUTF8(const char* utf8)
{
    if (utf8) ImGui::GetIO().AddInputCharactersUTF8(utf8);
}

PYLON_IMGUI_EXPORT void igAddKeyEvent(int32_t key, int32_t down)
{
    ImGui::GetIO().AddKeyEvent(static_cast<ImGuiKey>(key), down != 0);
}

PYLON_IMGUI_EXPORT int32_t igGetIO_WantTextInput()
{
    return ImGui::GetIO().WantTextInput ? 1 : 0;
}

PYLON_IMGUI_EXPORT int32_t igGetIO_WantCaptureMouse()
{
    return ImGui::GetIO().WantCaptureMouse ? 1 : 0;
}

/// 本帧鼠标滚轮 y（导航滚动等用；触摸滑动经驱动转成滚轮喂入）。
PYLON_IMGUI_EXPORT float igGetIO_MouseWheel()
{
    return ImGui::GetIO().MouseWheel;
}

/// 当前样式圆角（Chrome 用 kPlan*0.44 推窗圆角）。
PYLON_IMGUI_EXPORT float igGetStyleWindowRounding()
{
    return ImGui::GetStyle().WindowRounding;
}

PYLON_IMGUI_EXPORT int32_t igIsWindowMoving()
{
    ImGuiContext* ctx = ImGui::GetCurrentContext();
    return ctx && ctx->MovingWindow ? 1 : 0;
}

PYLON_IMGUI_EXPORT void igClearActiveID()
{
    ImGui::ClearActiveID();
}


PYLON_IMGUI_EXPORT void igSetKeyboardActive(int32_t active)
{
    using namespace pylon::imgui;
    const auto now = std::chrono::steady_clock::now().time_since_epoch().count();
    g_keyboardActiveUntil.store(now + 1500000000);   // +1.5s（纳秒）
}

// ---------------------------------------------------------------------------
// 样式（ModernTheme：C# 侧一次性 Apply，这里提供原语 setter）
// ---------------------------------------------------------------------------

/// 直接设置样式颜色（ImGuiCol 索引 → style.Colors[col]，越界/无上下文忽略）。
PYLON_IMGUI_EXPORT void igSetStyleColor(int32_t col, float r, float g, float b, float a)
{
    if (!ImGui::GetCurrentContext()) return;   // 上下文在第一次 BeginFrame 里才建，早期调用不崩
    ImGuiStyle& s = ImGui::GetStyle();
    if (col >= 0 && col < ImGuiCol_COUNT)
        s.Colors[col] = ImVec4(r, g, b, a);
}

/// 直接设置标量样式变量（ImGuiStyleVar 索引；Vec2 型走 igSetStyleVarVec2）。
PYLON_IMGUI_EXPORT void igSetStyleVarFloat(int32_t var, float v)
{
    if (!ImGui::GetCurrentContext()) return;
    ImGuiStyle& s = ImGui::GetStyle();
    switch (var)
    {
        case ImGuiStyleVar_Alpha:                   s.Alpha = v; break;
        case ImGuiStyleVar_DisabledAlpha:           s.DisabledAlpha = v; break;
        case ImGuiStyleVar_WindowRounding:          s.WindowRounding = v; break;
        case ImGuiStyleVar_WindowBorderSize:        s.WindowBorderSize = v; break;
        case ImGuiStyleVar_ChildRounding:           s.ChildRounding = v; break;
        case ImGuiStyleVar_ChildBorderSize:         s.ChildBorderSize = v; break;
        case ImGuiStyleVar_PopupRounding:           s.PopupRounding = v; break;
        case ImGuiStyleVar_PopupBorderSize:         s.PopupBorderSize = v; break;
        case ImGuiStyleVar_FrameRounding:           s.FrameRounding = v; break;
        case ImGuiStyleVar_FrameBorderSize:         s.FrameBorderSize = v; break;
        case ImGuiStyleVar_IndentSpacing:           s.IndentSpacing = v; break;
        case ImGuiStyleVar_ScrollbarSize:           s.ScrollbarSize = v; break;
        case ImGuiStyleVar_ScrollbarRounding:       s.ScrollbarRounding = v; break;
        case ImGuiStyleVar_GrabMinSize:             s.GrabMinSize = v; break;
        case ImGuiStyleVar_GrabRounding:            s.GrabRounding = v; break;
        case ImGuiStyleVar_TabRounding:             s.TabRounding = v; break;
        case ImGuiStyleVar_TabBorderSize:           s.TabBorderSize = v; break;
        case ImGuiStyleVar_TabBarBorderSize:        s.TabBarBorderSize = v; break;
        case ImGuiStyleVar_TabBarOverlineSize:      s.TabBarOverlineSize = v; break;
        case ImGuiStyleVar_SeparatorTextBorderSize: s.SeparatorTextBorderSize = v; break;
        default: break;
    }
}

/// 直接设置 ImVec2 型样式变量。
PYLON_IMGUI_EXPORT void igSetStyleVarVec2(int32_t var, float x, float y)
{
    if (!ImGui::GetCurrentContext()) return;
    ImGuiStyle& s = ImGui::GetStyle();
    switch (var)
    {
        case ImGuiStyleVar_WindowPadding:      s.WindowPadding = ImVec2(x, y); break;
        case ImGuiStyleVar_WindowMinSize:      s.WindowMinSize = ImVec2(x, y); break;
        case ImGuiStyleVar_WindowTitleAlign:   s.WindowTitleAlign = ImVec2(x, y); break;
        case ImGuiStyleVar_FramePadding:       s.FramePadding = ImVec2(x, y); break;
        case ImGuiStyleVar_ItemSpacing:        s.ItemSpacing = ImVec2(x, y); break;
        case ImGuiStyleVar_ItemInnerSpacing:   s.ItemInnerSpacing = ImVec2(x, y); break;
        case ImGuiStyleVar_CellPadding:        s.CellPadding = ImVec2(x, y); break;
        case ImGuiStyleVar_ButtonTextAlign:    s.ButtonTextAlign = ImVec2(x, y); break;
        case ImGuiStyleVar_SelectableTextAlign: s.SelectableTextAlign = ImVec2(x, y); break;
        default: break;
    }
}

// ---------------------------------------------------------------------------
// 绘制原语（DrawList）：C# 移植 GlassKit 动画/控件用（ImGui.NET 同款分层：
// GetDrawList 拿指针，Add* 系列以指针为第一参数；颜色一律 ImU32 = RGBA）
// ---------------------------------------------------------------------------

/// 当前窗口的绘制列表（窗口体内的自绘内容用；返回 ImDrawList*，C# 侧持 IntPtr）。
PYLON_IMGUI_EXPORT void* igGetWindowDrawList()
{
    return ImGui::GetWindowDrawList();
}
/// 前景绘制列表（悬浮球/覆盖效果用，盖在窗口内容之上）。
PYLON_IMGUI_EXPORT void* igGetForegroundDrawList()
{
    return ImGui::GetForegroundDrawList();
}
/// 全局时钟（秒；动画缓动用）。
PYLON_IMGUI_EXPORT float igGetTime()
{
    return ImGui::GetTime();
}
/// 当前字体大小。
PYLON_IMGUI_EXPORT float igGetFontSize()
{
    return ImGui::GetFontSize();
}
/// 鼠标位置（覆盖层坐标；自绘控件跟随用）。
PYLON_IMGUI_EXPORT void igGetMousePos(float* x, float* y)
{
    const ImVec2 p = ImGui::GetMousePos();
    if (x) *x = p.x;
    if (y) *y = p.y;
}

PYLON_IMGUI_EXPORT void igDL_AddRectFilled(void* dl, float x0, float y0, float x1, float y1,
                                           uint32_t col, float rounding)
{
    ((ImDrawList*)dl)->AddRectFilled(ImVec2(x0, y0), ImVec2(x1, y1), col, rounding);
}
PYLON_IMGUI_EXPORT void igDL_AddRectFilledMultiColor(void* dl, float x0, float y0, float x1, float y1,
                                                    uint32_t col_ul, uint32_t col_ur, uint32_t col_lr, uint32_t col_ll)
{
    ((ImDrawList*)dl)->AddRectFilledMultiColor(ImVec2(x0, y0), ImVec2(x1, y1), col_ul, col_ur, col_lr, col_ll);
}
PYLON_IMGUI_EXPORT void igDL_AddRect(void* dl, float x0, float y0, float x1, float y1,
                                     uint32_t col, float rounding, float thickness)
{
    ((ImDrawList*)dl)->AddRect(ImVec2(x0, y0), ImVec2(x1, y1), col, rounding, 0, thickness);
}
PYLON_IMGUI_EXPORT void igDL_AddLine(void* dl, float x0, float y0, float x1, float y1,
                                     uint32_t col, float thickness)
{
    ((ImDrawList*)dl)->AddLine(ImVec2(x0, y0), ImVec2(x1, y1), col, thickness);
}
PYLON_IMGUI_EXPORT void igDL_AddCircleFilled(void* dl, float cx, float cy, float r,
                                             uint32_t col, int32_t segments)
{
    ((ImDrawList*)dl)->AddCircleFilled(ImVec2(cx, cy), r, col, segments);
}
PYLON_IMGUI_EXPORT void igDL_AddCircle(void* dl, float cx, float cy, float r,
                                       uint32_t col, int32_t segments, float thickness)
{
    ((ImDrawList*)dl)->AddCircle(ImVec2(cx, cy), r, col, segments, thickness);
}
PYLON_IMGUI_EXPORT void igDL_AddEllipseFilled(void* dl, float cx, float cy, float rx, float ry,
                                              uint32_t col, float rotation, int32_t segments)
{
    ((ImDrawList*)dl)->AddEllipseFilled(ImVec2(cx, cy), ImVec2(rx, ry), col, rotation, segments);
}
PYLON_IMGUI_EXPORT void igDL_AddText(void* dl, float x, float y, uint32_t col, const char* utf8)
{
    ((ImDrawList*)dl)->AddText(ImVec2(x, y), col, utf8 ? utf8 : "");
}
PYLON_IMGUI_EXPORT void igDL_AddTriangleFilled(void* dl, float x0, float y0, float x1, float y1,
                                               float x2, float y2, uint32_t col)
{
    ((ImDrawList*)dl)->AddTriangleFilled(ImVec2(x0, y0), ImVec2(x1, y1), ImVec2(x2, y2), col);
}
PYLON_IMGUI_EXPORT void igDL_AddPolyline(void* dl, const float* xy, int32_t count,
                                         uint32_t col, float thickness)
{
    // xy 为连续的 [x0,y0, x1,y1, ...]；ImDrawList 收 ImVec2 数组，逐对复制（最多 64 点够用）。
    ImVec2 pts[64];
    const int n = count > 64 ? 64 : count;
    for (int i = 0; i < n; i++) { pts[i] = ImVec2(xy[i * 2], xy[i * 2 + 1]); }
    ((ImDrawList*)dl)->AddPolyline(pts, n, col, 0, thickness);
}

/// 顶部圆角 + 垂直渐变的矩形（极光顶部光带用）。
/// 不用 ImDrawList 内部 Path/Prim API，分段画 + 顶部两角圆覆盖，视觉等价且安全。
PYLON_IMGUI_EXPORT void igDL_AddRoundedGradientRectTop(void* dl, float x0, float y0,
                                                       float x1, float y1, float rounding,
                                                       uint32_t col_top, uint32_t col_bot)
{
    ImDrawList* d = (ImDrawList*)dl;
    const int kSegs = 12;
    // 把 top 的 alpha 线性渐变到 bot（RGB 用 top 的，只改 alpha）
    const int aTop = (int)((col_top >> 24) & 0xFF);
    const int aBot = (int)((col_bot >> 24) & 0xFF);
    const uint32_t rgb = col_top & 0x00FFFFFFu;
    for (int i = 0; i < kSegs; i++)
    {
        const float t0 = (float)i / kSegs, t1 = (float)(i + 1) / kSegs;
        const float yy0 = y0 + (y1 - y0) * t0;
        const float yy1 = y0 + (y1 - y0) * t1;
        const int a0 = (int)((float)aTop + (float)(aBot - aTop) * t0);
        const int a1 = (int)((float)aTop + (float)(aBot - aTop) * t1);
        // 每段上下两半用各自 alpha，近似平滑渐变（12 段肉眼不可见跳变）
        d->AddRectFilled(ImVec2(x0, yy0), ImVec2(x1, (yy0 + yy1) * 0.5f),
                         rgb | ((uint32_t)a0 << 24), 0.0f);
        d->AddRectFilled(ImVec2(x0, (yy0 + yy1) * 0.5f), ImVec2(x1, yy1),
                         rgb | ((uint32_t)a1 << 24), 0.0f);
    }
    // 顶部两角用同色圆覆盖成圆角（半径 = rounding）
    if (rounding > 0.5f)
    {
        d->AddCircleFilled(ImVec2(x0 + rounding, y0 + rounding), rounding, col_top, 0);
        d->AddCircleFilled(ImVec2(x1 - rounding, y0 + rounding), rounding, col_top, 0);
        d->AddRectFilled(ImVec2(x0 + rounding, y0), ImVec2(x1 - rounding, y0 + rounding), col_top, 0.0f);
    }
}
PYLON_IMGUI_EXPORT void igDL_PushClipRect(void* dl, float x0, float y0, float x1, float y1, int32_t intersect)
{
    ((ImDrawList*)dl)->PushClipRect(ImVec2(x0, y0), ImVec2(x1, y1), intersect != 0);
}
PYLON_IMGUI_EXPORT void igDL_PopClipRect(void* dl)
{
    ((ImDrawList*)dl)->PopClipRect();
}

/// 直接把 RGBA(0..1) 打包成 ImU32（C# 侧颜色构造用，省得管字节序）。
PYLON_IMGUI_EXPORT uint32_t igColor4(float r, float g, float b, float a)
{
    return ImGui::ColorConvertFloat4ToU32(ImVec4(r, g, b, a));
}

// ---------------------------------------------------------------------------
// 用户纹理（Mod 上传图片给 ImGui 用；由 VulkanTexture 管理，返回 ImTextureID）
// ---------------------------------------------------------------------------

/// 上传一张 RGBA 纹理（字节序 R,G,B,A，每像素 4 字节）→ ImTextureID（VkDescriptorSet）。
/// 失败返回 0。纹理生命周期由调用方负责：不用时 igDestroyTexture。
PYLON_IMGUI_EXPORT uint64_t igCreateTexture(const uint8_t* rgba, int32_t width, int32_t height)
{
    // Vulkan 上下文未就绪（首帧前 / 键盘键盘重建窗口）时创建纹理 = 空指针解引用崩溃。
    // 必须等 VulkanTexture 的 context 就绪才可创建。
    if (!rgba || width <= 0 || height <= 0 || !VulkanTexture::IsReady()) return 0;
    return static_cast<uint64_t>(VulkanTexture::CreateTexture(rgba, width, height));
}

/// 销毁上传的纹理（传 0 安全）。
PYLON_IMGUI_EXPORT void igDestroyTexture(uint64_t id)
{
    VulkanTexture::DestroyTexture(static_cast<ImTextureID>(id));
}

/// Image 控件：在光标处画出纹理（size 为显示尺寸；uv0/uv1 取纹理局部区域，默认整张）。
PYLON_IMGUI_EXPORT void igImage(uint64_t id, float size_x, float size_y,
                                float uv0x, float uv0y, float uv1x, float uv1y)
{
    if (id == 0 || !VulkanTexture::IsReady()) return;
    ImGui::Image(static_cast<ImTextureID>(id), ImVec2(size_x, size_y),
                 ImVec2(uv0x, uv0y), ImVec2(uv1x, uv1y));
}

/// DrawList 直接画纹理到矩形（pmin/pmax 屏幕坐标；tint 调色，白=原样）。
PYLON_IMGUI_EXPORT void igDL_AddImage(void* dl, uint64_t id,
                                      float x0, float y0, float x1, float y1, uint32_t tint)
{
    if (!dl || id == 0 || !VulkanTexture::IsReady()) return;
    ((ImDrawList*)dl)->AddImage(static_cast<ImTextureID>(id),
                                ImVec2(x0, y0), ImVec2(x1, y1),
                                ImVec2(0, 0), ImVec2(1, 1), tint);
}

// ---------------------------------------------------------------------------
// 工具
// ---------------------------------------------------------------------------

PYLON_IMGUI_EXPORT void    igCalcTextSize(const char* text, float wrap_width, float* out_w, float* out_h)
{
    const ImVec2 s = ImGui::CalcTextSize(text ? text : "", nullptr, false, wrap_width);
    if (out_w) *out_w = s.x;
    if (out_h) *out_h = s.y;
}
PYLON_IMGUI_EXPORT uint32_t igGetColorU32(int32_t idx) { return ImGui::GetColorU32(static_cast<ImGuiCol>(idx)); }
PYLON_IMGUI_EXPORT uint32_t igGetID(const char* str_id) { return ImGui::GetID(str_id ? str_id : ""); }