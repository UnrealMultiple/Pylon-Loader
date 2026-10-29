using System;
using System.Runtime.InteropServices;

namespace Pylon.ImGui;

public static unsafe class ImGuiNative
{
    private const string Lib = "pylon";

    // ---- 输入（框架驱动调；坐标/状态由 C# 侧换算）----
    [DllImport(Lib, EntryPoint = "igAddMousePosEvent")] public static extern void igAddMousePosEvent(float x, float y);
    [DllImport(Lib, EntryPoint = "igAddMouseButtonEvent")] public static extern void igAddMouseButtonEvent(int button, int down);
    [DllImport(Lib, EntryPoint = "igAddMouseWheelEvent")] public static extern void igAddMouseWheelEvent(float wx, float wy);
    [DllImport(Lib, EntryPoint = "igAddInputCharactersUTF8")] public static extern void igAddInputCharactersUTF8(string utf8);
    [DllImport(Lib, EntryPoint = "igAddKeyEvent")] public static extern void igAddKeyEvent(int key, int down);
    [DllImport(Lib, EntryPoint = "igGetIO_WantTextInput")] public static extern int igGetIO_WantTextInput();
    [DllImport(Lib, EntryPoint = "igGetIO_WantCaptureMouse")] public static extern int igGetIO_WantCaptureMouse();
    [DllImport(Lib, EntryPoint = "igGetIO_MouseWheel")] public static extern float igGetIO_MouseWheel();
    [DllImport(Lib, EntryPoint = "igGetStyleWindowRounding")] public static extern float igGetStyleWindowRounding();
    [DllImport(Lib, EntryPoint = "igIsWindowMoving")] public static extern int igIsWindowMoving();
    [DllImport(Lib, EntryPoint = "igClearActiveID")] public static extern void igClearActiveID();
    [DllImport(Lib, EntryPoint = "igSetKeyboardActive")] public static extern void igSetKeyboardActive(int active);

    // ---- 窗口 ----
    [DllImport(Lib, EntryPoint = "igBegin")] public static extern byte igBegin(byte* name, int* pOpen, int flags);
    [DllImport(Lib, EntryPoint = "igEnd")] public static extern void igEnd();
    [DllImport(Lib, EntryPoint = "igBeginChild_Str")] public static extern byte igBeginChild(byte* strId, float w, float h, int childFlags, int windowFlags);
    [DllImport(Lib, EntryPoint = "igEndChild")] public static extern void igEndChild();
    [DllImport(Lib, EntryPoint = "igSetNextWindowPos")] public static extern void igSetNextWindowPos(float x, float y, int cond);
    [DllImport(Lib, EntryPoint = "igSetNextWindowBgAlpha")] public static extern void igSetNextWindowBgAlpha(float alpha);
    [DllImport(Lib, EntryPoint = "igSetNextWindowSize")] public static extern void igSetNextWindowSize(float w, float h, int cond);
    [DllImport(Lib, EntryPoint = "igGetWindowPos")] public static extern void igGetWindowPos(float* x, float* y);
    [DllImport(Lib, EntryPoint = "igSetWindowPos")] public static extern void igSetWindowPos(float x, float y);
    [DllImport(Lib, EntryPoint = "igGetWindowSize")] public static extern void igGetWindowSize(float* w, float* h);
    [DllImport(Lib, EntryPoint = "igGetScrollX")] public static extern float igGetScrollX();
    [DllImport(Lib, EntryPoint = "igGetScrollY")] public static extern float igGetScrollY();
    [DllImport(Lib, EntryPoint = "igSetScrollHereY")] public static extern void igSetScrollHereY(float ratio);
    [DllImport(Lib, EntryPoint = "igSetScrollY")] public static extern void igSetScrollY(float y);
    [DllImport(Lib, EntryPoint = "igIsWindowAppearing")] public static extern byte igIsWindowAppearing();
    [DllImport(Lib, EntryPoint = "igIsWindowCollapsed")] public static extern byte igIsWindowCollapsed();

