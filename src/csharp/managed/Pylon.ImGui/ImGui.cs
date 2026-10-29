using System;

namespace Pylon.ImGui;

/// <summary>ImGui 立即模式控件层（C++ 引擎渲染，这里只是 API）。</summary>
public static unsafe class ImGui
{
    // 键盘桥（框架侧）需要的"当前激活输入框内容"：InputText 激活时记录。
    private static string _activeInputBuffer = "";

    /// <summary>当前激活输入框的内容（InputText 激活时更新；键盘桥用作预填/同步基线）。</summary>
    public static string ActiveInputBuffer => _activeInputBuffer;

    // ===================================================================
    // 窗口
    // ===================================================================

    /// <summary>窗口体开始；返回 true 才有内容可画。open 进出（关窗置 false）。</summary>
    public static bool Begin(string name, ref bool open, int flags = 0)
    {
        var namePtr = Util.AllocUtf8(name);
        int o = open ? 1 : 0;
        try
        {
            bool visible = ImGuiNative.igBegin(namePtr, &o, flags) != 0;
            open = o != 0;
            return visible;
        }
        finally { Util.Free(namePtr); }
    }

    public static void End() => ImGuiNative.igEnd();

    /// <summary>子窗口（滚动区）。</summary>
    public static bool BeginChild(string id, float w = 0, float h = 0, int childFlags = 0, int windowFlags = 0)
    {
        var idPtr = Util.AllocUtf8(id);
        try { return ImGuiNative.igBeginChild(idPtr, w, h, childFlags, windowFlags) != 0; }
        finally { Util.Free(idPtr); }
    }

    public static void EndChild() => ImGuiNative.igEndChild();

    public static void SetNextWindowPos(float x, float y, int cond = 0) => ImGuiNative.igSetNextWindowPos(x, y, cond);
    public static void SetNextWindowSize(float w, float h, int cond = 0) => ImGuiNative.igSetNextWindowSize(w, h, cond);
    /// <summary>下一窗口背景透明度（0..1；悬浮球等无背景窗口用 0）。</summary>
    public static void SetNextWindowBgAlpha(float alpha) => ImGuiNative.igSetNextWindowBgAlpha(alpha);

    public static ImVec2 GetWindowPos() { float x = 0, y = 0; ImGuiNative.igGetWindowPos(&x, &y); return new ImVec2(x, y); }
    public static ImVec2 GetWindowSize() { float w = 0, h = 0; ImGuiNative.igGetWindowSize(&w, &h); return new ImVec2(w, h); }

    /// <summary>设置当前窗口位置（悬浮球拖动用；必须在窗口体内调用）。</summary>
    public static void SetWindowPos(float x, float y) => ImGuiNative.igSetWindowPos(x, y);

    public static float GetScrollX() => ImGuiNative.igGetScrollX();
    public static float GetScrollY() => ImGuiNative.igGetScrollY();
    public static void SetScrollHereY(float ratio = 0.5f) => ImGuiNative.igSetScrollHereY(ratio);
    public static void SetScrollY(float y) => ImGuiNative.igSetScrollY(y);
    public static bool IsWindowAppearing() => ImGuiNative.igIsWindowAppearing() != 0;
    public static bool IsWindowCollapsed() => ImGuiNative.igIsWindowCollapsed() != 0;

    public static void SameLine(float offsetFromStartX = 0, float spacing = -1) => ImGuiNative.igSameLine(offsetFromStartX, spacing);
    public static void NewLine() => ImGuiNative.igNewLine();
    public static void Spacing() => ImGuiNative.igSpacing();
    public static void Dummy(float w, float h) => ImGuiNative.igDummy(w, h);
    public static void Separator() => ImGuiNative.igSeparator();
    public static void SeparatorText(string label)
    {
        var labelPtr = Util.AllocUtf8(label);
        try { ImGuiNative.igSeparatorText(labelPtr); }
        finally { Util.Free(labelPtr); }
    }
    public static void Indent(float indentW = 0) => ImGuiNative.igIndent(indentW);
    public static void Unindent(float indentW = 0) => ImGuiNative.igUnindent(indentW);
    public static void BeginGroup() => ImGuiNative.igBeginGroup();
    public static void EndGroup() => ImGuiNative.igEndGroup();
    public static void AlignTextToFramePadding() => ImGuiNative.igAlignTextToFramePadding();

