// GENERATED FILE — do not edit. See tools/imgui-gen/
#include "imgui.h"
#include "imgui_internal.h"
#include "Graphics/VulkanTexture.h"
#include <cstdint>
#include <cstdarg>
#include <cstring>

namespace pylon::imgui {
#define PYLON_IMGUI_EXPORT extern "C" __attribute__((visibility("default")))

// ===================== free functions =====================
PYLON_IMGUI_EXPORT ImGuiContext * igCreateContext(ImFontAtlas * shared_font_atlas) { return ImGui::CreateContext(shared_font_atlas); }
PYLON_IMGUI_EXPORT void igDestroyContext(ImGuiContext * ctx) { ImGui::DestroyContext(ctx); }
PYLON_IMGUI_EXPORT ImGuiContext * igGetCurrentContext() { return ImGui::GetCurrentContext(); }
PYLON_IMGUI_EXPORT void igSetCurrentContext(ImGuiContext * ctx) { ImGui::SetCurrentContext(ctx); }
PYLON_IMGUI_EXPORT ImGuiIO* igGetIO() { return &ImGui::GetIO(); }
PYLON_IMGUI_EXPORT ImGuiPlatformIO* igGetPlatformIO() { return &ImGui::GetPlatformIO(); }
PYLON_IMGUI_EXPORT ImGuiStyle* igGetStyle() { return &ImGui::GetStyle(); }
PYLON_IMGUI_EXPORT void igNewFrame() { ImGui::NewFrame(); }
PYLON_IMGUI_EXPORT void igEndFrame() { ImGui::EndFrame(); }
PYLON_IMGUI_EXPORT void igRender() { ImGui::Render(); }
PYLON_IMGUI_EXPORT ImDrawData * igGetDrawData() { return ImGui::GetDrawData(); }
PYLON_IMGUI_EXPORT void igShowDemoWindow(bool * p_open) { ImGui::ShowDemoWindow(p_open); }
PYLON_IMGUI_EXPORT void igShowMetricsWindow(bool * p_open) { ImGui::ShowMetricsWindow(p_open); }
PYLON_IMGUI_EXPORT void igShowDebugLogWindow(bool * p_open) { ImGui::ShowDebugLogWindow(p_open); }
PYLON_IMGUI_EXPORT void igShowIDStackToolWindow(bool * p_open) { ImGui::ShowIDStackToolWindow(p_open); }
PYLON_IMGUI_EXPORT void igShowAboutWindow(bool * p_open) { ImGui::ShowAboutWindow(p_open); }
PYLON_IMGUI_EXPORT void igShowStyleEditor(ImGuiStyle * ref) { ImGui::ShowStyleEditor(ref); }
PYLON_IMGUI_EXPORT bool igShowStyleSelector(const char * label) { return ImGui::ShowStyleSelector(label); }
PYLON_IMGUI_EXPORT void igShowFontSelector(const char * label) { ImGui::ShowFontSelector(label); }
PYLON_IMGUI_EXPORT void igShowUserGuide() { ImGui::ShowUserGuide(); }
PYLON_IMGUI_EXPORT const char * igGetVersion() { return ImGui::GetVersion(); }
PYLON_IMGUI_EXPORT void igStyleColorsDark(ImGuiStyle * dst) { ImGui::StyleColorsDark(dst); }
PYLON_IMGUI_EXPORT void igStyleColorsLight(ImGuiStyle * dst) { ImGui::StyleColorsLight(dst); }
PYLON_IMGUI_EXPORT void igStyleColorsClassic(ImGuiStyle * dst) { ImGui::StyleColorsClassic(dst); }
PYLON_IMGUI_EXPORT bool igBegin(const char * name, bool * p_open, ImGuiWindowFlags flags) { return ImGui::Begin(name, p_open, flags); }
PYLON_IMGUI_EXPORT void igEnd() { ImGui::End(); }
PYLON_IMGUI_EXPORT bool igBeginChild(const char * str_id, const ImVec2 size, ImGuiChildFlags child_flags, ImGuiWindowFlags window_flags) { return ImGui::BeginChild(str_id, size, child_flags, window_flags); }
PYLON_IMGUI_EXPORT void igEndChild() { ImGui::EndChild(); }
PYLON_IMGUI_EXPORT bool igIsWindowAppearing() { return ImGui::IsWindowAppearing(); }
PYLON_IMGUI_EXPORT bool igIsWindowCollapsed() { return ImGui::IsWindowCollapsed(); }
PYLON_IMGUI_EXPORT bool igIsWindowFocused(ImGuiFocusedFlags flags) { return ImGui::IsWindowFocused(flags); }
PYLON_IMGUI_EXPORT bool igIsWindowHovered(ImGuiHoveredFlags flags) { return ImGui::IsWindowHovered(flags); }
PYLON_IMGUI_EXPORT ImDrawList * igGetWindowDrawList() { return ImGui::GetWindowDrawList(); }
PYLON_IMGUI_EXPORT void igGetWindowPos(ImVec2* pOut) { *pOut = ImGui::GetWindowPos(); }
PYLON_IMGUI_EXPORT void igGetWindowSize(ImVec2* pOut) { *pOut = ImGui::GetWindowSize(); }
PYLON_IMGUI_EXPORT float igGetWindowWidth() { return ImGui::GetWindowWidth(); }
PYLON_IMGUI_EXPORT float igGetWindowHeight() { return ImGui::GetWindowHeight(); }
PYLON_IMGUI_EXPORT void igSetNextWindowPos(const ImVec2 pos, ImGuiCond cond, const ImVec2 pivot) { ImGui::SetNextWindowPos(pos, cond, pivot); }
PYLON_IMGUI_EXPORT void igSetNextWindowSize(const ImVec2 size, ImGuiCond cond) { ImGui::SetNextWindowSize(size, cond); }
PYLON_IMGUI_EXPORT void igSetNextWindowSizeConstraints(const ImVec2 size_min, const ImVec2 size_max, ImGuiSizeCallback custom_callback, void * custom_callback_data) { ImGui::SetNextWindowSizeConstraints(size_min, size_max, custom_callback, custom_callback_data); }
PYLON_IMGUI_EXPORT void igSetNextWindowContentSize(const ImVec2 size) { ImGui::SetNextWindowContentSize(size); }
PYLON_IMGUI_EXPORT void igSetNextWindowCollapsed(bool collapsed, ImGuiCond cond) { ImGui::SetNextWindowCollapsed(collapsed, cond); }
PYLON_IMGUI_EXPORT void igSetNextWindowFocus() { ImGui::SetNextWindowFocus(); }
PYLON_IMGUI_EXPORT void igSetNextWindowScroll(const ImVec2 scroll) { ImGui::SetNextWindowScroll(scroll); }
PYLON_IMGUI_EXPORT void igSetNextWindowBgAlpha(float alpha) { ImGui::SetNextWindowBgAlpha(alpha); }
PYLON_IMGUI_EXPORT void igSetWindowPos(const ImVec2 pos, ImGuiCond cond) { ImGui::SetWindowPos(pos, cond); }
PYLON_IMGUI_EXPORT void igSetWindowSize(const ImVec2 size, ImGuiCond cond) { ImGui::SetWindowSize(size, cond); }
PYLON_IMGUI_EXPORT void igSetWindowCollapsed(bool collapsed, ImGuiCond cond) { ImGui::SetWindowCollapsed(collapsed, cond); }
PYLON_IMGUI_EXPORT void igSetWindowFocus() { ImGui::SetWindowFocus(); }
PYLON_IMGUI_EXPORT float igGetScrollX() { return ImGui::GetScrollX(); }
PYLON_IMGUI_EXPORT float igGetScrollY() { return ImGui::GetScrollY(); }
PYLON_IMGUI_EXPORT void igSetScrollX(float scroll_x) { ImGui::SetScrollX(scroll_x); }
PYLON_IMGUI_EXPORT void igSetScrollY(float scroll_y) { ImGui::SetScrollY(scroll_y); }
PYLON_IMGUI_EXPORT float igGetScrollMaxX() { return ImGui::GetScrollMaxX(); }
PYLON_IMGUI_EXPORT float igGetScrollMaxY() { return ImGui::GetScrollMaxY(); }
PYLON_IMGUI_EXPORT void igSetScrollHereX(float center_x_ratio) { ImGui::SetScrollHereX(center_x_ratio); }
PYLON_IMGUI_EXPORT void igSetScrollHereY(float center_y_ratio) { ImGui::SetScrollHereY(center_y_ratio); }
PYLON_IMGUI_EXPORT void igSetScrollFromPosX(float local_x, float center_x_ratio) { ImGui::SetScrollFromPosX(local_x, center_x_ratio); }
PYLON_IMGUI_EXPORT void igSetScrollFromPosY(float local_y, float center_y_ratio) { ImGui::SetScrollFromPosY(local_y, center_y_ratio); }
PYLON_IMGUI_EXPORT void igPushFont(ImFont * font, float font_size_base_unscaled) { ImGui::PushFont(font, font_size_base_unscaled); }
PYLON_IMGUI_EXPORT void igPopFont() { ImGui::PopFont(); }
PYLON_IMGUI_EXPORT ImFont * igGetFont() { return ImGui::GetFont(); }
PYLON_IMGUI_EXPORT float igGetFontSize() { return ImGui::GetFontSize(); }
PYLON_IMGUI_EXPORT ImFontBaked * igGetFontBaked() { return ImGui::GetFontBaked(); }
PYLON_IMGUI_EXPORT void igPushStyleColor(ImGuiCol idx, ImU32 col) { ImGui::PushStyleColor(idx, col); }
PYLON_IMGUI_EXPORT void igPopStyleColor(int count) { ImGui::PopStyleColor(count); }
PYLON_IMGUI_EXPORT void igPushStyleVar(ImGuiStyleVar idx, float val) { ImGui::PushStyleVar(idx, val); }
PYLON_IMGUI_EXPORT void igPushStyleVarX(ImGuiStyleVar idx, float val_x) { ImGui::PushStyleVarX(idx, val_x); }
PYLON_IMGUI_EXPORT void igPushStyleVarY(ImGuiStyleVar idx, float val_y) { ImGui::PushStyleVarY(idx, val_y); }
PYLON_IMGUI_EXPORT void igPopStyleVar(int count) { ImGui::PopStyleVar(count); }
PYLON_IMGUI_EXPORT void igPushItemFlag(ImGuiItemFlags option, bool enabled) { ImGui::PushItemFlag(option, enabled); }
PYLON_IMGUI_EXPORT void igPopItemFlag() { ImGui::PopItemFlag(); }
PYLON_IMGUI_EXPORT void igPushItemWidth(float item_width) { ImGui::PushItemWidth(item_width); }
PYLON_IMGUI_EXPORT void igPopItemWidth() { ImGui::PopItemWidth(); }
PYLON_IMGUI_EXPORT void igSetNextItemWidth(float item_width) { ImGui::SetNextItemWidth(item_width); }
PYLON_IMGUI_EXPORT float igCalcItemWidth() { return ImGui::CalcItemWidth(); }
PYLON_IMGUI_EXPORT void igPushTextWrapPos(float wrap_local_pos_x) { ImGui::PushTextWrapPos(wrap_local_pos_x); }
PYLON_IMGUI_EXPORT void igPopTextWrapPos() { ImGui::PopTextWrapPos(); }
PYLON_IMGUI_EXPORT void igGetFontTexUvWhitePixel(ImVec2* pOut) { *pOut = ImGui::GetFontTexUvWhitePixel(); }
PYLON_IMGUI_EXPORT ImU32 igGetColorU32(ImGuiCol idx, float alpha_mul) { return ImGui::GetColorU32(idx, alpha_mul); }
PYLON_IMGUI_EXPORT const ImVec4* igGetStyleColorVec4(ImGuiCol idx) { return &ImGui::GetStyleColorVec4(idx); }
PYLON_IMGUI_EXPORT void igGetCursorScreenPos(ImVec2* pOut) { *pOut = ImGui::GetCursorScreenPos(); }
PYLON_IMGUI_EXPORT void igSetCursorScreenPos(const ImVec2 pos) { ImGui::SetCursorScreenPos(pos); }
PYLON_IMGUI_EXPORT void igGetContentRegionAvail(ImVec2* pOut) { *pOut = ImGui::GetContentRegionAvail(); }
PYLON_IMGUI_EXPORT void igGetCursorPos(ImVec2* pOut) { *pOut = ImGui::GetCursorPos(); }
PYLON_IMGUI_EXPORT float igGetCursorPosX() { return ImGui::GetCursorPosX(); }
PYLON_IMGUI_EXPORT float igGetCursorPosY() { return ImGui::GetCursorPosY(); }
PYLON_IMGUI_EXPORT void igSetCursorPos(const ImVec2 local_pos) { ImGui::SetCursorPos(local_pos); }
PYLON_IMGUI_EXPORT void igSetCursorPosX(float local_x) { ImGui::SetCursorPosX(local_x); }
PYLON_IMGUI_EXPORT void igSetCursorPosY(float local_y) { ImGui::SetCursorPosY(local_y); }
PYLON_IMGUI_EXPORT void igGetCursorStartPos(ImVec2* pOut) { *pOut = ImGui::GetCursorStartPos(); }
PYLON_IMGUI_EXPORT void igSeparator() { ImGui::Separator(); }
PYLON_IMGUI_EXPORT void igSameLine(float offset_from_start_x, float spacing) { ImGui::SameLine(offset_from_start_x, spacing); }
PYLON_IMGUI_EXPORT void igNewLine() { ImGui::NewLine(); }
PYLON_IMGUI_EXPORT void igSpacing() { ImGui::Spacing(); }
PYLON_IMGUI_EXPORT void igDummy(const ImVec2 size) { ImGui::Dummy(size); }
PYLON_IMGUI_EXPORT void igIndent(float indent_w) { ImGui::Indent(indent_w); }
PYLON_IMGUI_EXPORT void igUnindent(float indent_w) { ImGui::Unindent(indent_w); }
PYLON_IMGUI_EXPORT void igBeginGroup() { ImGui::BeginGroup(); }
PYLON_IMGUI_EXPORT void igEndGroup() { ImGui::EndGroup(); }
PYLON_IMGUI_EXPORT void igAlignTextToFramePadding() { ImGui::AlignTextToFramePadding(); }
PYLON_IMGUI_EXPORT float igGetTextLineHeight() { return ImGui::GetTextLineHeight(); }
PYLON_IMGUI_EXPORT float igGetTextLineHeightWithSpacing() { return ImGui::GetTextLineHeightWithSpacing(); }
PYLON_IMGUI_EXPORT float igGetFrameHeight() { return ImGui::GetFrameHeight(); }
PYLON_IMGUI_EXPORT float igGetFrameHeightWithSpacing() { return ImGui::GetFrameHeightWithSpacing(); }
PYLON_IMGUI_EXPORT void igPushID(const char * str_id) { ImGui::PushID(str_id); }
PYLON_IMGUI_EXPORT void igPopID() { ImGui::PopID(); }
PYLON_IMGUI_EXPORT ImGuiID igGetID(const char * str_id) { return ImGui::GetID(str_id); }
PYLON_IMGUI_EXPORT void igTextUnformatted(const char * text, const char * text_end) { ImGui::TextUnformatted(text, text_end); }
PYLON_IMGUI_EXPORT void igTextV(const char * fmt, va_list args) { ImGui::TextV(fmt, args); }
PYLON_IMGUI_EXPORT void igTextColoredV(const ImVec4 col, const char * fmt, va_list args) { ImGui::TextColoredV(col, fmt, args); }
PYLON_IMGUI_EXPORT void igTextDisabledV(const char * fmt, va_list args) { ImGui::TextDisabledV(fmt, args); }
PYLON_IMGUI_EXPORT void igTextWrappedV(const char * fmt, va_list args) { ImGui::TextWrappedV(fmt, args); }
PYLON_IMGUI_EXPORT void igLabelTextV(const char * label, const char * fmt, va_list args) { ImGui::LabelTextV(label, fmt, args); }
PYLON_IMGUI_EXPORT void igBulletTextV(const char * fmt, va_list args) { ImGui::BulletTextV(fmt, args); }
PYLON_IMGUI_EXPORT void igSeparatorText(const char * label) { ImGui::SeparatorText(label); }
PYLON_IMGUI_EXPORT bool igButton(const char * label, const ImVec2 size) { return ImGui::Button(label, size); }
PYLON_IMGUI_EXPORT bool igSmallButton(const char * label) { return ImGui::SmallButton(label); }
PYLON_IMGUI_EXPORT bool igInvisibleButton(const char * str_id, const ImVec2 size, ImGuiButtonFlags flags) { return ImGui::InvisibleButton(str_id, size, flags); }
PYLON_IMGUI_EXPORT bool igArrowButton(const char * str_id, ImGuiDir dir) { return ImGui::ArrowButton(str_id, dir); }
PYLON_IMGUI_EXPORT bool igCheckbox(const char * label, bool * v) { return ImGui::Checkbox(label, v); }
PYLON_IMGUI_EXPORT bool igCheckboxFlags(const char * label, int * flags, int flags_value) { return ImGui::CheckboxFlags(label, flags, flags_value); }
PYLON_IMGUI_EXPORT bool igRadioButton(const char * label, bool active) { return ImGui::RadioButton(label, active); }
PYLON_IMGUI_EXPORT void igProgressBar(float fraction, const ImVec2 size_arg, const char * overlay) { ImGui::ProgressBar(fraction, size_arg, overlay); }
PYLON_IMGUI_EXPORT void igBullet() { ImGui::Bullet(); }
PYLON_IMGUI_EXPORT bool igTextLink(const char * label) { return ImGui::TextLink(label); }
PYLON_IMGUI_EXPORT bool igTextLinkOpenURL(const char * label, const char * url) { return ImGui::TextLinkOpenURL(label, url); }
PYLON_IMGUI_EXPORT void igImage(ImTextureRef tex_ref, const ImVec2 image_size, const ImVec2 uv0, const ImVec2 uv1) { ImGui::Image(tex_ref, image_size, uv0, uv1); }
PYLON_IMGUI_EXPORT void igImageWithBg(ImTextureRef tex_ref, const ImVec2 image_size, const ImVec2 uv0, const ImVec2 uv1, const ImVec4 bg_col, const ImVec4 tint_col) { ImGui::ImageWithBg(tex_ref, image_size, uv0, uv1, bg_col, tint_col); }
PYLON_IMGUI_EXPORT bool igImageButton(const char * str_id, ImTextureRef tex_ref, const ImVec2 image_size, const ImVec2 uv0, const ImVec2 uv1, const ImVec4 bg_col, const ImVec4 tint_col) { return ImGui::ImageButton(str_id, tex_ref, image_size, uv0, uv1, bg_col, tint_col); }
PYLON_IMGUI_EXPORT bool igBeginCombo(const char * label, const char * preview_value, ImGuiComboFlags flags) { return ImGui::BeginCombo(label, preview_value, flags); }
PYLON_IMGUI_EXPORT void igEndCombo() { ImGui::EndCombo(); }
PYLON_IMGUI_EXPORT bool igCombo(const char * label, int * current_item, const char** items, int items_count, int popup_max_height_in_items) { return ImGui::Combo(label, current_item, items, items_count, popup_max_height_in_items); }
PYLON_IMGUI_EXPORT bool igDragFloat(const char * label, float * v, float v_speed, float v_min, float v_max, const char * format, ImGuiSliderFlags flags) { return ImGui::DragFloat(label, v, v_speed, v_min, v_max, format, flags); }
PYLON_IMGUI_EXPORT bool igDragFloat2(const char * label, float * v, float v_speed, float v_min, float v_max, const char * format, ImGuiSliderFlags flags) { return ImGui::DragFloat2(label, v, v_speed, v_min, v_max, format, flags); }
PYLON_IMGUI_EXPORT bool igDragFloat3(const char * label, float * v, float v_speed, float v_min, float v_max, const char * format, ImGuiSliderFlags flags) { return ImGui::DragFloat3(label, v, v_speed, v_min, v_max, format, flags); }
PYLON_IMGUI_EXPORT bool igDragFloat4(const char * label, float * v, float v_speed, float v_min, float v_max, const char * format, ImGuiSliderFlags flags) { return ImGui::DragFloat4(label, v, v_speed, v_min, v_max, format, flags); }
PYLON_IMGUI_EXPORT bool igDragFloatRange2(const char * label, float * v_current_min, float * v_current_max, float v_speed, float v_min, float v_max, const char * format, const char * format_max, ImGuiSliderFlags flags) { return ImGui::DragFloatRange2(label, v_current_min, v_current_max, v_speed, v_min, v_max, format, format_max, flags); }
PYLON_IMGUI_EXPORT bool igDragInt(const char * label, int * v, float v_speed, int v_min, int v_max, const char * format, ImGuiSliderFlags flags) { return ImGui::DragInt(label, v, v_speed, v_min, v_max, format, flags); }
PYLON_IMGUI_EXPORT bool igDragInt2(const char * label, int * v, float v_speed, int v_min, int v_max, const char * format, ImGuiSliderFlags flags) { return ImGui::DragInt2(label, v, v_speed, v_min, v_max, format, flags); }
PYLON_IMGUI_EXPORT bool igDragInt3(const char * label, int * v, float v_speed, int v_min, int v_max, const char * format, ImGuiSliderFlags flags) { return ImGui::DragInt3(label, v, v_speed, v_min, v_max, format, flags); }
PYLON_IMGUI_EXPORT bool igDragInt4(const char * label, int * v, float v_speed, int v_min, int v_max, const char * format, ImGuiSliderFlags flags) { return ImGui::DragInt4(label, v, v_speed, v_min, v_max, format, flags); }
PYLON_IMGUI_EXPORT bool igDragIntRange2(const char * label, int * v_current_min, int * v_current_max, float v_speed, int v_min, int v_max, const char * format, const char * format_max, ImGuiSliderFlags flags) { return ImGui::DragIntRange2(label, v_current_min, v_current_max, v_speed, v_min, v_max, format, format_max, flags); }
PYLON_IMGUI_EXPORT bool igDragScalar(const char * label, ImGuiDataType data_type, void * p_data, float v_speed, const void * p_min, const void * p_max, const char * format, ImGuiSliderFlags flags) { return ImGui::DragScalar(label, data_type, p_data, v_speed, p_min, p_max, format, flags); }
PYLON_IMGUI_EXPORT bool igDragScalarN(const char * label, ImGuiDataType data_type, void * p_data, int components, float v_speed, const void * p_min, const void * p_max, const char * format, ImGuiSliderFlags flags) { return ImGui::DragScalarN(label, data_type, p_data, components, v_speed, p_min, p_max, format, flags); }
PYLON_IMGUI_EXPORT bool igSliderFloat(const char * label, float * v, float v_min, float v_max, const char * format, ImGuiSliderFlags flags) { return ImGui::SliderFloat(label, v, v_min, v_max, format, flags); }
PYLON_IMGUI_EXPORT bool igSliderFloat2(const char * label, float * v, float v_min, float v_max, const char * format, ImGuiSliderFlags flags) { return ImGui::SliderFloat2(label, v, v_min, v_max, format, flags); }
PYLON_IMGUI_EXPORT bool igSliderFloat3(const char * label, float * v, float v_min, float v_max, const char * format, ImGuiSliderFlags flags) { return ImGui::SliderFloat3(label, v, v_min, v_max, format, flags); }
PYLON_IMGUI_EXPORT bool igSliderFloat4(const char * label, float * v, float v_min, float v_max, const char * format, ImGuiSliderFlags flags) { return ImGui::SliderFloat4(label, v, v_min, v_max, format, flags); }
PYLON_IMGUI_EXPORT bool igSliderAngle(const char * label, float * v_rad, float v_degrees_min, float v_degrees_max, const char * format, ImGuiSliderFlags flags) { return ImGui::SliderAngle(label, v_rad, v_degrees_min, v_degrees_max, format, flags); }
PYLON_IMGUI_EXPORT bool igSliderInt(const char * label, int * v, int v_min, int v_max, const char * format, ImGuiSliderFlags flags) { return ImGui::SliderInt(label, v, v_min, v_max, format, flags); }
PYLON_IMGUI_EXPORT bool igSliderInt2(const char * label, int * v, int v_min, int v_max, const char * format, ImGuiSliderFlags flags) { return ImGui::SliderInt2(label, v, v_min, v_max, format, flags); }
PYLON_IMGUI_EXPORT bool igSliderInt3(const char * label, int * v, int v_min, int v_max, const char * format, ImGuiSliderFlags flags) { return ImGui::SliderInt3(label, v, v_min, v_max, format, flags); }
PYLON_IMGUI_EXPORT bool igSliderInt4(const char * label, int * v, int v_min, int v_max, const char * format, ImGuiSliderFlags flags) { return ImGui::SliderInt4(label, v, v_min, v_max, format, flags); }
PYLON_IMGUI_EXPORT bool igSliderScalar(const char * label, ImGuiDataType data_type, void * p_data, const void * p_min, const void * p_max, const char * format, ImGuiSliderFlags flags) { return ImGui::SliderScalar(label, data_type, p_data, p_min, p_max, format, flags); }
PYLON_IMGUI_EXPORT bool igSliderScalarN(const char * label, ImGuiDataType data_type, void * p_data, int components, const void * p_min, const void * p_max, const char * format, ImGuiSliderFlags flags) { return ImGui::SliderScalarN(label, data_type, p_data, components, p_min, p_max, format, flags); }
PYLON_IMGUI_EXPORT bool igVSliderFloat(const char * label, const ImVec2 size, float * v, float v_min, float v_max, const char * format, ImGuiSliderFlags flags) { return ImGui::VSliderFloat(label, size, v, v_min, v_max, format, flags); }
PYLON_IMGUI_EXPORT bool igVSliderInt(const char * label, const ImVec2 size, int * v, int v_min, int v_max, const char * format, ImGuiSliderFlags flags) { return ImGui::VSliderInt(label, size, v, v_min, v_max, format, flags); }
PYLON_IMGUI_EXPORT bool igVSliderScalar(const char * label, const ImVec2 size, ImGuiDataType data_type, void * p_data, const void * p_min, const void * p_max, const char * format, ImGuiSliderFlags flags) { return ImGui::VSliderScalar(label, size, data_type, p_data, p_min, p_max, format, flags); }
PYLON_IMGUI_EXPORT bool igInputText(const char * label, char * buf, size_t buf_size, ImGuiInputTextFlags flags, ImGuiInputTextCallback callback, void * user_data) { return ImGui::InputText(label, buf, buf_size, flags, callback, user_data); }
PYLON_IMGUI_EXPORT bool igInputTextMultiline(const char * label, char * buf, size_t buf_size, const ImVec2 size, ImGuiInputTextFlags flags, ImGuiInputTextCallback callback, void * user_data) { return ImGui::InputTextMultiline(label, buf, buf_size, size, flags, callback, user_data); }
PYLON_IMGUI_EXPORT bool igInputTextWithHint(const char * label, const char * hint, char * buf, size_t buf_size, ImGuiInputTextFlags flags, ImGuiInputTextCallback callback, void * user_data) { return ImGui::InputTextWithHint(label, hint, buf, buf_size, flags, callback, user_data); }
PYLON_IMGUI_EXPORT bool igInputFloat(const char * label, float * v, float step, float step_fast, const char * format, ImGuiInputTextFlags flags) { return ImGui::InputFloat(label, v, step, step_fast, format, flags); }
PYLON_IMGUI_EXPORT bool igInputFloat2(const char * label, float * v, const char * format, ImGuiInputTextFlags flags) { return ImGui::InputFloat2(label, v, format, flags); }
PYLON_IMGUI_EXPORT bool igInputFloat3(const char * label, float * v, const char * format, ImGuiInputTextFlags flags) { return ImGui::InputFloat3(label, v, format, flags); }
PYLON_IMGUI_EXPORT bool igInputFloat4(const char * label, float * v, const char * format, ImGuiInputTextFlags flags) { return ImGui::InputFloat4(label, v, format, flags); }
PYLON_IMGUI_EXPORT bool igInputInt(const char * label, int * v, int step, int step_fast, ImGuiInputTextFlags flags) { return ImGui::InputInt(label, v, step, step_fast, flags); }
PYLON_IMGUI_EXPORT bool igInputInt2(const char * label, int * v, ImGuiInputTextFlags flags) { return ImGui::InputInt2(label, v, flags); }
PYLON_IMGUI_EXPORT bool igInputInt3(const char * label, int * v, ImGuiInputTextFlags flags) { return ImGui::InputInt3(label, v, flags); }
PYLON_IMGUI_EXPORT bool igInputInt4(const char * label, int * v, ImGuiInputTextFlags flags) { return ImGui::InputInt4(label, v, flags); }
PYLON_IMGUI_EXPORT bool igInputDouble(const char * label, double * v, double step, double step_fast, const char * format, ImGuiInputTextFlags flags) { return ImGui::InputDouble(label, v, step, step_fast, format, flags); }
PYLON_IMGUI_EXPORT bool igInputScalar(const char * label, ImGuiDataType data_type, void * p_data, const void * p_step, const void * p_step_fast, const char * format, ImGuiInputTextFlags flags) { return ImGui::InputScalar(label, data_type, p_data, p_step, p_step_fast, format, flags); }
PYLON_IMGUI_EXPORT bool igInputScalarN(const char * label, ImGuiDataType data_type, void * p_data, int components, const void * p_step, const void * p_step_fast, const char * format, ImGuiInputTextFlags flags) { return ImGui::InputScalarN(label, data_type, p_data, components, p_step, p_step_fast, format, flags); }
PYLON_IMGUI_EXPORT bool igColorEdit3(const char * label, float * col, ImGuiColorEditFlags flags) { return ImGui::ColorEdit3(label, col, flags); }
PYLON_IMGUI_EXPORT bool igColorEdit4(const char * label, float * col, ImGuiColorEditFlags flags) { return ImGui::ColorEdit4(label, col, flags); }
PYLON_IMGUI_EXPORT bool igColorPicker3(const char * label, float * col, ImGuiColorEditFlags flags) { return ImGui::ColorPicker3(label, col, flags); }
PYLON_IMGUI_EXPORT bool igColorPicker4(const char * label, float * col, ImGuiColorEditFlags flags, const float * ref_col) { return ImGui::ColorPicker4(label, col, flags, ref_col); }
PYLON_IMGUI_EXPORT bool igColorButton(const char * desc_id, const ImVec4 col, ImGuiColorEditFlags flags, const ImVec2 size) { return ImGui::ColorButton(desc_id, col, flags, size); }
PYLON_IMGUI_EXPORT void igSetColorEditOptions(ImGuiColorEditFlags flags) { ImGui::SetColorEditOptions(flags); }
PYLON_IMGUI_EXPORT bool igTreeNode(const char * label) { return ImGui::TreeNode(label); }
PYLON_IMGUI_EXPORT bool igTreeNodeV(const char * str_id, const char * fmt, va_list args) { return ImGui::TreeNodeV(str_id, fmt, args); }
PYLON_IMGUI_EXPORT bool igTreeNodeEx(const char * label, ImGuiTreeNodeFlags flags) { return ImGui::TreeNodeEx(label, flags); }
PYLON_IMGUI_EXPORT bool igTreeNodeExV(const char * str_id, ImGuiTreeNodeFlags flags, const char * fmt, va_list args) { return ImGui::TreeNodeExV(str_id, flags, fmt, args); }
PYLON_IMGUI_EXPORT void igTreePush(const char * str_id) { ImGui::TreePush(str_id); }
PYLON_IMGUI_EXPORT void igTreePop() { ImGui::TreePop(); }
PYLON_IMGUI_EXPORT float igGetTreeNodeToLabelSpacing() { return ImGui::GetTreeNodeToLabelSpacing(); }
PYLON_IMGUI_EXPORT bool igCollapsingHeader(const char * label, ImGuiTreeNodeFlags flags) { return ImGui::CollapsingHeader(label, flags); }
PYLON_IMGUI_EXPORT void igSetNextItemOpen(bool is_open, ImGuiCond cond) { ImGui::SetNextItemOpen(is_open, cond); }
PYLON_IMGUI_EXPORT void igSetNextItemStorageID(ImGuiID storage_id) { ImGui::SetNextItemStorageID(storage_id); }
PYLON_IMGUI_EXPORT bool igSelectable(const char * label, bool selected, ImGuiSelectableFlags flags, const ImVec2 size) { return ImGui::Selectable(label, selected, flags, size); }
PYLON_IMGUI_EXPORT ImGuiMultiSelectIO * igBeginMultiSelect(ImGuiMultiSelectFlags flags, int selection_size, int items_count) { return ImGui::BeginMultiSelect(flags, selection_size, items_count); }
PYLON_IMGUI_EXPORT ImGuiMultiSelectIO * igEndMultiSelect() { return ImGui::EndMultiSelect(); }
PYLON_IMGUI_EXPORT void igSetNextItemSelectionUserData(ImGuiSelectionUserData selection_user_data) { ImGui::SetNextItemSelectionUserData(selection_user_data); }
PYLON_IMGUI_EXPORT bool igIsItemToggledSelection() { return ImGui::IsItemToggledSelection(); }
PYLON_IMGUI_EXPORT bool igBeginListBox(const char * label, const ImVec2 size) { return ImGui::BeginListBox(label, size); }
PYLON_IMGUI_EXPORT void igEndListBox() { ImGui::EndListBox(); }
PYLON_IMGUI_EXPORT bool igListBox(const char * label, int * current_item, const char** items, int items_count, int height_in_items) { return ImGui::ListBox(label, current_item, items, items_count, height_in_items); }
PYLON_IMGUI_EXPORT void igPlotLines(const char * label, const float * values, int values_count, int values_offset, const char * overlay_text, float scale_min, float scale_max, ImVec2 graph_size, int stride) { ImGui::PlotLines(label, values, values_count, values_offset, overlay_text, scale_min, scale_max, graph_size, stride); }
PYLON_IMGUI_EXPORT void igPlotHistogram(const char * label, const float * values, int values_count, int values_offset, const char * overlay_text, float scale_min, float scale_max, ImVec2 graph_size, int stride) { ImGui::PlotHistogram(label, values, values_count, values_offset, overlay_text, scale_min, scale_max, graph_size, stride); }
PYLON_IMGUI_EXPORT void igValue(const char * prefix, bool b) { ImGui::Value(prefix, b); }
PYLON_IMGUI_EXPORT bool igBeginMenuBar() { return ImGui::BeginMenuBar(); }
PYLON_IMGUI_EXPORT void igEndMenuBar() { ImGui::EndMenuBar(); }
PYLON_IMGUI_EXPORT bool igBeginMainMenuBar() { return ImGui::BeginMainMenuBar(); }
PYLON_IMGUI_EXPORT void igEndMainMenuBar() { ImGui::EndMainMenuBar(); }
PYLON_IMGUI_EXPORT bool igBeginMenu(const char * label, bool enabled) { return ImGui::BeginMenu(label, enabled); }
PYLON_IMGUI_EXPORT void igEndMenu() { ImGui::EndMenu(); }
PYLON_IMGUI_EXPORT bool igMenuItem(const char * label, const char * shortcut, bool selected, bool enabled) { return ImGui::MenuItem(label, shortcut, selected, enabled); }
PYLON_IMGUI_EXPORT bool igBeginTooltip() { return ImGui::BeginTooltip(); }
PYLON_IMGUI_EXPORT void igEndTooltip() { ImGui::EndTooltip(); }
PYLON_IMGUI_EXPORT void igSetTooltipV(const char * fmt, va_list args) { ImGui::SetTooltipV(fmt, args); }
PYLON_IMGUI_EXPORT bool igBeginItemTooltip() { return ImGui::BeginItemTooltip(); }
PYLON_IMGUI_EXPORT void igSetItemTooltip(const char * fmt) { ImGui::SetItemTooltip(fmt); }
PYLON_IMGUI_EXPORT void igSetItemTooltipV(const char * fmt, va_list args) { ImGui::SetItemTooltipV(fmt, args); }
PYLON_IMGUI_EXPORT bool igBeginPopup(const char * str_id, ImGuiWindowFlags flags) { return ImGui::BeginPopup(str_id, flags); }
PYLON_IMGUI_EXPORT bool igBeginPopupModal(const char * name, bool * p_open, ImGuiWindowFlags flags) { return ImGui::BeginPopupModal(name, p_open, flags); }
PYLON_IMGUI_EXPORT void igEndPopup() { ImGui::EndPopup(); }
PYLON_IMGUI_EXPORT void igOpenPopup(const char * str_id, ImGuiPopupFlags popup_flags) { ImGui::OpenPopup(str_id, popup_flags); }
PYLON_IMGUI_EXPORT void igOpenPopupOnItemClick(const char * str_id, ImGuiPopupFlags popup_flags) { ImGui::OpenPopupOnItemClick(str_id, popup_flags); }
PYLON_IMGUI_EXPORT void igCloseCurrentPopup() { ImGui::CloseCurrentPopup(); }
PYLON_IMGUI_EXPORT bool igBeginPopupContextItem(const char * str_id, ImGuiPopupFlags popup_flags) { return ImGui::BeginPopupContextItem(str_id, popup_flags); }
PYLON_IMGUI_EXPORT bool igBeginPopupContextWindow(const char * str_id, ImGuiPopupFlags popup_flags) { return ImGui::BeginPopupContextWindow(str_id, popup_flags); }
PYLON_IMGUI_EXPORT bool igBeginPopupContextVoid(const char * str_id, ImGuiPopupFlags popup_flags) { return ImGui::BeginPopupContextVoid(str_id, popup_flags); }
PYLON_IMGUI_EXPORT bool igIsPopupOpen(const char * str_id, ImGuiPopupFlags flags) { return ImGui::IsPopupOpen(str_id, flags); }
PYLON_IMGUI_EXPORT bool igBeginTable(const char * str_id, int columns, ImGuiTableFlags flags, const ImVec2 outer_size, float inner_width) { return ImGui::BeginTable(str_id, columns, flags, outer_size, inner_width); }
PYLON_IMGUI_EXPORT void igEndTable() { ImGui::EndTable(); }
PYLON_IMGUI_EXPORT void igTableNextRow(ImGuiTableRowFlags row_flags, float min_row_height) { ImGui::TableNextRow(row_flags, min_row_height); }
PYLON_IMGUI_EXPORT bool igTableNextColumn() { return ImGui::TableNextColumn(); }
PYLON_IMGUI_EXPORT bool igTableSetColumnIndex(int column_n) { return ImGui::TableSetColumnIndex(column_n); }
PYLON_IMGUI_EXPORT void igTableSetupColumn(const char * label, ImGuiTableColumnFlags flags, float init_width_or_weight, ImGuiID user_id) { ImGui::TableSetupColumn(label, flags, init_width_or_weight, user_id); }
PYLON_IMGUI_EXPORT void igTableSetupScrollFreeze(int cols, int rows) { ImGui::TableSetupScrollFreeze(cols, rows); }
PYLON_IMGUI_EXPORT void igTableHeader(const char * label) { ImGui::TableHeader(label); }
PYLON_IMGUI_EXPORT void igTableHeadersRow() { ImGui::TableHeadersRow(); }
PYLON_IMGUI_EXPORT void igTableAngledHeadersRow() { ImGui::TableAngledHeadersRow(); }
PYLON_IMGUI_EXPORT ImGuiTableSortSpecs * igTableGetSortSpecs() { return ImGui::TableGetSortSpecs(); }
PYLON_IMGUI_EXPORT int igTableGetColumnCount() { return ImGui::TableGetColumnCount(); }
PYLON_IMGUI_EXPORT int igTableGetColumnIndex() { return ImGui::TableGetColumnIndex(); }
PYLON_IMGUI_EXPORT int igTableGetRowIndex() { return ImGui::TableGetRowIndex(); }
PYLON_IMGUI_EXPORT const char * igTableGetColumnName(int column_n) { return ImGui::TableGetColumnName(column_n); }
PYLON_IMGUI_EXPORT ImGuiTableColumnFlags igTableGetColumnFlags(int column_n) { return ImGui::TableGetColumnFlags(column_n); }
PYLON_IMGUI_EXPORT void igTableSetColumnEnabled(int column_n, bool v) { ImGui::TableSetColumnEnabled(column_n, v); }
PYLON_IMGUI_EXPORT int igTableGetHoveredColumn() { return ImGui::TableGetHoveredColumn(); }
PYLON_IMGUI_EXPORT void igTableSetBgColor(ImGuiTableBgTarget target, ImU32 color, int column_n) { ImGui::TableSetBgColor(target, color, column_n); }
PYLON_IMGUI_EXPORT void igColumns(int count, const char * id, bool borders) { ImGui::Columns(count, id, borders); }
PYLON_IMGUI_EXPORT void igNextColumn() { ImGui::NextColumn(); }
PYLON_IMGUI_EXPORT int igGetColumnIndex() { return ImGui::GetColumnIndex(); }
PYLON_IMGUI_EXPORT float igGetColumnWidth(int column_index) { return ImGui::GetColumnWidth(column_index); }
PYLON_IMGUI_EXPORT void igSetColumnWidth(int column_index, float width) { ImGui::SetColumnWidth(column_index, width); }
PYLON_IMGUI_EXPORT float igGetColumnOffset(int column_index) { return ImGui::GetColumnOffset(column_index); }
PYLON_IMGUI_EXPORT void igSetColumnOffset(int column_index, float offset_x) { ImGui::SetColumnOffset(column_index, offset_x); }
PYLON_IMGUI_EXPORT int igGetColumnsCount() { return ImGui::GetColumnsCount(); }
PYLON_IMGUI_EXPORT bool igBeginTabBar(const char * str_id, ImGuiTabBarFlags flags) { return ImGui::BeginTabBar(str_id, flags); }
PYLON_IMGUI_EXPORT void igEndTabBar() { ImGui::EndTabBar(); }
PYLON_IMGUI_EXPORT bool igBeginTabItem(const char * label, bool * p_open, ImGuiTabItemFlags flags) { return ImGui::BeginTabItem(label, p_open, flags); }
PYLON_IMGUI_EXPORT void igEndTabItem() { ImGui::EndTabItem(); }
PYLON_IMGUI_EXPORT bool igTabItemButton(const char * label, ImGuiTabItemFlags flags) { return ImGui::TabItemButton(label, flags); }
PYLON_IMGUI_EXPORT void igSetTabItemClosed(const char * tab_or_docked_window_label) { ImGui::SetTabItemClosed(tab_or_docked_window_label); }
PYLON_IMGUI_EXPORT void igLogToTTY(int auto_open_depth) { ImGui::LogToTTY(auto_open_depth); }
PYLON_IMGUI_EXPORT void igLogToFile(int auto_open_depth, const char * filename) { ImGui::LogToFile(auto_open_depth, filename); }
PYLON_IMGUI_EXPORT void igLogToClipboard(int auto_open_depth) { ImGui::LogToClipboard(auto_open_depth); }
PYLON_IMGUI_EXPORT void igLogFinish() { ImGui::LogFinish(); }
PYLON_IMGUI_EXPORT void igLogButtons() { ImGui::LogButtons(); }
PYLON_IMGUI_EXPORT void igLogText(const char * fmt) { ImGui::LogText(fmt); }
PYLON_IMGUI_EXPORT void igLogTextV(const char * fmt, va_list args) { ImGui::LogTextV(fmt, args); }
PYLON_IMGUI_EXPORT bool igBeginDragDropSource(ImGuiDragDropFlags flags) { return ImGui::BeginDragDropSource(flags); }
PYLON_IMGUI_EXPORT bool igSetDragDropPayload(const char * type, const void * data, size_t sz, ImGuiCond cond) { return ImGui::SetDragDropPayload(type, data, sz, cond); }
PYLON_IMGUI_EXPORT void igEndDragDropSource() { ImGui::EndDragDropSource(); }
PYLON_IMGUI_EXPORT bool igBeginDragDropTarget() { return ImGui::BeginDragDropTarget(); }
PYLON_IMGUI_EXPORT const ImGuiPayload * igAcceptDragDropPayload(const char * type, ImGuiDragDropFlags flags) { return ImGui::AcceptDragDropPayload(type, flags); }
PYLON_IMGUI_EXPORT void igEndDragDropTarget() { ImGui::EndDragDropTarget(); }
PYLON_IMGUI_EXPORT const ImGuiPayload * igGetDragDropPayload() { return ImGui::GetDragDropPayload(); }
PYLON_IMGUI_EXPORT void igBeginDisabled(bool disabled) { ImGui::BeginDisabled(disabled); }
PYLON_IMGUI_EXPORT void igEndDisabled() { ImGui::EndDisabled(); }
PYLON_IMGUI_EXPORT void igPushClipRect(const ImVec2 clip_rect_min, const ImVec2 clip_rect_max, bool intersect_with_current_clip_rect) { ImGui::PushClipRect(clip_rect_min, clip_rect_max, intersect_with_current_clip_rect); }
PYLON_IMGUI_EXPORT void igPopClipRect() { ImGui::PopClipRect(); }
PYLON_IMGUI_EXPORT void igSetItemDefaultFocus() { ImGui::SetItemDefaultFocus(); }
PYLON_IMGUI_EXPORT void igSetKeyboardFocusHere(int offset) { ImGui::SetKeyboardFocusHere(offset); }
PYLON_IMGUI_EXPORT void igSetNavCursorVisible(bool visible) { ImGui::SetNavCursorVisible(visible); }
PYLON_IMGUI_EXPORT void igSetNextItemAllowOverlap() { ImGui::SetNextItemAllowOverlap(); }
PYLON_IMGUI_EXPORT bool igIsItemHovered(ImGuiHoveredFlags flags) { return ImGui::IsItemHovered(flags); }
PYLON_IMGUI_EXPORT bool igIsItemActive() { return ImGui::IsItemActive(); }
PYLON_IMGUI_EXPORT bool igIsItemFocused() { return ImGui::IsItemFocused(); }
PYLON_IMGUI_EXPORT bool igIsItemClicked(ImGuiMouseButton mouse_button) { return ImGui::IsItemClicked(mouse_button); }
PYLON_IMGUI_EXPORT bool igIsItemVisible() { return ImGui::IsItemVisible(); }
PYLON_IMGUI_EXPORT bool igIsItemEdited() { return ImGui::IsItemEdited(); }
PYLON_IMGUI_EXPORT bool igIsItemActivated() { return ImGui::IsItemActivated(); }
PYLON_IMGUI_EXPORT bool igIsItemDeactivated() { return ImGui::IsItemDeactivated(); }
PYLON_IMGUI_EXPORT bool igIsItemDeactivatedAfterEdit() { return ImGui::IsItemDeactivatedAfterEdit(); }
PYLON_IMGUI_EXPORT bool igIsItemToggledOpen() { return ImGui::IsItemToggledOpen(); }
PYLON_IMGUI_EXPORT bool igIsAnyItemHovered() { return ImGui::IsAnyItemHovered(); }
PYLON_IMGUI_EXPORT bool igIsAnyItemActive() { return ImGui::IsAnyItemActive(); }
PYLON_IMGUI_EXPORT bool igIsAnyItemFocused() { return ImGui::IsAnyItemFocused(); }
PYLON_IMGUI_EXPORT ImGuiID igGetItemID() { return ImGui::GetItemID(); }
PYLON_IMGUI_EXPORT void igGetItemRectMin(ImVec2* pOut) { *pOut = ImGui::GetItemRectMin(); }
PYLON_IMGUI_EXPORT void igGetItemRectMax(ImVec2* pOut) { *pOut = ImGui::GetItemRectMax(); }
PYLON_IMGUI_EXPORT void igGetItemRectSize(ImVec2* pOut) { *pOut = ImGui::GetItemRectSize(); }
PYLON_IMGUI_EXPORT ImGuiViewport * igGetMainViewport() { return ImGui::GetMainViewport(); }
PYLON_IMGUI_EXPORT ImDrawList * igGetBackgroundDrawList() { return ImGui::GetBackgroundDrawList(); }
PYLON_IMGUI_EXPORT ImDrawList * igGetForegroundDrawList() { return ImGui::GetForegroundDrawList(); }
PYLON_IMGUI_EXPORT bool igIsRectVisible(const ImVec2 size) { return ImGui::IsRectVisible(size); }
PYLON_IMGUI_EXPORT double igGetTime() { return ImGui::GetTime(); }
PYLON_IMGUI_EXPORT int igGetFrameCount() { return ImGui::GetFrameCount(); }
PYLON_IMGUI_EXPORT ImDrawListSharedData * igGetDrawListSharedData() { return ImGui::GetDrawListSharedData(); }
PYLON_IMGUI_EXPORT const char * igGetStyleColorName(ImGuiCol idx) { return ImGui::GetStyleColorName(idx); }
PYLON_IMGUI_EXPORT void igSetStateStorage(ImGuiStorage * storage) { ImGui::SetStateStorage(storage); }
PYLON_IMGUI_EXPORT ImGuiStorage * igGetStateStorage() { return ImGui::GetStateStorage(); }
PYLON_IMGUI_EXPORT void igCalcTextSize(ImVec2* pOut, const char * text, const char * text_end, bool hide_text_after_double_hash, float wrap_width) { *pOut = ImGui::CalcTextSize(text, text_end, hide_text_after_double_hash, wrap_width); }
PYLON_IMGUI_EXPORT void igColorConvertU32ToFloat4(ImVec4* pOut, ImU32 in) { *pOut = ImGui::ColorConvertU32ToFloat4(in); }
PYLON_IMGUI_EXPORT ImU32 igColorConvertFloat4ToU32(const ImVec4 in) { return ImGui::ColorConvertFloat4ToU32(in); }
PYLON_IMGUI_EXPORT void igColorConvertRGBtoHSV(float r, float g, float b, float out_h, float out_s, float out_v) { ImGui::ColorConvertRGBtoHSV(r, g, b, out_h, out_s, out_v); }
PYLON_IMGUI_EXPORT void igColorConvertHSVtoRGB(float h, float s, float v, float out_r, float out_g, float out_b) { ImGui::ColorConvertHSVtoRGB(h, s, v, out_r, out_g, out_b); }
PYLON_IMGUI_EXPORT bool igIsKeyDown(ImGuiKey key) { return ImGui::IsKeyDown(key); }
PYLON_IMGUI_EXPORT bool igIsKeyPressed(ImGuiKey key, bool repeat) { return ImGui::IsKeyPressed(key, repeat); }
PYLON_IMGUI_EXPORT bool igIsKeyReleased(ImGuiKey key) { return ImGui::IsKeyReleased(key); }
PYLON_IMGUI_EXPORT bool igIsKeyChordPressed(ImGuiKeyChord key_chord) { return ImGui::IsKeyChordPressed(key_chord); }
PYLON_IMGUI_EXPORT int igGetKeyPressedAmount(ImGuiKey key, float repeat_delay, float rate) { return ImGui::GetKeyPressedAmount(key, repeat_delay, rate); }
PYLON_IMGUI_EXPORT const char * igGetKeyName(ImGuiKey key) { return ImGui::GetKeyName(key); }
PYLON_IMGUI_EXPORT void igSetNextFrameWantCaptureKeyboard(bool want_capture_keyboard) { ImGui::SetNextFrameWantCaptureKeyboard(want_capture_keyboard); }
PYLON_IMGUI_EXPORT bool igShortcut(ImGuiKeyChord key_chord, ImGuiInputFlags flags) { return ImGui::Shortcut(key_chord, flags); }
PYLON_IMGUI_EXPORT void igSetNextItemShortcut(ImGuiKeyChord key_chord, ImGuiInputFlags flags) { ImGui::SetNextItemShortcut(key_chord, flags); }
PYLON_IMGUI_EXPORT void igSetItemKeyOwner(ImGuiKey key) { ImGui::SetItemKeyOwner(key); }
PYLON_IMGUI_EXPORT bool igIsMouseDown(ImGuiMouseButton button) { return ImGui::IsMouseDown(button); }
PYLON_IMGUI_EXPORT bool igIsMouseClicked(ImGuiMouseButton button, bool repeat) { return ImGui::IsMouseClicked(button, repeat); }
PYLON_IMGUI_EXPORT bool igIsMouseReleased(ImGuiMouseButton button) { return ImGui::IsMouseReleased(button); }
PYLON_IMGUI_EXPORT bool igIsMouseDoubleClicked(ImGuiMouseButton button) { return ImGui::IsMouseDoubleClicked(button); }
PYLON_IMGUI_EXPORT bool igIsMouseReleasedWithDelay(ImGuiMouseButton button, float delay) { return ImGui::IsMouseReleasedWithDelay(button, delay); }
PYLON_IMGUI_EXPORT int igGetMouseClickedCount(ImGuiMouseButton button) { return ImGui::GetMouseClickedCount(button); }
PYLON_IMGUI_EXPORT bool igIsMouseHoveringRect(const ImVec2 r_min, const ImVec2 r_max, bool clip) { return ImGui::IsMouseHoveringRect(r_min, r_max, clip); }
PYLON_IMGUI_EXPORT bool igIsMousePosValid(const ImVec2 * mouse_pos) { return ImGui::IsMousePosValid(mouse_pos); }
PYLON_IMGUI_EXPORT bool igIsAnyMouseDown() { return ImGui::IsAnyMouseDown(); }
PYLON_IMGUI_EXPORT void igGetMousePos(ImVec2* pOut) { *pOut = ImGui::GetMousePos(); }
PYLON_IMGUI_EXPORT void igGetMousePosOnOpeningCurrentPopup(ImVec2* pOut) { *pOut = ImGui::GetMousePosOnOpeningCurrentPopup(); }
PYLON_IMGUI_EXPORT bool igIsMouseDragging(ImGuiMouseButton button, float lock_threshold) { return ImGui::IsMouseDragging(button, lock_threshold); }
PYLON_IMGUI_EXPORT void igGetMouseDragDelta(ImVec2* pOut, ImGuiMouseButton button, float lock_threshold) { *pOut = ImGui::GetMouseDragDelta(button, lock_threshold); }
PYLON_IMGUI_EXPORT void igResetMouseDragDelta(ImGuiMouseButton button) { ImGui::ResetMouseDragDelta(button); }
PYLON_IMGUI_EXPORT ImGuiMouseCursor igGetMouseCursor() { return ImGui::GetMouseCursor(); }
PYLON_IMGUI_EXPORT void igSetMouseCursor(ImGuiMouseCursor cursor_type) { ImGui::SetMouseCursor(cursor_type); }
PYLON_IMGUI_EXPORT void igSetNextFrameWantCaptureMouse(bool want_capture_mouse) { ImGui::SetNextFrameWantCaptureMouse(want_capture_mouse); }
PYLON_IMGUI_EXPORT const char * igGetClipboardText() { return ImGui::GetClipboardText(); }
PYLON_IMGUI_EXPORT void igSetClipboardText(const char * text) { ImGui::SetClipboardText(text); }
PYLON_IMGUI_EXPORT void igLoadIniSettingsFromDisk(const char * ini_filename) { ImGui::LoadIniSettingsFromDisk(ini_filename); }
PYLON_IMGUI_EXPORT void igLoadIniSettingsFromMemory(const char * ini_data, size_t ini_size) { ImGui::LoadIniSettingsFromMemory(ini_data, ini_size); }
PYLON_IMGUI_EXPORT void igSaveIniSettingsToDisk(const char * ini_filename) { ImGui::SaveIniSettingsToDisk(ini_filename); }
PYLON_IMGUI_EXPORT const char * igSaveIniSettingsToMemory(size_t * out_ini_size) { return ImGui::SaveIniSettingsToMemory(out_ini_size); }
PYLON_IMGUI_EXPORT void igDebugTextEncoding(const char * text) { ImGui::DebugTextEncoding(text); }
PYLON_IMGUI_EXPORT void igDebugFlashStyleColor(ImGuiCol idx) { ImGui::DebugFlashStyleColor(idx); }
PYLON_IMGUI_EXPORT void igDebugStartItemPicker() { ImGui::DebugStartItemPicker(); }
PYLON_IMGUI_EXPORT bool igDebugCheckVersionAndDataLayout(const char * version_str, size_t sz_io, size_t sz_style, size_t sz_vec2, size_t sz_vec4, size_t sz_drawvert, size_t sz_drawidx) { return ImGui::DebugCheckVersionAndDataLayout(version_str, sz_io, sz_style, sz_vec2, sz_vec4, sz_drawvert, sz_drawidx); }
PYLON_IMGUI_EXPORT void igDebugLogV(const char * fmt, va_list args) { ImGui::DebugLogV(fmt, args); }
PYLON_IMGUI_EXPORT void igSetAllocatorFunctions(ImGuiMemAllocFunc alloc_func, ImGuiMemFreeFunc free_func, void * user_data) { ImGui::SetAllocatorFunctions(alloc_func, free_func, user_data); }
PYLON_IMGUI_EXPORT void igGetAllocatorFunctions(ImGuiMemAllocFunc * p_alloc_func, ImGuiMemFreeFunc * p_free_func, void** p_user_data) { ImGui::GetAllocatorFunctions(p_alloc_func, p_free_func, p_user_data); }
PYLON_IMGUI_EXPORT void * igMemAlloc(size_t size) { return ImGui::MemAlloc(size); }
PYLON_IMGUI_EXPORT void igMemFree(void * ptr) { ImGui::MemFree(ptr); }
PYLON_IMGUI_EXPORT void igSetWindowFontScale(float scale) { ImGui::SetWindowFontScale(scale); }
PYLON_IMGUI_EXPORT void igGetContentRegionMax(ImVec2* pOut) { *pOut = ImGui::GetContentRegionMax(); }
PYLON_IMGUI_EXPORT void igGetWindowContentRegionMin(ImVec2* pOut) { *pOut = ImGui::GetWindowContentRegionMin(); }
PYLON_IMGUI_EXPORT void igGetWindowContentRegionMax(ImVec2* pOut) { *pOut = ImGui::GetWindowContentRegionMax(); }
PYLON_IMGUI_EXPORT void igSetItemAllowOverlap() { ImGui::SetItemAllowOverlap(); }

// ===================== member functions =====================
PYLON_IMGUI_EXPORT void ImGuiStyle_ScaleAllSizes(ImGuiStyle* self, float scale_factor) { self->ScaleAllSizes(scale_factor); }
PYLON_IMGUI_EXPORT void ImGuiIO_AddKeyEvent(ImGuiIO* self, ImGuiKey key, bool down) { self->AddKeyEvent(key, down); }
PYLON_IMGUI_EXPORT void ImGuiIO_AddKeyAnalogEvent(ImGuiIO* self, ImGuiKey key, bool down, float v) { self->AddKeyAnalogEvent(key, down, v); }
PYLON_IMGUI_EXPORT void ImGuiIO_AddMousePosEvent(ImGuiIO* self, float x, float y) { self->AddMousePosEvent(x, y); }
PYLON_IMGUI_EXPORT void ImGuiIO_AddMouseButtonEvent(ImGuiIO* self, int button, bool down) { self->AddMouseButtonEvent(button, down); }
PYLON_IMGUI_EXPORT void ImGuiIO_AddMouseWheelEvent(ImGuiIO* self, float wheel_x, float wheel_y) { self->AddMouseWheelEvent(wheel_x, wheel_y); }
PYLON_IMGUI_EXPORT void ImGuiIO_AddMouseSourceEvent(ImGuiIO* self, ImGuiMouseSource source) { self->AddMouseSourceEvent(source); }
PYLON_IMGUI_EXPORT void ImGuiIO_AddFocusEvent(ImGuiIO* self, bool focused) { self->AddFocusEvent(focused); }
PYLON_IMGUI_EXPORT void ImGuiIO_AddInputCharacter(ImGuiIO* self, unsigned int c) { self->AddInputCharacter(c); }
PYLON_IMGUI_EXPORT void ImGuiIO_AddInputCharacterUTF16(ImGuiIO* self, ImWchar16 c) { self->AddInputCharacterUTF16(c); }
PYLON_IMGUI_EXPORT void ImGuiIO_AddInputCharactersUTF8(ImGuiIO* self, const char * str) { self->AddInputCharactersUTF8(str); }
PYLON_IMGUI_EXPORT void ImGuiIO_SetKeyEventNativeData(ImGuiIO* self, ImGuiKey key, int native_keycode, int native_scancode, int native_legacy_index) { self->SetKeyEventNativeData(key, native_keycode, native_scancode, native_legacy_index); }
PYLON_IMGUI_EXPORT void ImGuiIO_SetAppAcceptingEvents(ImGuiIO* self, bool accepting_events) { self->SetAppAcceptingEvents(accepting_events); }
PYLON_IMGUI_EXPORT void ImGuiIO_ClearEventsQueue(ImGuiIO* self) { self->ClearEventsQueue(); }
PYLON_IMGUI_EXPORT void ImGuiIO_ClearInputKeys(ImGuiIO* self) { self->ClearInputKeys(); }
PYLON_IMGUI_EXPORT void ImGuiIO_ClearInputMouse(ImGuiIO* self) { self->ClearInputMouse(); }
PYLON_IMGUI_EXPORT void ImGuiIO_ClearInputCharacters(ImGuiIO* self) { self->ClearInputCharacters(); }
PYLON_IMGUI_EXPORT void ImGuiInputTextCallbackData_DeleteChars(ImGuiInputTextCallbackData* self, int pos, int bytes_count) { self->DeleteChars(pos, bytes_count); }
PYLON_IMGUI_EXPORT void ImGuiInputTextCallbackData_InsertChars(ImGuiInputTextCallbackData* self, int pos, const char * text, const char * text_end) { self->InsertChars(pos, text, text_end); }
PYLON_IMGUI_EXPORT void ImGuiInputTextCallbackData_SelectAll(ImGuiInputTextCallbackData* self) { self->SelectAll(); }
PYLON_IMGUI_EXPORT void ImGuiInputTextCallbackData_ClearSelection(ImGuiInputTextCallbackData* self) { self->ClearSelection(); }
PYLON_IMGUI_EXPORT bool ImGuiInputTextCallbackData_HasSelection(ImGuiInputTextCallbackData* self) { return self->HasSelection(); }
PYLON_IMGUI_EXPORT void ImGuiPayload_Clear(ImGuiPayload* self) { self->Clear(); }
PYLON_IMGUI_EXPORT bool ImGuiPayload_IsDataType(ImGuiPayload* self, const char * type) { return self->IsDataType(type); }
PYLON_IMGUI_EXPORT bool ImGuiPayload_IsPreview(ImGuiPayload* self) { return self->IsPreview(); }
PYLON_IMGUI_EXPORT bool ImGuiPayload_IsDelivery(ImGuiPayload* self) { return self->IsDelivery(); }
PYLON_IMGUI_EXPORT bool ImGuiTextFilter_Draw(ImGuiTextFilter* self, const char * label, float width) { return self->Draw(label, width); }
PYLON_IMGUI_EXPORT bool ImGuiTextFilter_PassFilter(ImGuiTextFilter* self, const char * text, const char * text_end) { return self->PassFilter(text, text_end); }
PYLON_IMGUI_EXPORT void ImGuiTextFilter_Build(ImGuiTextFilter* self) { self->Build(); }
PYLON_IMGUI_EXPORT void ImGuiTextFilter_Clear(ImGuiTextFilter* self) { self->Clear(); }
PYLON_IMGUI_EXPORT bool ImGuiTextFilter_IsActive(ImGuiTextFilter* self) { return self->IsActive(); }
PYLON_IMGUI_EXPORT const char * ImGuiTextBuffer_begin(ImGuiTextBuffer* self) { return self->begin(); }
PYLON_IMGUI_EXPORT const char * ImGuiTextBuffer_end(ImGuiTextBuffer* self) { return self->end(); }
PYLON_IMGUI_EXPORT int ImGuiTextBuffer_size(ImGuiTextBuffer* self) { return self->size(); }
PYLON_IMGUI_EXPORT bool ImGuiTextBuffer_empty(ImGuiTextBuffer* self) { return self->empty(); }
PYLON_IMGUI_EXPORT void ImGuiTextBuffer_clear(ImGuiTextBuffer* self) { self->clear(); }
PYLON_IMGUI_EXPORT void ImGuiTextBuffer_resize(ImGuiTextBuffer* self, int size) { self->resize(size); }
PYLON_IMGUI_EXPORT void ImGuiTextBuffer_reserve(ImGuiTextBuffer* self, int capacity) { self->reserve(capacity); }
PYLON_IMGUI_EXPORT const char * ImGuiTextBuffer_c_str(ImGuiTextBuffer* self) { return self->c_str(); }
PYLON_IMGUI_EXPORT void ImGuiTextBuffer_append(ImGuiTextBuffer* self, const char * str, const char * str_end) { self->append(str, str_end); }
PYLON_IMGUI_EXPORT void ImGuiStorage_Clear(ImGuiStorage* self) { self->Clear(); }
PYLON_IMGUI_EXPORT int ImGuiStorage_GetInt(ImGuiStorage* self, ImGuiID key, int default_val) { return self->GetInt(key, default_val); }
PYLON_IMGUI_EXPORT void ImGuiStorage_SetInt(ImGuiStorage* self, ImGuiID key, int val) { self->SetInt(key, val); }
PYLON_IMGUI_EXPORT bool ImGuiStorage_GetBool(ImGuiStorage* self, ImGuiID key, bool default_val) { return self->GetBool(key, default_val); }
PYLON_IMGUI_EXPORT void ImGuiStorage_SetBool(ImGuiStorage* self, ImGuiID key, bool val) { self->SetBool(key, val); }
PYLON_IMGUI_EXPORT float ImGuiStorage_GetFloat(ImGuiStorage* self, ImGuiID key, float default_val) { return self->GetFloat(key, default_val); }
PYLON_IMGUI_EXPORT void ImGuiStorage_SetFloat(ImGuiStorage* self, ImGuiID key, float val) { self->SetFloat(key, val); }
PYLON_IMGUI_EXPORT void * ImGuiStorage_GetVoidPtr(ImGuiStorage* self, ImGuiID key) { return self->GetVoidPtr(key); }
PYLON_IMGUI_EXPORT void ImGuiStorage_SetVoidPtr(ImGuiStorage* self, ImGuiID key, void * val) { self->SetVoidPtr(key, val); }
PYLON_IMGUI_EXPORT int * ImGuiStorage_GetIntRef(ImGuiStorage* self, ImGuiID key, int default_val) { return self->GetIntRef(key, default_val); }
PYLON_IMGUI_EXPORT bool * ImGuiStorage_GetBoolRef(ImGuiStorage* self, ImGuiID key, bool default_val) { return self->GetBoolRef(key, default_val); }
PYLON_IMGUI_EXPORT float * ImGuiStorage_GetFloatRef(ImGuiStorage* self, ImGuiID key, float default_val) { return self->GetFloatRef(key, default_val); }
PYLON_IMGUI_EXPORT void * * ImGuiStorage_GetVoidPtrRef(ImGuiStorage* self, ImGuiID key, void * default_val) { return self->GetVoidPtrRef(key, default_val); }
PYLON_IMGUI_EXPORT void ImGuiStorage_BuildSortByKey(ImGuiStorage* self) { self->BuildSortByKey(); }
PYLON_IMGUI_EXPORT void ImGuiStorage_SetAllInt(ImGuiStorage* self, int val) { self->SetAllInt(val); }
PYLON_IMGUI_EXPORT void ImGuiListClipper_Begin(ImGuiListClipper* self, int items_count, float items_height) { self->Begin(items_count, items_height); }
PYLON_IMGUI_EXPORT void ImGuiListClipper_End(ImGuiListClipper* self) { self->End(); }
PYLON_IMGUI_EXPORT bool ImGuiListClipper_Step(ImGuiListClipper* self) { return self->Step(); }
PYLON_IMGUI_EXPORT void ImGuiListClipper_IncludeItemByIndex(ImGuiListClipper* self, int item_index) { self->IncludeItemByIndex(item_index); }
PYLON_IMGUI_EXPORT void ImGuiListClipper_IncludeItemsByIndex(ImGuiListClipper* self, int item_begin, int item_end) { self->IncludeItemsByIndex(item_begin, item_end); }
PYLON_IMGUI_EXPORT void ImGuiListClipper_SeekCursorForItem(ImGuiListClipper* self, int item_index) { self->SeekCursorForItem(item_index); }
PYLON_IMGUI_EXPORT void ImGuiListClipper_IncludeRangeByIndices(ImGuiListClipper* self, int item_begin, int item_end) { self->IncludeRangeByIndices(item_begin, item_end); }
PYLON_IMGUI_EXPORT void ImColor_SetHSV(ImColor* self, float h, float s, float v, float a) { self->SetHSV(h, s, v, a); }
PYLON_IMGUI_EXPORT void ImColor_HSV(ImColor* pOut, ImColor* self, float h, float s, float v, float a) { *pOut = self->HSV(h, s, v, a); }
PYLON_IMGUI_EXPORT void ImGuiSelectionBasicStorage_ApplyRequests(ImGuiSelectionBasicStorage* self, ImGuiMultiSelectIO * ms_io) { self->ApplyRequests(ms_io); }
PYLON_IMGUI_EXPORT bool ImGuiSelectionBasicStorage_Contains(ImGuiSelectionBasicStorage* self, ImGuiID id) { return self->Contains(id); }
PYLON_IMGUI_EXPORT void ImGuiSelectionBasicStorage_Clear(ImGuiSelectionBasicStorage* self) { self->Clear(); }
PYLON_IMGUI_EXPORT void ImGuiSelectionBasicStorage_Swap(ImGuiSelectionBasicStorage* self, ImGuiSelectionBasicStorage r) { self->Swap(r); }
PYLON_IMGUI_EXPORT void ImGuiSelectionBasicStorage_SetItemSelected(ImGuiSelectionBasicStorage* self, ImGuiID id, bool selected) { self->SetItemSelected(id, selected); }
PYLON_IMGUI_EXPORT bool ImGuiSelectionBasicStorage_GetNextSelectedItem(ImGuiSelectionBasicStorage* self, void** opaque_it, ImGuiID * out_id) { return self->GetNextSelectedItem(opaque_it, out_id); }
PYLON_IMGUI_EXPORT ImGuiID ImGuiSelectionBasicStorage_GetStorageIdFromIndex(ImGuiSelectionBasicStorage* self, int idx) { return self->GetStorageIdFromIndex(idx); }
PYLON_IMGUI_EXPORT void ImGuiSelectionExternalStorage_ApplyRequests(ImGuiSelectionExternalStorage* self, ImGuiMultiSelectIO * ms_io) { self->ApplyRequests(ms_io); }
PYLON_IMGUI_EXPORT ImTextureID ImDrawCmd_GetTexID(ImDrawCmd* self) { return self->GetTexID(); }
PYLON_IMGUI_EXPORT void ImDrawListSplitter_Clear(ImDrawListSplitter* self) { self->Clear(); }
PYLON_IMGUI_EXPORT void ImDrawListSplitter_ClearFreeMemory(ImDrawListSplitter* self) { self->ClearFreeMemory(); }
PYLON_IMGUI_EXPORT void ImDrawListSplitter_Split(ImDrawListSplitter* self, ImDrawList * draw_list, int count) { self->Split(draw_list, count); }
PYLON_IMGUI_EXPORT void ImDrawListSplitter_Merge(ImDrawListSplitter* self, ImDrawList * draw_list) { self->Merge(draw_list); }
PYLON_IMGUI_EXPORT void ImDrawListSplitter_SetCurrentChannel(ImDrawListSplitter* self, ImDrawList * draw_list, int channel_idx) { self->SetCurrentChannel(draw_list, channel_idx); }
PYLON_IMGUI_EXPORT void ImDrawList_PushClipRect(ImDrawList* self, const ImVec2 clip_rect_min, const ImVec2 clip_rect_max, bool intersect_with_current_clip_rect) { self->PushClipRect(clip_rect_min, clip_rect_max, intersect_with_current_clip_rect); }
PYLON_IMGUI_EXPORT void ImDrawList_PushClipRectFullScreen(ImDrawList* self) { self->PushClipRectFullScreen(); }
PYLON_IMGUI_EXPORT void ImDrawList_PopClipRect(ImDrawList* self) { self->PopClipRect(); }
PYLON_IMGUI_EXPORT void ImDrawList_PushTexture(ImDrawList* self, ImTextureRef tex_ref) { self->PushTexture(tex_ref); }
PYLON_IMGUI_EXPORT void ImDrawList_PopTexture(ImDrawList* self) { self->PopTexture(); }
PYLON_IMGUI_EXPORT void ImDrawList_GetClipRectMin(ImVec2* pOut, ImDrawList* self) { *pOut = self->GetClipRectMin(); }
PYLON_IMGUI_EXPORT void ImDrawList_GetClipRectMax(ImVec2* pOut, ImDrawList* self) { *pOut = self->GetClipRectMax(); }
PYLON_IMGUI_EXPORT void ImDrawList_AddLine(ImDrawList* self, const ImVec2 p1, const ImVec2 p2, ImU32 col, float thickness) { self->AddLine(p1, p2, col, thickness); }
PYLON_IMGUI_EXPORT void ImDrawList_AddRect(ImDrawList* self, const ImVec2 p_min, const ImVec2 p_max, ImU32 col, float rounding, ImDrawFlags flags, float thickness) { self->AddRect(p_min, p_max, col, rounding, flags, thickness); }
PYLON_IMGUI_EXPORT void ImDrawList_AddRectFilled(ImDrawList* self, const ImVec2 p_min, const ImVec2 p_max, ImU32 col, float rounding, ImDrawFlags flags) { self->AddRectFilled(p_min, p_max, col, rounding, flags); }
PYLON_IMGUI_EXPORT void ImDrawList_AddRectFilledMultiColor(ImDrawList* self, const ImVec2 p_min, const ImVec2 p_max, ImU32 col_upr_left, ImU32 col_upr_right, ImU32 col_bot_right, ImU32 col_bot_left) { self->AddRectFilledMultiColor(p_min, p_max, col_upr_left, col_upr_right, col_bot_right, col_bot_left); }
PYLON_IMGUI_EXPORT void ImDrawList_AddQuad(ImDrawList* self, const ImVec2 p1, const ImVec2 p2, const ImVec2 p3, const ImVec2 p4, ImU32 col, float thickness) { self->AddQuad(p1, p2, p3, p4, col, thickness); }
PYLON_IMGUI_EXPORT void ImDrawList_AddQuadFilled(ImDrawList* self, const ImVec2 p1, const ImVec2 p2, const ImVec2 p3, const ImVec2 p4, ImU32 col) { self->AddQuadFilled(p1, p2, p3, p4, col); }
PYLON_IMGUI_EXPORT void ImDrawList_AddTriangle(ImDrawList* self, const ImVec2 p1, const ImVec2 p2, const ImVec2 p3, ImU32 col, float thickness) { self->AddTriangle(p1, p2, p3, col, thickness); }
PYLON_IMGUI_EXPORT void ImDrawList_AddTriangleFilled(ImDrawList* self, const ImVec2 p1, const ImVec2 p2, const ImVec2 p3, ImU32 col) { self->AddTriangleFilled(p1, p2, p3, col); }
PYLON_IMGUI_EXPORT void ImDrawList_AddCircle(ImDrawList* self, const ImVec2 center, float radius, ImU32 col, int num_segments, float thickness) { self->AddCircle(center, radius, col, num_segments, thickness); }
PYLON_IMGUI_EXPORT void ImDrawList_AddCircleFilled(ImDrawList* self, const ImVec2 center, float radius, ImU32 col, int num_segments) { self->AddCircleFilled(center, radius, col, num_segments); }
PYLON_IMGUI_EXPORT void ImDrawList_AddNgon(ImDrawList* self, const ImVec2 center, float radius, ImU32 col, int num_segments, float thickness) { self->AddNgon(center, radius, col, num_segments, thickness); }
PYLON_IMGUI_EXPORT void ImDrawList_AddNgonFilled(ImDrawList* self, const ImVec2 center, float radius, ImU32 col, int num_segments) { self->AddNgonFilled(center, radius, col, num_segments); }
PYLON_IMGUI_EXPORT void ImDrawList_AddEllipse(ImDrawList* self, const ImVec2 center, const ImVec2 radius, ImU32 col, float rot, int num_segments, float thickness) { self->AddEllipse(center, radius, col, rot, num_segments, thickness); }
PYLON_IMGUI_EXPORT void ImDrawList_AddEllipseFilled(ImDrawList* self, const ImVec2 center, const ImVec2 radius, ImU32 col, float rot, int num_segments) { self->AddEllipseFilled(center, radius, col, rot, num_segments); }
PYLON_IMGUI_EXPORT void ImDrawList_AddText(ImDrawList* self, const ImVec2 pos, ImU32 col, const char * text_begin, const char * text_end) { self->AddText(pos, col, text_begin, text_end); }
PYLON_IMGUI_EXPORT void ImDrawList_AddText_FontPtr(ImDrawList* self, ImFont * font, float font_size, const ImVec2 pos, ImU32 col, const char * text_begin, const char * text_end, float wrap_width, const ImVec4 * cpu_fine_clip_rect) { self->AddText(font, font_size, pos, col, text_begin, text_end, wrap_width, cpu_fine_clip_rect); }
PYLON_IMGUI_EXPORT void ImDrawList_AddBezierCubic(ImDrawList* self, const ImVec2 p1, const ImVec2 p2, const ImVec2 p3, const ImVec2 p4, ImU32 col, float thickness, int num_segments) { self->AddBezierCubic(p1, p2, p3, p4, col, thickness, num_segments); }
PYLON_IMGUI_EXPORT void ImDrawList_AddBezierQuadratic(ImDrawList* self, const ImVec2 p1, const ImVec2 p2, const ImVec2 p3, ImU32 col, float thickness, int num_segments) { self->AddBezierQuadratic(p1, p2, p3, col, thickness, num_segments); }
PYLON_IMGUI_EXPORT void ImDrawList_AddPolyline(ImDrawList* self, const ImVec2 * points, int num_points, ImU32 col, ImDrawFlags flags, float thickness) { self->AddPolyline(points, num_points, col, flags, thickness); }
PYLON_IMGUI_EXPORT void ImDrawList_AddConvexPolyFilled(ImDrawList* self, const ImVec2 * points, int num_points, ImU32 col) { self->AddConvexPolyFilled(points, num_points, col); }
PYLON_IMGUI_EXPORT void ImDrawList_AddConcavePolyFilled(ImDrawList* self, const ImVec2 * points, int num_points, ImU32 col) { self->AddConcavePolyFilled(points, num_points, col); }
PYLON_IMGUI_EXPORT void ImDrawList_AddImage(ImDrawList* self, ImTextureRef tex_ref, const ImVec2 p_min, const ImVec2 p_max, const ImVec2 uv_min, const ImVec2 uv_max, ImU32 col) { self->AddImage(tex_ref, p_min, p_max, uv_min, uv_max, col); }
PYLON_IMGUI_EXPORT void ImDrawList_AddImageQuad(ImDrawList* self, ImTextureRef tex_ref, const ImVec2 p1, const ImVec2 p2, const ImVec2 p3, const ImVec2 p4, const ImVec2 uv1, const ImVec2 uv2, const ImVec2 uv3, const ImVec2 uv4, ImU32 col) { self->AddImageQuad(tex_ref, p1, p2, p3, p4, uv1, uv2, uv3, uv4, col); }
PYLON_IMGUI_EXPORT void ImDrawList_AddImageRounded(ImDrawList* self, ImTextureRef tex_ref, const ImVec2 p_min, const ImVec2 p_max, const ImVec2 uv_min, const ImVec2 uv_max, ImU32 col, float rounding, ImDrawFlags flags) { self->AddImageRounded(tex_ref, p_min, p_max, uv_min, uv_max, col, rounding, flags); }
PYLON_IMGUI_EXPORT void ImDrawList_PathClear(ImDrawList* self) { self->PathClear(); }
PYLON_IMGUI_EXPORT void ImDrawList_PathLineTo(ImDrawList* self, const ImVec2 pos) { self->PathLineTo(pos); }
PYLON_IMGUI_EXPORT void ImDrawList_PathLineToMergeDuplicate(ImDrawList* self, const ImVec2 pos) { self->PathLineToMergeDuplicate(pos); }
PYLON_IMGUI_EXPORT void ImDrawList_PathFillConvex(ImDrawList* self, ImU32 col) { self->PathFillConvex(col); }
PYLON_IMGUI_EXPORT void ImDrawList_PathFillConcave(ImDrawList* self, ImU32 col) { self->PathFillConcave(col); }
PYLON_IMGUI_EXPORT void ImDrawList_PathStroke(ImDrawList* self, ImU32 col, ImDrawFlags flags, float thickness) { self->PathStroke(col, flags, thickness); }
PYLON_IMGUI_EXPORT void ImDrawList_PathArcTo(ImDrawList* self, const ImVec2 center, float radius, float a_min, float a_max, int num_segments) { self->PathArcTo(center, radius, a_min, a_max, num_segments); }
PYLON_IMGUI_EXPORT void ImDrawList_PathArcToFast(ImDrawList* self, const ImVec2 center, float radius, int a_min_of_12, int a_max_of_12) { self->PathArcToFast(center, radius, a_min_of_12, a_max_of_12); }
PYLON_IMGUI_EXPORT void ImDrawList_PathEllipticalArcTo(ImDrawList* self, const ImVec2 center, const ImVec2 radius, float rot, float a_min, float a_max, int num_segments) { self->PathEllipticalArcTo(center, radius, rot, a_min, a_max, num_segments); }
PYLON_IMGUI_EXPORT void ImDrawList_PathBezierCubicCurveTo(ImDrawList* self, const ImVec2 p2, const ImVec2 p3, const ImVec2 p4, int num_segments) { self->PathBezierCubicCurveTo(p2, p3, p4, num_segments); }
PYLON_IMGUI_EXPORT void ImDrawList_PathBezierQuadraticCurveTo(ImDrawList* self, const ImVec2 p2, const ImVec2 p3, int num_segments) { self->PathBezierQuadraticCurveTo(p2, p3, num_segments); }
PYLON_IMGUI_EXPORT void ImDrawList_PathRect(ImDrawList* self, const ImVec2 rect_min, const ImVec2 rect_max, float rounding, ImDrawFlags flags) { self->PathRect(rect_min, rect_max, rounding, flags); }
PYLON_IMGUI_EXPORT void ImDrawList_AddCallback(ImDrawList* self, ImDrawCallback callback, void * userdata, size_t userdata_size) { self->AddCallback(callback, userdata, userdata_size); }
PYLON_IMGUI_EXPORT void ImDrawList_AddDrawCmd(ImDrawList* self) { self->AddDrawCmd(); }
PYLON_IMGUI_EXPORT ImDrawList * ImDrawList_CloneOutput(ImDrawList* self) { return self->CloneOutput(); }
PYLON_IMGUI_EXPORT void ImDrawList_ChannelsSplit(ImDrawList* self, int count) { self->ChannelsSplit(count); }
PYLON_IMGUI_EXPORT void ImDrawList_ChannelsMerge(ImDrawList* self) { self->ChannelsMerge(); }
PYLON_IMGUI_EXPORT void ImDrawList_ChannelsSetCurrent(ImDrawList* self, int n) { self->ChannelsSetCurrent(n); }
PYLON_IMGUI_EXPORT void ImDrawList_PrimReserve(ImDrawList* self, int idx_count, int vtx_count) { self->PrimReserve(idx_count, vtx_count); }
PYLON_IMGUI_EXPORT void ImDrawList_PrimUnreserve(ImDrawList* self, int idx_count, int vtx_count) { self->PrimUnreserve(idx_count, vtx_count); }
PYLON_IMGUI_EXPORT void ImDrawList_PrimRect(ImDrawList* self, const ImVec2 a, const ImVec2 b, ImU32 col) { self->PrimRect(a, b, col); }
PYLON_IMGUI_EXPORT void ImDrawList_PrimRectUV(ImDrawList* self, const ImVec2 a, const ImVec2 b, const ImVec2 uv_a, const ImVec2 uv_b, ImU32 col) { self->PrimRectUV(a, b, uv_a, uv_b, col); }
PYLON_IMGUI_EXPORT void ImDrawList_PrimQuadUV(ImDrawList* self, const ImVec2 a, const ImVec2 b, const ImVec2 c, const ImVec2 d, const ImVec2 uv_a, const ImVec2 uv_b, const ImVec2 uv_c, const ImVec2 uv_d, ImU32 col) { self->PrimQuadUV(a, b, c, d, uv_a, uv_b, uv_c, uv_d, col); }
PYLON_IMGUI_EXPORT void ImDrawList_PrimWriteVtx(ImDrawList* self, const ImVec2 pos, const ImVec2 uv, ImU32 col) { self->PrimWriteVtx(pos, uv, col); }
PYLON_IMGUI_EXPORT void ImDrawList_PrimWriteIdx(ImDrawList* self, ImDrawIdx idx) { self->PrimWriteIdx(idx); }
PYLON_IMGUI_EXPORT void ImDrawList_PrimVtx(ImDrawList* self, const ImVec2 pos, const ImVec2 uv, ImU32 col) { self->PrimVtx(pos, uv, col); }
PYLON_IMGUI_EXPORT void ImDrawList_PushTextureID(ImDrawList* self, ImTextureRef tex_ref) { self->PushTextureID(tex_ref); }
PYLON_IMGUI_EXPORT void ImDrawList_PopTextureID(ImDrawList* self) { self->PopTextureID(); }
PYLON_IMGUI_EXPORT void ImDrawList__SetDrawListSharedData(ImDrawList* self, ImDrawListSharedData * data) { self->_SetDrawListSharedData(data); }
PYLON_IMGUI_EXPORT void ImDrawList__ResetForNewFrame(ImDrawList* self) { self->_ResetForNewFrame(); }
PYLON_IMGUI_EXPORT void ImDrawList__ClearFreeMemory(ImDrawList* self) { self->_ClearFreeMemory(); }
PYLON_IMGUI_EXPORT void ImDrawList__PopUnusedDrawCmd(ImDrawList* self) { self->_PopUnusedDrawCmd(); }
PYLON_IMGUI_EXPORT void ImDrawList__TryMergeDrawCmds(ImDrawList* self) { self->_TryMergeDrawCmds(); }
PYLON_IMGUI_EXPORT void ImDrawList__OnChangedClipRect(ImDrawList* self) { self->_OnChangedClipRect(); }
PYLON_IMGUI_EXPORT void ImDrawList__OnChangedTexture(ImDrawList* self) { self->_OnChangedTexture(); }
PYLON_IMGUI_EXPORT void ImDrawList__OnChangedVtxOffset(ImDrawList* self) { self->_OnChangedVtxOffset(); }
PYLON_IMGUI_EXPORT void ImDrawList__SetTexture(ImDrawList* self, ImTextureRef tex_ref) { self->_SetTexture(tex_ref); }
PYLON_IMGUI_EXPORT int ImDrawList__CalcCircleAutoSegmentCount(ImDrawList* self, float radius) { return self->_CalcCircleAutoSegmentCount(radius); }
PYLON_IMGUI_EXPORT void ImDrawList__PathArcToFastEx(ImDrawList* self, const ImVec2 center, float radius, int a_min_sample, int a_max_sample, int a_step) { self->_PathArcToFastEx(center, radius, a_min_sample, a_max_sample, a_step); }
PYLON_IMGUI_EXPORT void ImDrawList__PathArcToN(ImDrawList* self, const ImVec2 center, float radius, float a_min, float a_max, int num_segments) { self->_PathArcToN(center, radius, a_min, a_max, num_segments); }
PYLON_IMGUI_EXPORT void ImDrawData_Clear(ImDrawData* self) { self->Clear(); }
PYLON_IMGUI_EXPORT void ImDrawData_AddDrawList(ImDrawData* self, ImDrawList * draw_list) { self->AddDrawList(draw_list); }
PYLON_IMGUI_EXPORT void ImDrawData_DeIndexAllBuffers(ImDrawData* self) { self->DeIndexAllBuffers(); }
PYLON_IMGUI_EXPORT void ImDrawData_ScaleClipRects(ImDrawData* self, const ImVec2 fb_scale) { self->ScaleClipRects(fb_scale); }
PYLON_IMGUI_EXPORT void ImTextureData_Create(ImTextureData* self, ImTextureFormat format, int w, int h) { self->Create(format, w, h); }
PYLON_IMGUI_EXPORT void ImTextureData_DestroyPixels(ImTextureData* self) { self->DestroyPixels(); }
PYLON_IMGUI_EXPORT void * ImTextureData_GetPixels(ImTextureData* self) { return self->GetPixels(); }
PYLON_IMGUI_EXPORT void * ImTextureData_GetPixelsAt(ImTextureData* self, int x, int y) { return self->GetPixelsAt(x, y); }
PYLON_IMGUI_EXPORT int ImTextureData_GetSizeInBytes(ImTextureData* self) { return self->GetSizeInBytes(); }
PYLON_IMGUI_EXPORT int ImTextureData_GetPitch(ImTextureData* self) { return self->GetPitch(); }
PYLON_IMGUI_EXPORT ImTextureRef ImTextureData_GetTexRef(ImTextureData* self) { return self->GetTexRef(); }
PYLON_IMGUI_EXPORT ImTextureID ImTextureData_GetTexID(ImTextureData* self) { return self->GetTexID(); }
PYLON_IMGUI_EXPORT void ImTextureData_SetTexID(ImTextureData* self, ImTextureID tex_id) { self->SetTexID(tex_id); }
PYLON_IMGUI_EXPORT void ImTextureData_SetStatus(ImTextureData* self, ImTextureStatus status) { self->SetStatus(status); }
PYLON_IMGUI_EXPORT void ImFontGlyphRangesBuilder_Clear(ImFontGlyphRangesBuilder* self) { self->Clear(); }
PYLON_IMGUI_EXPORT bool ImFontGlyphRangesBuilder_GetBit(ImFontGlyphRangesBuilder* self, size_t n) { return self->GetBit(n); }
PYLON_IMGUI_EXPORT void ImFontGlyphRangesBuilder_SetBit(ImFontGlyphRangesBuilder* self, size_t n) { self->SetBit(n); }
PYLON_IMGUI_EXPORT void ImFontGlyphRangesBuilder_AddChar(ImFontGlyphRangesBuilder* self, ImWchar c) { self->AddChar(c); }
PYLON_IMGUI_EXPORT void ImFontGlyphRangesBuilder_AddText(ImFontGlyphRangesBuilder* self, const char * text, const char * text_end) { self->AddText(text, text_end); }
PYLON_IMGUI_EXPORT void ImFontGlyphRangesBuilder_AddRanges(ImFontGlyphRangesBuilder* self, const ImWchar * ranges) { self->AddRanges(ranges); }
PYLON_IMGUI_EXPORT void ImFontGlyphRangesBuilder_BuildRanges(ImFontGlyphRangesBuilder* self, ImVector < ImWchar > * out_ranges) { self->BuildRanges(out_ranges); }
PYLON_IMGUI_EXPORT ImFont * ImFontAtlas_AddFont(ImFontAtlas* self, const ImFontConfig * font_cfg) { return self->AddFont(font_cfg); }
PYLON_IMGUI_EXPORT ImFont * ImFontAtlas_AddFontDefault(ImFontAtlas* self, const ImFontConfig * font_cfg) { return self->AddFontDefault(font_cfg); }
PYLON_IMGUI_EXPORT ImFont * ImFontAtlas_AddFontFromFileTTF(ImFontAtlas* self, const char * filename, float size_pixels, const ImFontConfig * font_cfg, const ImWchar * glyph_ranges) { return self->AddFontFromFileTTF(filename, size_pixels, font_cfg, glyph_ranges); }
PYLON_IMGUI_EXPORT ImFont * ImFontAtlas_AddFontFromMemoryTTF(ImFontAtlas* self, void * font_data, int font_data_size, float size_pixels, const ImFontConfig * font_cfg, const ImWchar * glyph_ranges) { return self->AddFontFromMemoryTTF(font_data, font_data_size, size_pixels, font_cfg, glyph_ranges); }
PYLON_IMGUI_EXPORT ImFont * ImFontAtlas_AddFontFromMemoryCompressedTTF(ImFontAtlas* self, const void * compressed_font_data, int compressed_font_data_size, float size_pixels, const ImFontConfig * font_cfg, const ImWchar * glyph_ranges) { return self->AddFontFromMemoryCompressedTTF(compressed_font_data, compressed_font_data_size, size_pixels, font_cfg, glyph_ranges); }
PYLON_IMGUI_EXPORT ImFont * ImFontAtlas_AddFontFromMemoryCompressedBase85TTF(ImFontAtlas* self, const char * compressed_font_data_base85, float size_pixels, const ImFontConfig * font_cfg, const ImWchar * glyph_ranges) { return self->AddFontFromMemoryCompressedBase85TTF(compressed_font_data_base85, size_pixels, font_cfg, glyph_ranges); }
PYLON_IMGUI_EXPORT void ImFontAtlas_RemoveFont(ImFontAtlas* self, ImFont * font) { self->RemoveFont(font); }
PYLON_IMGUI_EXPORT void ImFontAtlas_Clear(ImFontAtlas* self) { self->Clear(); }
PYLON_IMGUI_EXPORT void ImFontAtlas_CompactCache(ImFontAtlas* self) { self->CompactCache(); }
PYLON_IMGUI_EXPORT void ImFontAtlas_SetFontLoader(ImFontAtlas* self, const ImFontLoader * font_loader) { self->SetFontLoader(font_loader); }
PYLON_IMGUI_EXPORT void ImFontAtlas_ClearInputData(ImFontAtlas* self) { self->ClearInputData(); }
PYLON_IMGUI_EXPORT void ImFontAtlas_ClearFonts(ImFontAtlas* self) { self->ClearFonts(); }
PYLON_IMGUI_EXPORT void ImFontAtlas_ClearTexData(ImFontAtlas* self) { self->ClearTexData(); }
PYLON_IMGUI_EXPORT bool ImFontAtlas_Build(ImFontAtlas* self) { return self->Build(); }
PYLON_IMGUI_EXPORT void ImFontAtlas_GetTexDataAsAlpha8(ImFontAtlas* self, unsigned char** out_pixels, int * out_width, int * out_height, int * out_bytes_per_pixel) { self->GetTexDataAsAlpha8(out_pixels, out_width, out_height, out_bytes_per_pixel); }
PYLON_IMGUI_EXPORT void ImFontAtlas_GetTexDataAsRGBA32(ImFontAtlas* self, unsigned char** out_pixels, int * out_width, int * out_height, int * out_bytes_per_pixel) { self->GetTexDataAsRGBA32(out_pixels, out_width, out_height, out_bytes_per_pixel); }
PYLON_IMGUI_EXPORT void ImFontAtlas_SetTexID(ImFontAtlas* self, ImTextureID id) { self->SetTexID(id); }
PYLON_IMGUI_EXPORT void ImFontAtlas_SetTexID_ImTextureRefArg(ImFontAtlas* self, ImTextureRef id) { self->SetTexID(id); }
PYLON_IMGUI_EXPORT bool ImFontAtlas_IsBuilt(ImFontAtlas* self) { return self->IsBuilt(); }
PYLON_IMGUI_EXPORT const ImWchar * ImFontAtlas_GetGlyphRangesDefault(ImFontAtlas* self) { return self->GetGlyphRangesDefault(); }
PYLON_IMGUI_EXPORT const ImWchar * ImFontAtlas_GetGlyphRangesGreek(ImFontAtlas* self) { return self->GetGlyphRangesGreek(); }
PYLON_IMGUI_EXPORT const ImWchar * ImFontAtlas_GetGlyphRangesKorean(ImFontAtlas* self) { return self->GetGlyphRangesKorean(); }
PYLON_IMGUI_EXPORT const ImWchar * ImFontAtlas_GetGlyphRangesJapanese(ImFontAtlas* self) { return self->GetGlyphRangesJapanese(); }
PYLON_IMGUI_EXPORT const ImWchar * ImFontAtlas_GetGlyphRangesChineseFull(ImFontAtlas* self) { return self->GetGlyphRangesChineseFull(); }
PYLON_IMGUI_EXPORT const ImWchar * ImFontAtlas_GetGlyphRangesChineseSimplifiedCommon(ImFontAtlas* self) { return self->GetGlyphRangesChineseSimplifiedCommon(); }
PYLON_IMGUI_EXPORT const ImWchar * ImFontAtlas_GetGlyphRangesCyrillic(ImFontAtlas* self) { return self->GetGlyphRangesCyrillic(); }
PYLON_IMGUI_EXPORT const ImWchar * ImFontAtlas_GetGlyphRangesThai(ImFontAtlas* self) { return self->GetGlyphRangesThai(); }
PYLON_IMGUI_EXPORT const ImWchar * ImFontAtlas_GetGlyphRangesVietnamese(ImFontAtlas* self) { return self->GetGlyphRangesVietnamese(); }
PYLON_IMGUI_EXPORT ImFontAtlasRectId ImFontAtlas_AddCustomRect(ImFontAtlas* self, int width, int height, ImFontAtlasRect * out_r) { return self->AddCustomRect(width, height, out_r); }
PYLON_IMGUI_EXPORT void ImFontAtlas_RemoveCustomRect(ImFontAtlas* self, ImFontAtlasRectId id) { self->RemoveCustomRect(id); }
PYLON_IMGUI_EXPORT bool ImFontAtlas_GetCustomRect(ImFontAtlas* self, ImFontAtlasRectId id, ImFontAtlasRect * out_r) { return self->GetCustomRect(id, out_r); }
PYLON_IMGUI_EXPORT ImFontAtlasRectId ImFontAtlas_AddCustomRectRegular(ImFontAtlas* self, int w, int h) { return self->AddCustomRectRegular(w, h); }
PYLON_IMGUI_EXPORT const ImFontAtlasRect * ImFontAtlas_GetCustomRectByIndex(ImFontAtlas* self, ImFontAtlasRectId id) { return self->GetCustomRectByIndex(id); }
PYLON_IMGUI_EXPORT void ImFontAtlas_CalcCustomRectUV(ImFontAtlas* self, const ImFontAtlasRect * r, ImVec2 * out_uv_min, ImVec2 * out_uv_max) { self->CalcCustomRectUV(r, out_uv_min, out_uv_max); }
PYLON_IMGUI_EXPORT ImFontAtlasRectId ImFontAtlas_AddCustomRectFontGlyph(ImFontAtlas* self, ImFont * font, ImWchar codepoint, int w, int h, float advance_x, const ImVec2 offset) { return self->AddCustomRectFontGlyph(font, codepoint, w, h, advance_x, offset); }
PYLON_IMGUI_EXPORT ImFontAtlasRectId ImFontAtlas_AddCustomRectFontGlyphForSize(ImFontAtlas* self, ImFont * font, float font_size, ImWchar codepoint, int w, int h, float advance_x, const ImVec2 offset) { return self->AddCustomRectFontGlyphForSize(font, font_size, codepoint, w, h, advance_x, offset); }
PYLON_IMGUI_EXPORT void ImFontBaked_ClearOutputData(ImFontBaked* self) { self->ClearOutputData(); }
PYLON_IMGUI_EXPORT ImFontGlyph * ImFontBaked_FindGlyph(ImFontBaked* self, ImWchar c) { return self->FindGlyph(c); }
PYLON_IMGUI_EXPORT ImFontGlyph * ImFontBaked_FindGlyphNoFallback(ImFontBaked* self, ImWchar c) { return self->FindGlyphNoFallback(c); }
PYLON_IMGUI_EXPORT float ImFontBaked_GetCharAdvance(ImFontBaked* self, ImWchar c) { return self->GetCharAdvance(c); }
PYLON_IMGUI_EXPORT bool ImFontBaked_IsGlyphLoaded(ImFontBaked* self, ImWchar c) { return self->IsGlyphLoaded(c); }
PYLON_IMGUI_EXPORT bool ImFont_IsGlyphInFont(ImFont* self, ImWchar c) { return self->IsGlyphInFont(c); }
PYLON_IMGUI_EXPORT bool ImFont_IsLoaded(ImFont* self) { return self->IsLoaded(); }
PYLON_IMGUI_EXPORT const char * ImFont_GetDebugName(ImFont* self) { return self->GetDebugName(); }
PYLON_IMGUI_EXPORT ImFontBaked * ImFont_GetFontBaked(ImFont* self, float font_size, float density) { return self->GetFontBaked(font_size, density); }
PYLON_IMGUI_EXPORT void ImFont_CalcTextSizeA(ImVec2* pOut, ImFont* self, float size, float max_width, float wrap_width, const char * text_begin, const char * text_end, const char** remaining) { *pOut = self->CalcTextSizeA(size, max_width, wrap_width, text_begin, text_end, remaining); }
PYLON_IMGUI_EXPORT const char * ImFont_CalcWordWrapPosition(ImFont* self, float size, const char * text, const char * text_end, float wrap_width) { return self->CalcWordWrapPosition(size, text, text_end, wrap_width); }
PYLON_IMGUI_EXPORT void ImFont_RenderChar(ImFont* self, ImDrawList * draw_list, float size, const ImVec2 pos, ImU32 col, ImWchar c, const ImVec4 * cpu_fine_clip) { self->RenderChar(draw_list, size, pos, col, c, cpu_fine_clip); }
PYLON_IMGUI_EXPORT void ImFont_RenderText(ImFont* self, ImDrawList * draw_list, float size, const ImVec2 pos, ImU32 col, const ImVec4 clip_rect, const char * text_begin, const char * text_end, float wrap_width, bool cpu_fine_clip) { self->RenderText(draw_list, size, pos, col, clip_rect, text_begin, text_end, wrap_width, cpu_fine_clip); }
PYLON_IMGUI_EXPORT const char * ImFont_CalcWordWrapPositionA(ImFont* self, float scale, const char * text, const char * text_end, float wrap_width) { return self->CalcWordWrapPositionA(scale, text, text_end, wrap_width); }
PYLON_IMGUI_EXPORT void ImFont_ClearOutputData(ImFont* self) { self->ClearOutputData(); }
PYLON_IMGUI_EXPORT void ImFont_AddRemapChar(ImFont* self, ImWchar from_codepoint, ImWchar to_codepoint) { self->AddRemapChar(from_codepoint, to_codepoint); }
PYLON_IMGUI_EXPORT bool ImFont_IsGlyphRangeUnused(ImFont* self, unsigned int c_begin, unsigned int c_last) { return self->IsGlyphRangeUnused(c_begin, c_last); }
PYLON_IMGUI_EXPORT void ImGuiViewport_GetCenter(ImVec2* pOut, ImGuiViewport* self) { *pOut = self->GetCenter(); }
PYLON_IMGUI_EXPORT void ImGuiViewport_GetWorkCenter(ImVec2* pOut, ImGuiViewport* self) { *pOut = self->GetWorkCenter(); }
PYLON_IMGUI_EXPORT void ImRect_GetCenter(ImVec2* pOut, ImRect* self) { *pOut = self->GetCenter(); }
PYLON_IMGUI_EXPORT void ImRect_GetSize(ImVec2* pOut, ImRect* self) { *pOut = self->GetSize(); }
PYLON_IMGUI_EXPORT float ImRect_GetWidth(ImRect* self) { return self->GetWidth(); }
PYLON_IMGUI_EXPORT float ImRect_GetHeight(ImRect* self) { return self->GetHeight(); }
PYLON_IMGUI_EXPORT float ImRect_GetArea(ImRect* self) { return self->GetArea(); }
PYLON_IMGUI_EXPORT void ImRect_GetTL(ImVec2* pOut, ImRect* self) { *pOut = self->GetTL(); }
PYLON_IMGUI_EXPORT void ImRect_GetTR(ImVec2* pOut, ImRect* self) { *pOut = self->GetTR(); }
PYLON_IMGUI_EXPORT void ImRect_GetBL(ImVec2* pOut, ImRect* self) { *pOut = self->GetBL(); }
PYLON_IMGUI_EXPORT void ImRect_GetBR(ImVec2* pOut, ImRect* self) { *pOut = self->GetBR(); }
PYLON_IMGUI_EXPORT bool ImRect_Contains(ImRect* self, const ImVec2 p) { return self->Contains(p); }
PYLON_IMGUI_EXPORT bool ImRect_Contains_ImRectArg(ImRect* self, const ImRect r) { return self->Contains(r); }
PYLON_IMGUI_EXPORT bool ImRect_ContainsWithPad(ImRect* self, const ImVec2 p, const ImVec2 pad) { return self->ContainsWithPad(p, pad); }
PYLON_IMGUI_EXPORT bool ImRect_Overlaps(ImRect* self, const ImRect r) { return self->Overlaps(r); }
PYLON_IMGUI_EXPORT void ImRect_Add(ImRect* self, const ImVec2 p) { self->Add(p); }
PYLON_IMGUI_EXPORT void ImRect_Add_ImRectArg(ImRect* self, const ImRect r) { self->Add(r); }
PYLON_IMGUI_EXPORT void ImRect_Expand(ImRect* self, const float amount) { self->Expand(amount); }
PYLON_IMGUI_EXPORT void ImRect_Expand_ImVec2Arg(ImRect* self, const ImVec2 amount) { self->Expand(amount); }
PYLON_IMGUI_EXPORT void ImRect_Translate(ImRect* self, const ImVec2 d) { self->Translate(d); }
PYLON_IMGUI_EXPORT void ImRect_TranslateX(ImRect* self, float dx) { self->TranslateX(dx); }
PYLON_IMGUI_EXPORT void ImRect_TranslateY(ImRect* self, float dy) { self->TranslateY(dy); }
PYLON_IMGUI_EXPORT void ImRect_ClipWith(ImRect* self, const ImRect r) { self->ClipWith(r); }
PYLON_IMGUI_EXPORT void ImRect_ClipWithFull(ImRect* self, const ImRect r) { self->ClipWithFull(r); }
PYLON_IMGUI_EXPORT void ImRect_Floor(ImRect* self) { self->Floor(); }
PYLON_IMGUI_EXPORT bool ImRect_IsInverted(ImRect* self) { return self->IsInverted(); }
PYLON_IMGUI_EXPORT void ImRect_ToVec4(ImVec4* pOut, ImRect* self) { *pOut = self->ToVec4(); }
PYLON_IMGUI_EXPORT void ImDrawListSharedData_SetCircleTessellationMaxError(ImDrawListSharedData* self, float max_error) { self->SetCircleTessellationMaxError(max_error); }

// ===================== constructors / destructors =====================
PYLON_IMGUI_EXPORT ImVec2* ImVec2_ImVec2_Nil() { return IM_NEW(ImVec2)(ImVec2()); }
PYLON_IMGUI_EXPORT ImVec2* ImVec2_ImVec2_Float(float _x, float _y) { return IM_NEW(ImVec2)(ImVec2(_x, _y)); }
PYLON_IMGUI_EXPORT ImVec4* ImVec4_ImVec4_Nil() { return IM_NEW(ImVec4)(ImVec4()); }
PYLON_IMGUI_EXPORT ImVec4* ImVec4_ImVec4_Float(float _x, float _y, float _z, float _w) { return IM_NEW(ImVec4)(ImVec4(_x, _y, _z, _w)); }
PYLON_IMGUI_EXPORT ImColor* ImColor_ImColor_Nil() { return IM_NEW(ImColor)(ImColor()); }
PYLON_IMGUI_EXPORT ImColor* ImColor_ImColor_Float(float r, float g, float b, float a) { return IM_NEW(ImColor)(ImColor(r, g, b, a)); }
PYLON_IMGUI_EXPORT ImColor* ImColor_ImColor_Int(int r, int g, int b, int a) { return IM_NEW(ImColor)(ImColor(r, g, b, a)); }
PYLON_IMGUI_EXPORT ImColor* ImColor_ImColor_U32(ImU32 rgba) { return IM_NEW(ImColor)(ImColor(rgba)); }
PYLON_IMGUI_EXPORT ImColor* ImColor_ImColor_Vec4(ImVec4 col) { return IM_NEW(ImColor)(ImColor(col)); }
PYLON_IMGUI_EXPORT ImDrawCmd* ImDrawCmd_ImDrawCmd_Nil() { return IM_NEW(ImDrawCmd)(ImDrawCmd()); }
PYLON_IMGUI_EXPORT ImDrawData* ImDrawData_ImDrawData_Nil() { return IM_NEW(ImDrawData)(ImDrawData()); }
PYLON_IMGUI_EXPORT ImDrawListSplitter* ImDrawListSplitter_ImDrawListSplitter_Nil() { return IM_NEW(ImDrawListSplitter)(ImDrawListSplitter()); }
PYLON_IMGUI_EXPORT ImFont* ImFont_ImFont_Nil() { return IM_NEW(ImFont)(ImFont()); }
PYLON_IMGUI_EXPORT ImFontAtlas* ImFontAtlas_ImFontAtlas_Nil() { return IM_NEW(ImFontAtlas)(ImFontAtlas()); }
PYLON_IMGUI_EXPORT ImFontAtlasCustomRect* ImFontAtlasCustomRect_ImFontAtlasCustomRect_Nil() { return IM_NEW(ImFontAtlasCustomRect)(ImFontAtlasCustomRect()); }
PYLON_IMGUI_EXPORT ImFontConfig* ImFontConfig_ImFontConfig_Nil() { return IM_NEW(ImFontConfig)(ImFontConfig()); }
PYLON_IMGUI_EXPORT ImFontGlyphRangesBuilder* ImFontGlyphRangesBuilder_ImFontGlyphRangesBuilder_Nil() { return IM_NEW(ImFontGlyphRangesBuilder)(ImFontGlyphRangesBuilder()); }
PYLON_IMGUI_EXPORT ImGuiInputTextCallbackData* ImGuiInputTextCallbackData_ImGuiInputTextCallbackData_Nil() { return IM_NEW(ImGuiInputTextCallbackData)(ImGuiInputTextCallbackData()); }
PYLON_IMGUI_EXPORT ImGuiIO* ImGuiIO_ImGuiIO_Nil() { return IM_NEW(ImGuiIO)(ImGuiIO()); }
PYLON_IMGUI_EXPORT ImGuiListClipper* ImGuiListClipper_ImGuiListClipper_Nil() { return IM_NEW(ImGuiListClipper)(ImGuiListClipper()); }
PYLON_IMGUI_EXPORT ImGuiOnceUponAFrame* ImGuiOnceUponAFrame_ImGuiOnceUponAFrame_Nil() { return IM_NEW(ImGuiOnceUponAFrame)(ImGuiOnceUponAFrame()); }
PYLON_IMGUI_EXPORT ImGuiPayload* ImGuiPayload_ImGuiPayload_Nil() { return IM_NEW(ImGuiPayload)(ImGuiPayload()); }
PYLON_IMGUI_EXPORT ImGuiPlatformImeData* ImGuiPlatformImeData_ImGuiPlatformImeData_Nil() { return IM_NEW(ImGuiPlatformImeData)(ImGuiPlatformImeData()); }
PYLON_IMGUI_EXPORT ImGuiPlatformIO* ImGuiPlatformIO_ImGuiPlatformIO_Nil() { return IM_NEW(ImGuiPlatformIO)(ImGuiPlatformIO()); }
PYLON_IMGUI_EXPORT ImGuiSelectionBasicStorage* ImGuiSelectionBasicStorage_ImGuiSelectionBasicStorage_Nil() { return IM_NEW(ImGuiSelectionBasicStorage)(ImGuiSelectionBasicStorage()); }
PYLON_IMGUI_EXPORT ImGuiSelectionExternalStorage* ImGuiSelectionExternalStorage_ImGuiSelectionExternalStorage_Nil() { return IM_NEW(ImGuiSelectionExternalStorage)(ImGuiSelectionExternalStorage()); }
PYLON_IMGUI_EXPORT ImGuiStoragePair* ImGuiStoragePair_ImGuiStoragePair_Int(ImGuiID _key, int _val) { return IM_NEW(ImGuiStoragePair)(ImGuiStoragePair(_key, _val)); }
PYLON_IMGUI_EXPORT ImGuiStoragePair* ImGuiStoragePair_ImGuiStoragePair_Float(ImGuiID _key, float _val) { return IM_NEW(ImGuiStoragePair)(ImGuiStoragePair(_key, _val)); }
PYLON_IMGUI_EXPORT ImGuiStoragePair* ImGuiStoragePair_ImGuiStoragePair_Ptr(ImGuiID _key, void* _val) { return IM_NEW(ImGuiStoragePair)(ImGuiStoragePair(_key, _val)); }
PYLON_IMGUI_EXPORT ImGuiStyle* ImGuiStyle_ImGuiStyle_Nil() { return IM_NEW(ImGuiStyle)(ImGuiStyle()); }
PYLON_IMGUI_EXPORT ImGuiTableColumnSortSpecs* ImGuiTableColumnSortSpecs_ImGuiTableColumnSortSpecs_Nil() { return IM_NEW(ImGuiTableColumnSortSpecs)(ImGuiTableColumnSortSpecs()); }
PYLON_IMGUI_EXPORT ImGuiTableSortSpecs* ImGuiTableSortSpecs_ImGuiTableSortSpecs_Nil() { return IM_NEW(ImGuiTableSortSpecs)(ImGuiTableSortSpecs()); }
PYLON_IMGUI_EXPORT ImGuiTextBuffer* ImGuiTextBuffer_ImGuiTextBuffer_Nil() { return IM_NEW(ImGuiTextBuffer)(ImGuiTextBuffer()); }
PYLON_IMGUI_EXPORT ImGuiTextFilter* ImGuiTextFilter_ImGuiTextFilter_Nil() { return IM_NEW(ImGuiTextFilter)(ImGuiTextFilter()); }
PYLON_IMGUI_EXPORT ImGuiViewport* ImGuiViewport_ImGuiViewport_Nil() { return IM_NEW(ImGuiViewport)(ImGuiViewport()); }
PYLON_IMGUI_EXPORT void ImVec2_destroy(ImVec2* self) { IM_DELETE(self); }
PYLON_IMGUI_EXPORT void ImVec4_destroy(ImVec4* self) { IM_DELETE(self); }
PYLON_IMGUI_EXPORT void ImColor_destroy(ImColor* self) { IM_DELETE(self); }
PYLON_IMGUI_EXPORT void ImDrawCmd_destroy(ImDrawCmd* self) { IM_DELETE(self); }
PYLON_IMGUI_EXPORT void ImDrawData_destroy(ImDrawData* self) { IM_DELETE(self); }
PYLON_IMGUI_EXPORT void ImDrawListSplitter_destroy(ImDrawListSplitter* self) { IM_DELETE(self); }
PYLON_IMGUI_EXPORT void ImFont_destroy(ImFont* self) { IM_DELETE(self); }
PYLON_IMGUI_EXPORT void ImFontAtlas_destroy(ImFontAtlas* self) { IM_DELETE(self); }
PYLON_IMGUI_EXPORT void ImFontAtlasCustomRect_destroy(ImFontAtlasCustomRect* self) { IM_DELETE(self); }
PYLON_IMGUI_EXPORT void ImFontConfig_destroy(ImFontConfig* self) { IM_DELETE(self); }
PYLON_IMGUI_EXPORT void ImFontGlyphRangesBuilder_destroy(ImFontGlyphRangesBuilder* self) { IM_DELETE(self); }
PYLON_IMGUI_EXPORT void ImGuiInputTextCallbackData_destroy(ImGuiInputTextCallbackData* self) { IM_DELETE(self); }
PYLON_IMGUI_EXPORT void ImGuiIO_destroy(ImGuiIO* self) { IM_DELETE(self); }
PYLON_IMGUI_EXPORT void ImGuiListClipper_destroy(ImGuiListClipper* self) { IM_DELETE(self); }
PYLON_IMGUI_EXPORT void ImGuiOnceUponAFrame_destroy(ImGuiOnceUponAFrame* self) { IM_DELETE(self); }
PYLON_IMGUI_EXPORT void ImGuiPayload_destroy(ImGuiPayload* self) { IM_DELETE(self); }
PYLON_IMGUI_EXPORT void ImGuiPlatformImeData_destroy(ImGuiPlatformImeData* self) { IM_DELETE(self); }
PYLON_IMGUI_EXPORT void ImGuiPlatformIO_destroy(ImGuiPlatformIO* self) { IM_DELETE(self); }
PYLON_IMGUI_EXPORT void ImGuiSelectionBasicStorage_destroy(ImGuiSelectionBasicStorage* self) { IM_DELETE(self); }
PYLON_IMGUI_EXPORT void ImGuiSelectionExternalStorage_destroy(ImGuiSelectionExternalStorage* self) { IM_DELETE(self); }
PYLON_IMGUI_EXPORT void ImGuiStoragePair_destroy(ImGuiStoragePair* self) { IM_DELETE(self); }
PYLON_IMGUI_EXPORT void ImGuiStyle_destroy(ImGuiStyle* self) { IM_DELETE(self); }
PYLON_IMGUI_EXPORT void ImGuiTableColumnSortSpecs_destroy(ImGuiTableColumnSortSpecs* self) { IM_DELETE(self); }
PYLON_IMGUI_EXPORT void ImGuiTableSortSpecs_destroy(ImGuiTableSortSpecs* self) { IM_DELETE(self); }
PYLON_IMGUI_EXPORT void ImGuiTextBuffer_destroy(ImGuiTextBuffer* self) { IM_DELETE(self); }
PYLON_IMGUI_EXPORT void ImGuiTextFilter_destroy(ImGuiTextFilter* self) { IM_DELETE(self); }
PYLON_IMGUI_EXPORT void ImGuiViewport_destroy(ImGuiViewport* self) { IM_DELETE(self); }

// Vulkan texture upload/destroy — Pylon host integration, not part of imgui API.
PYLON_IMGUI_EXPORT uint64_t igCreateTexture(const uint8_t* rgba, int32_t width, int32_t height)
{
    if (!rgba || width <= 0 || height <= 0 || !VulkanTexture::IsReady()) return 0;
    return static_cast<uint64_t>(VulkanTexture::CreateTexture(rgba, width, height));
}

PYLON_IMGUI_EXPORT void igDestroyTexture(uint64_t id)
{
    VulkanTexture::DestroyTexture(static_cast<ImTextureID>(id));
}
// ===================== variadic text functions (Pylon text-safe) =====================
PYLON_IMGUI_EXPORT void igText(const char* fmt) { ImGui::TextUnformatted(fmt); }
PYLON_IMGUI_EXPORT void igTextColored(ImVec4 col, const char* fmt) { ImGui::TextUnformatted(fmt); }
PYLON_IMGUI_EXPORT void igTextDisabled(const char* fmt) { ImGui::TextUnformatted(fmt); }
PYLON_IMGUI_EXPORT void igTextWrapped(const char* fmt) { ImGui::TextWrapped(fmt); }
PYLON_IMGUI_EXPORT void igLabelText(const char* label, const char* fmt) { ImGui::LabelText(label, fmt); }
PYLON_IMGUI_EXPORT void igBulletText(const char* fmt) { ImGui::BulletText(fmt); }
PYLON_IMGUI_EXPORT void igSetTooltip(const char* fmt) { ImGui::SetTooltip(fmt); }
PYLON_IMGUI_EXPORT void igDebugLog(const char* fmt) { ImGui::DebugLog(fmt); }

}  // namespace pylon::imgui