    // ---- 布局 / 光标 ----
    [DllImport(Lib, EntryPoint = "igSameLine")] public static extern void igSameLine(float offsetFromStartX, float spacing);
    [DllImport(Lib, EntryPoint = "igNewLine")] public static extern void igNewLine();
    [DllImport(Lib, EntryPoint = "igSpacing")] public static extern void igSpacing();
    [DllImport(Lib, EntryPoint = "igDummy")] public static extern void igDummy(float w, float h);
    [DllImport(Lib, EntryPoint = "igSeparator")] public static extern void igSeparator();
    [DllImport(Lib, EntryPoint = "igSeparatorText")] public static extern void igSeparatorText(byte* label);
    [DllImport(Lib, EntryPoint = "igIndent")] public static extern void igIndent(float indentW);
    [DllImport(Lib, EntryPoint = "igUnindent")] public static extern void igUnindent(float indentW);
    [DllImport(Lib, EntryPoint = "igBeginGroup")] public static extern void igBeginGroup();
    [DllImport(Lib, EntryPoint = "igEndGroup")] public static extern void igEndGroup();
    [DllImport(Lib, EntryPoint = "igAlignTextToFramePadding")] public static extern void igAlignTextToFramePadding();
    [DllImport(Lib, EntryPoint = "igGetCursorPosX")] public static extern float igGetCursorPosX();
    [DllImport(Lib, EntryPoint = "igGetCursorPosY")] public static extern float igGetCursorPosY();
    [DllImport(Lib, EntryPoint = "igSetCursorPos")] public static extern void igSetCursorPos(float x, float y);
    [DllImport(Lib, EntryPoint = "igGetContentRegionAvail")] public static extern void igGetContentRegionAvail(float* w, float* h);
    [DllImport(Lib, EntryPoint = "igGetFrameHeight")] public static extern float igGetFrameHeight();
    [DllImport(Lib, EntryPoint = "igGetFrameHeightWithSpacing")] public static extern float igGetFrameHeightWithSpacing();
    [DllImport(Lib, EntryPoint = "igGetTextLineHeight")] public static extern float igGetTextLineHeight();

    // ---- 文本 ----
    [DllImport(Lib, EntryPoint = "igText")] public static extern void igText(byte* fmt);
    [DllImport(Lib, EntryPoint = "igTextColored")] public static extern void igTextColored(float r, float g, float b, float a, byte* fmt);
    [DllImport(Lib, EntryPoint = "igTextDisabled")] public static extern void igTextDisabled(byte* fmt);
    [DllImport(Lib, EntryPoint = "igTextWrapped")] public static extern void igTextWrapped(byte* fmt);
    [DllImport(Lib, EntryPoint = "igLabelText")] public static extern void igLabelText(byte* label, byte* fmt);
    [DllImport(Lib, EntryPoint = "igBulletText")] public static extern void igBulletText(byte* fmt);

    // ---- 按钮 / 选择 ----
    [DllImport(Lib, EntryPoint = "igButton")] public static extern byte igButton(byte* label, float w, float h);
    [DllImport(Lib, EntryPoint = "igSmallButton")] public static extern byte igSmallButton(byte* label);
    [DllImport(Lib, EntryPoint = "igInvisibleButton")] public static extern byte igInvisibleButton(byte* id, float w, float h, int flags);
    [DllImport(Lib, EntryPoint = "igGetCursorScreenPos")] public static extern void igGetCursorScreenPos(float* x, float* y);
    [DllImport(Lib, EntryPoint = "igCheckbox")] public static extern byte igCheckbox(byte* label, int* v);
    [DllImport(Lib, EntryPoint = "igRadioButton")] public static extern byte igRadioButton(byte* label, int* v, int vButton);
    [DllImport(Lib, EntryPoint = "igProgressBar")] public static extern void igProgressBar(float fraction, float w, float h, byte* overlay);
    [DllImport(Lib, EntryPoint = "igBullet")] public static extern void igBullet();

    // ---- 数值输入 / 拖动 / 滑块 ----
    [DllImport(Lib, EntryPoint = "igInputText")] public static extern byte igInputText(byte* label, byte* buf, int bufSize);
    [DllImport(Lib, EntryPoint = "igInputInt")] public static extern byte igInputInt(byte* label, int* v, int step, int stepFast);
    [DllImport(Lib, EntryPoint = "igInputFloat")] public static extern byte igInputFloat(byte* label, float* v, float step, float stepFast, byte* format);
    [DllImport(Lib, EntryPoint = "igDragFloat")] public static extern byte igDragFloat(byte* label, float* v, float speed, float vMin, float vMax, byte* format);
    [DllImport(Lib, EntryPoint = "igDragInt")] public static extern byte igDragInt(byte* label, int* v, float speed, int vMin, int vMax, byte* format);
    [DllImport(Lib, EntryPoint = "igSliderFloat")] public static extern byte igSliderFloat(byte* label, float* v, float vMin, float vMax, byte* format);
    [DllImport(Lib, EntryPoint = "igSliderInt")] public static extern byte igSliderInt(byte* label, int* v, int vMin, int vMax, byte* format);