    public static float GetCursorPosX() => ImGuiNative.igGetCursorPosX();
    public static float GetCursorPosY() => ImGuiNative.igGetCursorPosY();
    public static void SetCursorPos(float x, float y) => ImGuiNative.igSetCursorPos(x, y);

    /// <summary>当前光标（下一个 item 左上角）的屏幕坐标——自绘控件定位基础。</summary>
    public static ImVec2 GetCursorScreenPos()
    {
        float x = 0, y = 0;
        ImGuiNative.igGetCursorScreenPos(&x, &y);
        return new ImVec2(x, y);
    }

    /// <summary>设置光标屏幕坐标（自绘控件/InvisibleButton 定位用）。</summary>
    public static void SetCursorScreenPos(ImVec2 pos) => ImGuiNative.igSetCursorScreenPos(pos.X, pos.Y);
    public static void SetCursorScreenPos(float x, float y) => ImGuiNative.igSetCursorScreenPos(x, y);

    /// <summary>鼠标位置（覆盖层坐标）。</summary>
    public static ImVec2 MousePos
    {
        get
        {
            float x = 0, y = 0;
            ImGuiNative.igGetMousePos(ref x, ref y);
            return new ImVec2(x, y);
        }
    }

    public static ImVec2 GetContentRegionAvail() { float w = 0, h = 0; ImGuiNative.igGetContentRegionAvail(&w, &h); return new ImVec2(w, h); }
    public static float GetFrameHeight() => ImGuiNative.igGetFrameHeight();
    public static float GetFrameHeightWithSpacing() => ImGuiNative.igGetFrameHeightWithSpacing();
    public static float GetTextLineHeight() => ImGuiNative.igGetTextLineHeight();

    // ===================================================================
    // 文本（内容一律当普通文本显示）
    // ===================================================================

    public static void Text(string fmt)
    {
        var fmtPtr = Util.AllocUtf8(fmt);
        try { ImGuiNative.igText(fmtPtr); }
        finally { Util.Free(fmtPtr); }
    }
    public static void TextColored(ImVec4 col, string fmt)
    {
        var fmtPtr = Util.AllocUtf8(fmt);
        try { ImGuiNative.igTextColored(col.X, col.Y, col.Z, col.W, fmtPtr); }
        finally { Util.Free(fmtPtr); }
    }
    public static void TextDisabled(string fmt)
    {
        var fmtPtr = Util.AllocUtf8(fmt);
        try { ImGuiNative.igTextDisabled(fmtPtr); }
        finally { Util.Free(fmtPtr); }
    }
    public static void TextWrapped(string fmt)
    {
        var fmtPtr = Util.AllocUtf8(fmt);
        try { ImGuiNative.igTextWrapped(fmtPtr); }
        finally { Util.Free(fmtPtr); }
    }
    public static void LabelText(string label, string fmt)
    {
        var labelPtr = Util.AllocUtf8(label);
        var fmtPtr = Util.AllocUtf8(fmt);
        try { ImGuiNative.igLabelText(labelPtr, fmtPtr); }
        finally { Util.Free(fmtPtr); Util.Free(labelPtr); }
    }
    public static void BulletText(string fmt)
    {
        var fmtPtr = Util.AllocUtf8(fmt);
        try { ImGuiNative.igBulletText(fmtPtr); }
        finally { Util.Free(fmtPtr); }
    }

    // ===================================================================
    // 按钮 / 选择
    // ===================================================================

    public static bool Button(string label, ImVec2 size = default)
    {
        var labelPtr = Util.AllocUtf8(label);
        try { return ImGuiNative.igButton(labelPtr, size.X, size.Y) != 0; }
        finally { Util.Free(labelPtr); }
    }
    public static bool SmallButton(string label)
    {
        var labelPtr = Util.AllocUtf8(label);
        try { return ImGuiNative.igSmallButton(labelPtr) != 0; }
        finally { Util.Free(labelPtr); }
    }

    /// <summary>隐形按钮（拿点击区域，自己画内容）；size 必须 > 0。Glass 自绘控件的地基。</summary>
    public static bool InvisibleButton(string id, float w, float h, int flags = 0)
    {
        var idPtr = Util.AllocUtf8(id);
        try { return ImGuiNative.igInvisibleButton(idPtr, w, h, flags) != 0; }
        finally { Util.Free(idPtr); }
    }
    public static bool Checkbox(string label, ref bool value)
    {
        var labelPtr = Util.AllocUtf8(label);
        int v = value ? 1 : 0;
        try
        {
            bool changed = ImGuiNative.igCheckbox(labelPtr, &v) != 0;
            value = v != 0;
            return changed;
        }
        finally { Util.Free(labelPtr); }
    }
    public static bool RadioButton(string label, ref int value, int thisValue)
    {
        var labelPtr = Util.AllocUtf8(label);
        int v = value;
        try
        {
            bool chosen = ImGuiNative.igRadioButton(labelPtr, &v, thisValue) != 0;
            value = v;
            return chosen;
        }
        finally { Util.Free(labelPtr); }
    }
    public static void ProgressBar(float fraction, ImVec2 size = default, string overlay = null)
    {
        var overlayPtr = Util.AllocUtf8(overlay);
        try { ImGuiNative.igProgressBar(fraction, size.X, size.Y, overlayPtr); }
        finally { Util.Free(overlayPtr); }
    }
    public static void Bullet() => ImGuiNative.igBullet();

    // ===================================================================
    // 数值输入 / 拖动 / 滑块
    // ===================================================================