    // ---- 下拉 / 列表 ----
    [DllImport(Lib, EntryPoint = "igCombo")] public static extern byte igCombo(byte* label, int* current, byte** items, int itemsCount);
    [DllImport(Lib, EntryPoint = "igListBox")] public static extern byte igListBox(byte* label, int* current, byte** items, int itemsCount, int heightInItems);

    // ---- 颜色 ----
    [DllImport(Lib, EntryPoint = "igColorEdit4")] public static extern byte igColorEdit4(byte* label, float* col, int flags);
    [DllImport(Lib, EntryPoint = "igColorPicker4")] public static extern byte igColorPicker4(byte* label, float* col, int flags);

    // ---- 树 / 折叠 ----
    [DllImport(Lib, EntryPoint = "igTreeNode")] public static extern byte igTreeNode(byte* label);
    [DllImport(Lib, EntryPoint = "igCollapsingHeader")] public static extern byte igCollapsingHeader(byte* label);
    [DllImport(Lib, EntryPoint = "igTreePush")] public static extern void igTreePush(byte* strId);
    [DllImport(Lib, EntryPoint = "igTreePop")] public static extern void igTreePop();

    // ---- 表格 ----
    [DllImport(Lib, EntryPoint = "igBeginTable")] public static extern byte igBeginTable(byte* strId, int column, int flags);
    [DllImport(Lib, EntryPoint = "igTableSetupColumn")] public static extern void igTableSetupColumn(byte* label, int flags, float initWidth, int column);
    [DllImport(Lib, EntryPoint = "igTableHeadersRow")] public static extern void igTableHeadersRow();
    [DllImport(Lib, EntryPoint = "igTableNextRow")] public static extern void igTableNextRow(float minRowHeight);
    [DllImport(Lib, EntryPoint = "igTableNextColumn")] public static extern byte igTableNextColumn();
    [DllImport(Lib, EntryPoint = "igEndTable")] public static extern void igEndTable();

    // ---- 标签页 ----
    [DllImport(Lib, EntryPoint = "igBeginTabBar")] public static extern byte igBeginTabBar(byte* strId, int flags);
    [DllImport(Lib, EntryPoint = "igBeginTabItem")] public static extern byte igBeginTabItem(byte* label, int* pOpen, int flags);
    [DllImport(Lib, EntryPoint = "igEndTabItem")] public static extern void igEndTabItem();
    [DllImport(Lib, EntryPoint = "igEndTabBar")] public static extern void igEndTabBar();

    // ---- 弹窗 / 菜单 / 工具提示 ----
    [DllImport(Lib, EntryPoint = "igOpenPopup")] public static extern void igOpenPopup(byte* strId, int flags);
    [DllImport(Lib, EntryPoint = "igBeginPopupModal")] public static extern byte igBeginPopupModal(byte* name, int* pOpen, int flags);
    [DllImport(Lib, EntryPoint = "igEndPopup")] public static extern void igEndPopup();
    [DllImport(Lib, EntryPoint = "igBeginPopup")] public static extern byte igBeginPopup(byte* strId, int flags);
    [DllImport(Lib, EntryPoint = "igCloseCurrentPopup")] public static extern void igCloseCurrentPopup();
    [DllImport(Lib, EntryPoint = "igBeginPopupContextItem")] public static extern byte igBeginPopupContextItem(byte* strId, int flags);
    [DllImport(Lib, EntryPoint = "igSetTooltip")] public static extern void igSetTooltip(byte* fmt);

    // ---- ID / 样式栈 ----
    [DllImport(Lib, EntryPoint = "igPushID")] public static extern void igPushID(int id);
    [DllImport(Lib, EntryPoint = "igPushIDStr")] public static extern void igPushIDStr(byte* strId);
    [DllImport(Lib, EntryPoint = "igPopID")] public static extern void igPopID();
    [DllImport(Lib, EntryPoint = "igSetNextItemWidth")] public static extern void igSetNextItemWidth(float itemWidth);
    [DllImport(Lib, EntryPoint = "igPushItemWidth")] public static extern void igPushItemWidth(float itemWidth);
    [DllImport(Lib, EntryPoint = "igPopItemWidth")] public static extern void igPopItemWidth();
    [DllImport(Lib, EntryPoint = "igPushStyleVar")] public static extern void igPushStyleVar(int idx, float v0, float v1);
    [DllImport(Lib, EntryPoint = "igPopStyleVar")] public static extern void igPopStyleVar(int count);
    [DllImport(Lib, EntryPoint = "igPushStyleColor")] public static extern void igPushStyleColor(int idx, float r, float g, float b, float a);
    [DllImport(Lib, EntryPoint = "igPopStyleColor")] public static extern void igPopStyleColor(int count);