    /// <summary>文本输入框（ref string 版本，自动 UTF-8 编解码）。</summary>
    public static bool InputText(string label, ref string buffer, int capacity = 256)
    {
        if (buffer == null) buffer = string.Empty;
        var labelPtr = Util.AllocUtf8(label);
        var ptr = System.Runtime.InteropServices.Marshal.AllocCoTaskMem(capacity);
        try
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(buffer);
            var fill = Math.Min(bytes.Length, capacity - 1);
            System.Runtime.InteropServices.Marshal.Copy(bytes, 0, ptr, fill);
            System.Runtime.InteropServices.Marshal.WriteByte(ptr, fill, 0);
            bool edited = ImGuiNative.igInputText(labelPtr, (byte*)ptr, capacity) != 0;
            buffer = Util.ReadUtf8((byte*)ptr);
            // 激活中的输入框 = 键盘桥的编辑视图（预填与增量 diff 基线用）
            if (IsItemActive()) _activeInputBuffer = buffer;
            return edited;
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeCoTaskMem(ptr);
            Util.Free(labelPtr);
        }
    }
    public static bool InputInt(string label, ref int value, int step = 1, int stepFast = 100)
    {
        var labelPtr = Util.AllocUtf8(label);
        int v = value;
        try
        {
            bool changed = ImGuiNative.igInputInt(labelPtr, &v, step, stepFast) != 0;
            value = v;
            return changed;
        }
        finally { Util.Free(labelPtr); }
    }
    public static bool InputFloat(string label, ref float value, float step = 0.1f, float stepFast = 1f, string format = "%.3f")
    {
        var labelPtr = Util.AllocUtf8(label);
        var fmtPtr = Util.AllocUtf8(format);
        float v = value;
        try
        {
            bool changed = ImGuiNative.igInputFloat(labelPtr, &v, step, stepFast, fmtPtr) != 0;
            value = v;
            return changed;
        }
        finally { Util.Free(fmtPtr); Util.Free(labelPtr); }
    }
    public static bool DragFloat(string label, ref float value, float speed = 1f, float vMin = 0, float vMax = 0, string format = "%.3f")
    {
        var labelPtr = Util.AllocUtf8(label);
        var fmtPtr = Util.AllocUtf8(format);
        float v = value;
        try
        {
            bool changed = ImGuiNative.igDragFloat(labelPtr, &v, speed, vMin, vMax, fmtPtr) != 0;
            value = v;
            return changed;
        }
        finally { Util.Free(fmtPtr); Util.Free(labelPtr); }
    }
    public static bool DragInt(string label, ref int value, float speed = 1f, int vMin = 0, int vMax = 0, string format = "%d")
    {
        var labelPtr = Util.AllocUtf8(label);
        var fmtPtr = Util.AllocUtf8(format);
        int v = value;
        try
        {
            bool changed = ImGuiNative.igDragInt(labelPtr, &v, speed, vMin, vMax, fmtPtr) != 0;
            value = v;
            return changed;
        }
        finally { Util.Free(fmtPtr); Util.Free(labelPtr); }
    }
    public static bool SliderFloat(string label, ref float value, float vMin, float vMax, string format = "%.3f")
    {
        var labelPtr = Util.AllocUtf8(label);
        var fmtPtr = Util.AllocUtf8(format);
        float v = value;
        try
        {
            bool changed = ImGuiNative.igSliderFloat(labelPtr, &v, vMin, vMax, fmtPtr) != 0;
            value = v;
            return changed;
        }
        finally { Util.Free(fmtPtr); Util.Free(labelPtr); }
    }
    public static bool SliderInt(string label, ref int value, int vMin, int vMax, string format = "%d")
    {
        var labelPtr = Util.AllocUtf8(label);
        var fmtPtr = Util.AllocUtf8(format);
        int v = value;
        try
        {
            bool changed = ImGuiNative.igSliderInt(labelPtr, &v, vMin, vMax, fmtPtr) != 0;
            value = v;
            return changed;
        }
        finally { Util.Free(fmtPtr); Util.Free(labelPtr); }
    }

    // ===================================================================
    // 下拉 / 列表
    // ===================================================================

    public static bool Combo(string label, ref int current, string[] items)
    {
        if (items == null || items.Length == 0) return false;
        var labelPtr = Util.AllocUtf8(label);
        var ptrs = Util.AllocItems(items, out var allocs);
        int c = current;
        try
        {
            bool changed = ImGuiNative.igCombo(labelPtr, &c, ptrs, items.Length) != 0;
            current = c;
            return changed;
        }
        finally { Util.FreeAll(ptrs, allocs); Util.Free(labelPtr); }
    }

    public static bool ListBox(string label, ref int current, string[] items, int heightInItems = -1)
    {
        if (items == null || items.Length == 0) return false;
        var labelPtr = Util.AllocUtf8(label);
        var ptrs = Util.AllocItems(items, out var allocs);
        int c = current;
        try
        {
            bool changed = ImGuiNative.igListBox(labelPtr, &c, ptrs, items.Length, heightInItems) != 0;
            current = c;
            return changed;
        }
        finally { Util.FreeAll(ptrs, allocs); Util.Free(labelPtr); }
    }

    // ===================================================================
    // 颜色 / 树 / 折叠
    // ===================================================================

    public static bool ColorEdit4(string label, ref ImVec4 color, int flags = 0)
    {
        var labelPtr = Util.AllocUtf8(label);
        var col = stackalloc float[4] { color.X, color.Y, color.Z, color.W };
        try
        {
            bool changed = ImGuiNative.igColorEdit4(labelPtr, col, flags) != 0;
            color = new ImVec4(col[0], col[1], col[2], col[3]);
            return changed;
        }
        finally { Util.Free(labelPtr); }
    }

    public static bool TreeNode(string label)
    {
        var labelPtr = Util.AllocUtf8(label);
        try { return ImGuiNative.igTreeNode(labelPtr) != 0; }
        finally { Util.Free(labelPtr); }
    }
    public static bool CollapsingHeader(string label)
    {
        var labelPtr = Util.AllocUtf8(label);
        try { return ImGuiNative.igCollapsingHeader(labelPtr) != 0; }
        finally { Util.Free(labelPtr); }
    }
    public static void TreePush(string id)
    {
        var idPtr = Util.AllocUtf8(id);
        try { ImGuiNative.igTreePush(idPtr); }
        finally { Util.Free(idPtr); }
    }
    public static void TreePop() => ImGuiNative.igTreePop();

    // ===================================================================
    // 表格 / 标签页
    // ===================================================================

    public static bool BeginTable(string id, int column, int flags = 0)
    {
        var idPtr = Util.AllocUtf8(id);
        try { return ImGuiNative.igBeginTable(idPtr, column, flags) != 0; }
        finally { Util.Free(idPtr); }
    }
    public static void TableSetupColumn(string label, int flags = 0, float initWidth = 0, int column = -1)
    {
        var labelPtr = Util.AllocUtf8(label);
        try { ImGuiNative.igTableSetupColumn(labelPtr, flags, initWidth, column); }
        finally { Util.Free(labelPtr); }
    }
    public static void TableHeadersRow() => ImGuiNative.igTableHeadersRow();
    public static void TableNextRow(float minRowHeight = 0) => ImGuiNative.igTableNextRow(minRowHeight);
    public static bool TableNextColumn() => ImGuiNative.igTableNextColumn() != 0;
    public static void EndTable() => ImGuiNative.igEndTable();

    public static bool BeginTabBar(string id, int flags = 0)
    {
        var idPtr = Util.AllocUtf8(id);
        try { return ImGuiNative.igBeginTabBar(idPtr, flags) != 0; }
        finally { Util.Free(idPtr); }
    }
    public static bool BeginTabItem(string label, ref bool open, int flags = 0)
    {
        var labelPtr = Util.AllocUtf8(label);
        int o = open ? 1 : 0;
        try
        {
            bool visible = ImGuiNative.igBeginTabItem(labelPtr, &o, flags) != 0;
            open = o != 0;
            return visible;
        }
        finally { Util.Free(labelPtr); }
    }
    public static void EndTabItem() => ImGuiNative.igEndTabItem();
    public static void EndTabBar() => ImGuiNative.igEndTabBar();

    // ===================================================================
    // 弹窗 / 菜单 / 工具提示
    // ===================================================================

    public static void OpenPopup(string id, int flags = 0)
    {
        var idPtr = Util.AllocUtf8(id);
        try { ImGuiNative.igOpenPopup(idPtr, flags); }
        finally { Util.Free(idPtr); }
    }
    public static bool BeginPopupModal(string name, ref bool open, int flags = 0)
    {
        var namePtr = Util.AllocUtf8(name);
        int o = open ? 1 : 0;
        try
        {
            bool visible = ImGuiNative.igBeginPopupModal(namePtr, &o, flags) != 0;
            open = o != 0;
            return visible;
        }
        finally { Util.Free(namePtr); }
    }
    public static void EndPopup() => ImGuiNative.igEndPopup();
    public static bool BeginPopup(string id, int flags = 0)
    {
        var idPtr = Util.AllocUtf8(id);
        try { return ImGuiNative.igBeginPopup(idPtr, flags) != 0; }
        finally { Util.Free(idPtr); }
    }
    public static void CloseCurrentPopup() => ImGuiNative.igCloseCurrentPopup();
    public static bool BeginPopupContextItem(string id = null, int flags = 1)
    {
        var idPtr = Util.AllocUtf8(id);
        try { return ImGuiNative.igBeginPopupContextItem(idPtr, flags) != 0; }
        finally { Util.Free(idPtr); }
    }
    public static void SetTooltip(string fmt)
    {
        var fmtPtr = Util.AllocUtf8(fmt);
        try { ImGuiNative.igSetTooltip(fmtPtr); }
        finally { Util.Free(fmtPtr); }
    }

    // ===================================================================
    // ID / 样式栈
    // ===================================================================

    public static void PushID(int id) => ImGuiNative.igPushID(id);
    public static void PushID(string strId)
    {
        var p = Util.AllocUtf8(strId);
        try { ImGuiNative.igPushIDStr(p); }
        finally { Util.Free(p); }
    }
    public static void PopID() => ImGuiNative.igPopID();
    public static void SetNextItemWidth(float width) => ImGuiNative.igSetNextItemWidth(width);
    public static void PushItemWidth(float width) => ImGuiNative.igPushItemWidth(width);
    public static void PopItemWidth() => ImGuiNative.igPopItemWidth();
    public static void PushStyleVar(int idx, float v0, float v1 = 0) => ImGuiNative.igPushStyleVar(idx, v0, v1);
    public static void PopStyleVar(int count = 1) => ImGuiNative.igPopStyleVar(count);
    public static void PushStyleColor(int idx, ImVec4 col) => ImGuiNative.igPushStyleColor(idx, col.X, col.Y, col.Z, col.W);
    public static void PopStyleColor(int count = 1) => ImGuiNative.igPopStyleColor(count);

    // ===================================================================
    // 禁用
    // ===================================================================

    public static void BeginDisabled(bool disabled = true) => ImGuiNative.igBeginDisabled(disabled ? 1 : 0);
    public static void EndDisabled() => ImGuiNative.igEndDisabled();

    // ===================================================================
    // item 状态
    // ===================================================================

    public static bool IsItemHovered(int flags = 0) => ImGuiNative.igIsItemHovered(flags) != 0;
    public static bool IsItemActive() => ImGuiNative.igIsItemActive() != 0;
    /// <summary>本帧 item 刚被激活（按下边沿，拖动/悬浮球用）。</summary>
    public static bool IsItemActivated() => ImGuiNative.igIsItemActivated() != 0;
    /// <summary>本帧 item 刚失活（释放边沿）。</summary>
    public static bool IsItemDeactivated() => ImGuiNative.igIsItemDeactivated() != 0;
    public static bool IsItemClicked(int mouseButton = 0) => ImGuiNative.igIsItemClicked(mouseButton) != 0;
    public static bool IsItemFocused() => ImGuiNative.igIsItemFocused() != 0;
    public static bool IsItemVisible() => ImGuiNative.igIsItemVisible() != 0;
    public static bool IsItemEdited() => ImGuiNative.igIsItemEdited() != 0;
    public static bool IsAnyItemActive() => ImGuiNative.igIsAnyItemActive() != 0;

    /// <summary>上一个 item 的矩形（自绘控件定位用）。</summary>
    public static ImVec2 GetItemRectMin()
    {
        float x = 0, y = 0;
        ImGuiNative.igGetItemRectMin(&x, &y);
        return new ImVec2(x, y);
    }

    public static ImVec2 GetItemRectMax()
    {
        float x = 0, y = 0;
        ImGuiNative.igGetItemRectMax(&x, &y);
        return new ImVec2(x, y);
    }

    // ===================================================================
    // 工具
    // ===================================================================

    /// <summary>按指定字号计算文本尺寸（Chrome：标题/副标题按比例字号）。</summary>
    public static ImVec2 CalcTextSizeA(float fontSize, string text)
    {
        float w = 0, h = 0;
        ImGuiNative.igCalcTextSizeA(fontSize, text, ref w, ref h);
        return new ImVec2(w, h);
    }

    // ===================================================================
    // 用户纹理（Mod 上传图片；纹理生命周期由调用方负责：不用时 DestroyTexture）
    // ===================================================================

    /// <summary>上传 RGBA 纹理（每像素 4 字节）→ ImTextureID；失败返回 0，Mod 退出前必须 DestroyTexture。</summary>
    public static ulong UploadTexture(byte[] rgba, int width, int height)
    {
        if (rgba == null || width <= 0 || height <= 0 || rgba.Length < width * height * 4) return 0;
        return ImGuiNative.igCreateTexture(rgba, width, height);
    }

    /// <summary>销毁上传的纹理（传 0 安全）。</summary>
    public static void DestroyTexture(ulong id) => ImGuiNative.igDestroyTexture(id);

    /// <summary>Image 控件：整张纹理画到光标处（size 为显示尺寸）。</summary>
    public static void Image(ulong id, ImVec2 size)
        => ImGuiNative.igImage(id, size.X, size.Y, 0, 0, 1, 1);

    /// <summary>Image 控件：取纹理局部区域（uv0/uv1，0..1）画到光标处。</summary>
    public static void Image(ulong id, ImVec2 size, ImVec2 uv0, ImVec2 uv1)
        => ImGuiNative.igImage(id, size.X, size.Y, uv0.X, uv0.Y, uv1.X, uv1.Y);

    /// <summary>当前窗口宽/高（Chrome 布局用）。</summary>
    public static float WindowWidth => ImGuiNative.igGetWindowWidth();
    public static float WindowHeight => ImGuiNative.igGetWindowHeight();

    /// <summary>当前字号（Chrome 几何缩放 U() 的基准）。</summary>
    public static float FontSize => ImGuiNative.igGetFontSize();

    /// <summary>本帧鼠标滚轮 y（导航滚动等用；触摸滑动经驱动转成滚轮喂入）。</summary>
    public static float MouseWheelY => ImGuiNative.igGetIO_MouseWheel();

    /// <summary>当前样式圆角（Chrome 推窗圆角 = 此值 * kPlan * 0.44 近似）。</summary>
    public static float StyleWindowRounding => ImGuiNative.igGetStyleWindowRounding();

    /// <summary>鼠标是否悬停在某矩形上（侧栏滚动判定用）。</summary>
    public static bool IsMouseHoveringRect(ImVec2 min, ImVec2 max, bool allowWhenDisabled = false)
        => ImGuiNative.igIsMouseHoveringRect(min.X, min.Y, max.X, max.Y, allowWhenDisabled ? 1 : 0) != 0;

    public static ImVec2 CalcTextSize(string text, float wrapWidth = -1)
    {
        var textPtr = Util.AllocUtf8(text);
        float w = 0, h = 0;
        try { ImGuiNative.igCalcTextSize(textPtr, wrapWidth, &w, &h); }
        finally { Util.Free(textPtr); }
        return new ImVec2(w, h);
    }
    public static uint GetColorU32(int idx) => ImGuiNative.igGetColorU32(idx);
    public static uint GetID(string id)
    {
        var idPtr = Util.AllocUtf8(id);
        try { return ImGuiNative.igGetID(idPtr); }
        finally { Util.Free(idPtr); }
    }
}