    // ---- 禁用 ----
    [DllImport(Lib, EntryPoint = "igBeginDisabled")] public static extern void igBeginDisabled(int disabled);
    [DllImport(Lib, EntryPoint = "igEndDisabled")] public static extern void igEndDisabled();

    // ---- item 状态 ----
    [DllImport(Lib, EntryPoint = "igIsItemHovered")] public static extern byte igIsItemHovered(int flags);
    [DllImport(Lib, EntryPoint = "igIsItemActive")] public static extern byte igIsItemActive();
    [DllImport(Lib, EntryPoint = "igIsItemActivated")] public static extern byte igIsItemActivated();
    [DllImport(Lib, EntryPoint = "igIsItemDeactivated")] public static extern byte igIsItemDeactivated();
    [DllImport(Lib, EntryPoint = "igIsItemClicked")] public static extern byte igIsItemClicked(int mouseButton);
    [DllImport(Lib, EntryPoint = "igIsItemFocused")] public static extern byte igIsItemFocused();
    [DllImport(Lib, EntryPoint = "igIsItemVisible")] public static extern byte igIsItemVisible();
    [DllImport(Lib, EntryPoint = "igIsItemEdited")] public static extern byte igIsItemEdited();
    [DllImport(Lib, EntryPoint = "igIsAnyItemActive")] public static extern byte igIsAnyItemActive();
    [DllImport(Lib, EntryPoint = "igGetItemRectMin")] public static extern void igGetItemRectMin(float* x, float* y);
    [DllImport(Lib, EntryPoint = "igGetItemRectMax")] public static extern void igGetItemRectMax(float* x, float* y);

    // ---- 样式（ModernTheme 直接设置全局 style）----
    [DllImport(Lib, EntryPoint = "igSetStyleColor")] public static extern void igSetStyleColor(int col, float r, float g, float b, float a);
    [DllImport(Lib, EntryPoint = "igSetStyleVarFloat")] public static extern void igSetStyleVarFloat(int var, float v);
    [DllImport(Lib, EntryPoint = "igSetStyleVarVec2")] public static extern void igSetStyleVarVec2(int var, float x, float y);

    // ---- 绘制原语（GlassKit 动画/自绘控件用；dl 是 ImDrawList*）----
    [DllImport(Lib, EntryPoint = "igGetWindowDrawList")] public static extern IntPtr igGetWindowDrawList();
    [DllImport(Lib, EntryPoint = "igGetForegroundDrawList")] public static extern IntPtr igGetForegroundDrawList();
    [DllImport(Lib, EntryPoint = "igGetTime")] public static extern float igGetTime();
    [DllImport(Lib, EntryPoint = "igGetFontSize")] public static extern float igGetFontSize();
    [DllImport(Lib, EntryPoint = "igGetMousePos")] public static extern void igGetMousePos(ref float x, ref float y);
    [DllImport(Lib, EntryPoint = "igColor4")] public static extern uint igColor4(float r, float g, float b, float a);
    [DllImport(Lib, EntryPoint = "igDL_AddRectFilled")] public static extern void igDL_AddRectFilled(IntPtr dl, float x0, float y0, float x1, float y1, uint col, float rounding);
    [DllImport(Lib, EntryPoint = "igDL_AddRectFilledMultiColor")] public static extern void igDL_AddRectFilledMultiColor(IntPtr dl, float x0, float y0, float x1, float y1, uint colUL, uint colUR, uint colLR, uint colLL);
    [DllImport(Lib, EntryPoint = "igDL_AddRect")] public static extern void igDL_AddRect(IntPtr dl, float x0, float y0, float x1, float y1, uint col, float rounding, float thickness);
    [DllImport(Lib, EntryPoint = "igDL_AddLine")] public static extern void igDL_AddLine(IntPtr dl, float x0, float y0, float x1, float y1, uint col, float thickness);
    [DllImport(Lib, EntryPoint = "igDL_AddCircleFilled")] public static extern void igDL_AddCircleFilled(IntPtr dl, float cx, float cy, float r, uint col, int segments);
    [DllImport(Lib, EntryPoint = "igDL_AddCircle")] public static extern void igDL_AddCircle(IntPtr dl, float cx, float cy, float r, uint col, int segments, float thickness);
    [DllImport(Lib, EntryPoint = "igDL_AddEllipseFilled")] public static extern void igDL_AddEllipseFilled(IntPtr dl, float cx, float cy, float rx, float ry, uint col, float rotation, int segments);
    [DllImport(Lib, EntryPoint = "igDL_AddText")] public static extern void igDL_AddText(IntPtr dl, float x, float y, uint col, string utf8);
    [DllImport(Lib, EntryPoint = "igDL_AddTriangleFilled")] public static extern void igDL_AddTriangleFilled(IntPtr dl, float x0, float y0, float x1, float y1, float x2, float y2, uint col);
    [DllImport(Lib, EntryPoint = "igDL_AddPolyline")] public static extern void igDL_AddPolyline(IntPtr dl, float[] xy, int count, uint col, float thickness);
    [DllImport(Lib, EntryPoint = "igDL_AddRoundedGradientRectTop")] public static extern void igDL_AddRoundedGradientRectTop(IntPtr dl, float x0, float y0, float x1, float y1, float rounding, uint colTop, uint colBot);
    [DllImport(Lib, EntryPoint = "igDL_AddTextFont")] public static extern void igDL_AddTextFont(IntPtr dl, float x, float y, uint col, string utf8, float fontSize);
    [DllImport(Lib, EntryPoint = "igDL_AddRectFilledEx")] public static extern void igDL_AddRectFilledEx(IntPtr dl, float x0, float y0, float x1, float y1, uint col, float rounding, int flags);
    [DllImport(Lib, EntryPoint = "igDL_AddRectEx")] public static extern void igDL_AddRectEx(IntPtr dl, float x0, float y0, float x1, float y1, uint col, float rounding, int flags, float thickness);
    [DllImport(Lib, EntryPoint = "igDL_AddQuadFilled")] public static extern void igDL_AddQuadFilled(IntPtr dl, float x0, float y0, float x1, float y1, float x2, float y2, float x3, float y3, uint col);
    [DllImport(Lib, EntryPoint = "igDL_AddConvexPolyFilled")] public static extern void igDL_AddConvexPolyFilled(IntPtr dl, float[] xy, int count, uint col);
    [DllImport(Lib, EntryPoint = "igCalcTextSizeA")] public static extern void igCalcTextSizeA(float fontSize, string text, ref float outW, ref float outH);
    [DllImport(Lib, EntryPoint = "igSetCursorScreenPos")] public static extern void igSetCursorScreenPos(float x, float y);
    [DllImport(Lib, EntryPoint = "igGetWindowWidth")] public static extern float igGetWindowWidth();
    [DllImport(Lib, EntryPoint = "igGetWindowHeight")] public static extern float igGetWindowHeight();
    [DllImport(Lib, EntryPoint = "igIsMouseHoveringRect")] public static extern int igIsMouseHoveringRect(float x0, float y0, float x1, float y1, int allowWhenDisabled);
    [DllImport(Lib, EntryPoint = "igDL_PushClipRect")] public static extern void igDL_PushClipRect(IntPtr dl, float x0, float y0, float x1, float y1, int intersect);
    [DllImport(Lib, EntryPoint = "igDL_PopClipRect")] public static extern void igDL_PopClipRect(IntPtr dl);

    // ---- 用户纹理（Mod 上传图片；VulkanTexture 管理）----
    [DllImport(Lib, EntryPoint = "igCreateTexture")] public static extern ulong igCreateTexture(byte[] rgba, int width, int height);
    [DllImport(Lib, EntryPoint = "igDestroyTexture")] public static extern void igDestroyTexture(ulong id);
    [DllImport(Lib, EntryPoint = "igImage")] public static extern void igImage(ulong id, float w, float h, float uv0x, float uv0y, float uv1x, float uv1y);
    [DllImport(Lib, EntryPoint = "igDL_AddImage")] public static extern void igDL_AddImage(IntPtr dl, ulong id, float x0, float y0, float x1, float y1, uint tint);

    // ---- 工具 ----
    [DllImport(Lib, EntryPoint = "igCalcTextSize")] public static extern void igCalcTextSize(byte* text, float wrapWidth, float* outW, float* outH);
    [DllImport(Lib, EntryPoint = "igGetColorU32")] public static extern uint igGetColorU32(int idx);
    [DllImport(Lib, EntryPoint = "igGetID")] public static extern uint igGetID(byte* strId);
}