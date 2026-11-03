using System.Globalization;
using System.Numerics;
using Pylon.NaviteAPI.ImGui;

namespace Pylon.ModLoader.UI;

/// <summary>
/// Terraria 风格控件。布局与输入沿用 ImGui 的 item 体系，外观全部由 <see cref="TerrariaTheme"/> 手工绘制，
/// 因此可以在不依赖图片资源的情况下得到渐变、内倒角与描边文字。
/// </summary>
public static class TerrariaUI
{
    private const float BorderWidth = 2f;
    private const float ButtonPaddingX = 18f;
    private const float ButtonPaddingY = 8f;
    private const float NavPaddingX = 10f;
    private const float NavPaddingY = 8f;
    private const float CheckboxSize = 26f;
    private const float CheckboxGap = 8f;
    private const float SliderKnob = 26f;
    private const float SliderTrack = 16f;
    private const float RowGap = 12f;

    /// <summary>钢蓝渐变按钮；悬停转金褐，按下倒角翻转并下沉 1px。</summary>
    public static bool Button(string label, Vector2 size = default)
    {
        var text = DisplayLabel(label);
        var textSize = ImGui.CalcTextSize(text);
        if (size.X <= 0f) size.X = textSize.X + TerrariaTheme.Px(ButtonPaddingX * 2f + BorderWidth * 2f);
        if (size.Y <= 0f)
            size.Y = MathF.Max(textSize.Y + TerrariaTheme.Px(ButtonPaddingY * 2f + BorderWidth * 2f),
                TerrariaTheme.Px(34f));

        ImGui.PushID(WidgetId(label));
        try
        {
            var clicked = ImGui.InvisibleButton("##button", size, ImGuiButtonFlags.EnableNav);
            var hovered = ImGui.IsItemHovered() || ImGui.IsItemFocused();
            var held = ImGui.IsItemActive();
            DrawButton(ImGui.GetWindowDrawList(), ImGui.GetItemRectMin(), ImGui.GetItemRectMax(),
                text, hovered, held);
            return clicked;
        }
        finally
        {
            ImGui.PopID();
        }
    }

    /// <summary>左侧导航项。选中时金色高亮并在条目左侧亮起竖条。</summary>
    public static bool NavButton(string label, bool selected, string? id = null)
    {
        var text = DisplayLabel(label);
        var textSize = ImGui.CalcTextSize(text);
        var height = MathF.Max(textSize.Y + TerrariaTheme.Px(NavPaddingY * 2f + BorderWidth * 2f),
            TerrariaTheme.Px(34f));
        var width = ImGui.GetContentRegionAvail().X;

        ImGui.PushID(WidgetId(id ?? label));
        try
        {
            var clicked = ImGui.InvisibleButton("##nav", new Vector2(width, height), ImGuiButtonFlags.EnableNav);
            var hovered = ImGui.IsItemHovered() || ImGui.IsItemFocused();
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            var dl = ImGui.GetWindowDrawList();
            var active = selected || hovered;

            TerrariaTheme.ButtonGradient(active, false, out var top, out var mid, out var bottom);
            TerrariaTheme.Raised(dl, min, max, top, mid, bottom,
                TerrariaTheme.Px(2f), TerrariaTheme.Px(2f));

            if (selected) DrawNavMarker(dl, min, max);

            var textInset = TerrariaTheme.Px(BorderWidth);
            TextClipped(dl, min + new Vector2(textInset), max - new Vector2(textInset),
                new Vector2(min.X + TerrariaTheme.Px(NavPaddingX + BorderWidth),
                    min.Y + (height - textSize.Y) * 0.5f),
                active ? TerrariaTheme.GoldBright : TerrariaTheme.TextMain, text);
            return clicked;
        }
        finally
        {
            ImGui.PopID();
        }
    }

    /// <summary>像素勾选框，整行可点。</summary>
    public static bool Checkbox(string label, ref bool value)
    {
        var text = DisplayLabel(label);
        var textSize = ImGui.CalcTextSize(text);
        var box = TerrariaTheme.Px(CheckboxSize);
        var gap = TerrariaTheme.Px(CheckboxGap);
        var height = MathF.Max(box, textSize.Y);
        var width = box + gap + textSize.X;

        ImGui.PushID(WidgetId(label));
        try
        {
            var clicked = ImGui.InvisibleButton("##check", new Vector2(width, height), ImGuiButtonFlags.EnableNav);
            if (clicked) value = !value;

            var hovered = ImGui.IsItemHovered() || ImGui.IsItemFocused();
            var min = ImGui.GetItemRectMin();
            var boxMin = new Vector2(min.X, min.Y + (height - box) * 0.5f);
            var boxMax = boxMin + new Vector2(box, box);
            DrawCheckbox(ImGui.GetWindowDrawList(), boxMin, boxMax, value, hovered);
            TerrariaTheme.TextOutlined(ImGui.GetWindowDrawList(),
                new Vector2(boxMax.X + gap, min.Y + (height - textSize.Y) * 0.5f),
                TerrariaTheme.TextMain, text);
            return clicked;
        }
        finally
        {
            ImGui.PopID();
        }
    }

    /// <summary>带标签和右侧数值的 Terraria 滑条。</summary>
    public static bool SliderFloat(string label, ref float value, float min, float max,
                                   string format = "0.00", string suffix = "",
                                   float labelWidth = 110f, float valueWidth = 58f)
    {
        if (max <= min) return false;

        var text = DisplayLabel(label);
        var avail = MathF.Max(ImGui.GetContentRegionAvail().X, 1f);
        var textHeight = ImGui.GetTextLineHeight();
        var rowHeight = MathF.Max(TerrariaTheme.Px(SliderKnob), textHeight);
        var gap = TerrariaTheme.Px(RowGap);
        var labelW = MathF.Min(TerrariaTheme.Px(labelWidth), avail * 0.45f);
        var valueW = MathF.Min(TerrariaTheme.Px(valueWidth), avail * 0.20f);
        var trackW = avail - labelW - valueW - gap * 2f;

        if (trackW < TerrariaTheme.Px(40f))
        {
            valueW = 0f;
            trackW = avail - labelW - gap;
        }
        if (trackW < TerrariaTheme.Px(24f))
        {
            labelW = 0f;
            trackW = avail;
        }

        var start = ImGui.GetCursorScreenPos();
        var trackLeft = start.X + (labelW > 0f ? labelW + gap : 0f);
        var dl = ImGui.GetWindowDrawList();
        if (labelW > 0f)
        {
            TextClipped(dl, new Vector2(start.X, start.Y),
                new Vector2(start.X + labelW, start.Y + rowHeight),
                new Vector2(start.X, start.Y + (rowHeight - textHeight) * 0.5f),
                TerrariaTheme.TextMain, text);
        }

        ImGui.PushID(WidgetId(label));
        try
        {
            ImGui.SetCursorScreenPos(new Vector2(trackLeft, start.Y));
            ImGui.InvisibleButton("##track", new Vector2(trackW, rowHeight), ImGuiButtonFlags.MouseButtonLeft);

            var changed = false;
            var trackMin = new Vector2(trackLeft, start.Y + (rowHeight - TerrariaTheme.Px(SliderTrack)) * 0.5f);
            var trackMax = new Vector2(trackMin.X + trackW, trackMin.Y + TerrariaTheme.Px(SliderTrack));
            var ratio = Math.Clamp((value - min) / (max - min), 0f, 1f);

            if (ImGui.IsItemActive() && ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                var innerWidth = MathF.Max(trackW - TerrariaTheme.Px(4f), 1f);
                var nextRatio = Math.Clamp(
                    (ImGui.GetMousePos().X - trackMin.X - TerrariaTheme.Px(2f)) / innerWidth, 0f, 1f);
                var next = min + (max - min) * nextRatio;
                if (MathF.Abs(next - value) > 0.0001f)
                {
                    value = next;
                    ratio = nextRatio;
                    changed = true;
                }
            }

            DrawSlider(dl, trackMin, trackMax, ratio,
                ImGui.IsItemActive() || ImGui.IsItemHovered());

            if (valueW > 0f)
            {
                var valueText = value.ToString(format, CultureInfo.InvariantCulture) + suffix;
                var valueSize = ImGui.CalcTextSize(valueText);
                TextClipped(dl, new Vector2(start.X + avail - valueW, start.Y),
                    new Vector2(start.X + avail, start.Y + rowHeight),
                    new Vector2(start.X + avail - valueSize.X,
                        start.Y + (rowHeight - valueSize.Y) * 0.5f),
                    TerrariaTheme.GoldBright, valueText);
            }

            return changed;
        }
        finally
        {
            ImGui.PopID();
        }
    }

    /// <summary>带标签的钢蓝下拉框；展开后是深色 Terraria 列表。</summary>
    public static bool Combo(string label, ref int current, string[]? items, float labelWidth = 110f)
    {
        if (items is null || items.Length == 0) return false;
        current = Math.Clamp(current, 0, items.Length - 1);

        var text = DisplayLabel(label);
        var avail = MathF.Max(ImGui.GetContentRegionAvail().X, 1f);
        var textHeight = ImGui.GetTextLineHeight();
        var rowHeight = MathF.Max(textHeight + TerrariaTheme.Px(14f + BorderWidth * 2f), TerrariaTheme.Px(34f));
        var gap = TerrariaTheme.Px(RowGap);
        var labelW = MathF.Min(TerrariaTheme.Px(labelWidth), avail * 0.35f);
        var headerW = MathF.Min(TerrariaTheme.Px(150f), MathF.Max(avail - labelW - gap, 1f));
        if (headerW < TerrariaTheme.Px(72f))
        {
            labelW = 0f;
            headerW = MathF.Min(avail, TerrariaTheme.Px(150f));
        }

        var start = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        if (labelW > 0f)
        {
            TextClipped(dl, new Vector2(start.X, start.Y),
                new Vector2(start.X + labelW, start.Y + rowHeight),
                new Vector2(start.X, start.Y + (rowHeight - textHeight) * 0.5f),
                TerrariaTheme.TextMain, text);
        }

        var headerMin = new Vector2(start.X + (labelW > 0f ? labelW + gap : 0f), start.Y);
        ImGui.PushID(WidgetId(label));
        try
        {
            ImGui.SetCursorScreenPos(headerMin);
            if (ImGui.InvisibleButton("##combo", new Vector2(headerW, rowHeight), ImGuiButtonFlags.EnableNav))
                ImGui.OpenPopup("##comboPopup");

            var hovered = ImGui.IsItemHovered() || ImGui.IsItemFocused();
            DrawComboHeader(ImGui.GetWindowDrawList(), ImGui.GetItemRectMin(), ImGui.GetItemRectMax(),
                items[current], hovered);

            var changed = false;
            TerrariaTheme.PushVec2(ImGuiStyleVar.WindowPadding, new Vector2(TerrariaTheme.Px(4f)));
            try
            {
                if (ImGui.IsPopupOpen("##comboPopup"))
                {
                    // 只在弹层真的存在时设置 next-window 数据，否则会泄漏给本帧后创建的悬浮球窗口。
                    var popupWidth = headerW;
                    for (var i = 0; i < items.Length; i++)
                        popupWidth = MathF.Max(popupWidth,
                            ImGui.CalcTextSize(items[i]).X + TerrariaTheme.Px(44f));

                    ImGui.SetNextWindowPos(new Vector2(headerMin.X, headerMin.Y + rowHeight + TerrariaTheme.Px(4f)),
                        ImGuiCond.Appearing);
                    ImGui.SetNextWindowSizeConstraints(new Vector2(popupWidth, 0f),
                        new Vector2(popupWidth, TerrariaTheme.Px(260f)), IntPtr.Zero, IntPtr.Zero);
                }

                if (ImGui.BeginPopup("##comboPopup", ImGuiWindowFlags.NoSavedSettings))
                {
                    DrawComboPopup(items, ref current, ref changed);
                    ImGui.EndPopup();
                }
            }
            finally
            {
                ImGui.PopStyleVar(2);
            }

            return changed;
        }
        finally
        {
            ImGui.PopID();
        }
    }

    /// <summary>分隔标题：金色描边文字 + 两侧渐隐金线。</summary>
    public static void SeparatorText(string label)
    {
        var text = DisplayLabel(label);
        var width = ImGui.GetContentRegionAvail().X;
        var textSize = ImGui.CalcTextSize(text);
        var dl = ImGui.GetWindowDrawList();

        ImGui.Dummy(new Vector2(0f, TerrariaTheme.Px(6f)));
        var pos = ImGui.GetCursorScreenPos();
        var centerY = pos.Y + textSize.Y * 0.5f;
        var lineHeight = TerrariaTheme.Px(1f);
        var gap = TerrariaTheme.Px(10f);
        var textStart = pos.X + (width - textSize.X) * 0.5f;
        var textEnd = textStart + textSize.X;
        var leftEnd = MathF.Max(pos.X, textStart - gap);
        var rightStart = MathF.Min(pos.X + width, textEnd + gap);

        if (leftEnd > pos.X)
            TerrariaTheme.Gradient(dl, new Vector2(pos.X, centerY - lineHeight),
                new Vector2(leftEnd, centerY + lineHeight),
                TerrariaTheme.Alpha(TerrariaTheme.GoldDeep, 0f), TerrariaTheme.GoldDeep);
        if (rightStart < pos.X + width)
            TerrariaTheme.Gradient(dl, new Vector2(rightStart, centerY - lineHeight),
                new Vector2(pos.X + width, centerY + lineHeight),
                TerrariaTheme.GoldDeep, TerrariaTheme.Alpha(TerrariaTheme.GoldDeep, 0f));

        TerrariaTheme.TextOutlined(dl, new Vector2(textStart, pos.Y), TerrariaTheme.Gold, text);
        ImGui.Dummy(new Vector2(width, textSize.Y));
    }

    public static void Text(string text) => TextColored(text, TerrariaTheme.TextMain);

    public static void TextColored(string text, Vector4 color)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var height = DrawWrappedText(dl, pos, width, color, text);
        ImGui.Dummy(new Vector2(width, height));
    }

    public static void DimText(string text) => TextColored(text, TerrariaTheme.TextDim);

    public static void Status(string text) => TextColored(text, TerrariaTheme.Success);

    /// <summary>
    /// 文本输入框：外观自己画（和其他控件一样），编辑交给系统软键盘（见 <see cref="ImGuiInputText"/>）。
    /// 不走 igInputText：它在 ReadOnly 下仍会记光标和横向滚动位置，外部整串回写文本时按旧光标算，
    /// 删字后文字就飘到框中间；它的 ActiveId 也收不回来。自己画就只有"左对齐 + 超宽贴右"两种状态。
    /// </summary>
    public static bool TextInput(string label, ref string value)
    {
        value ??= string.Empty;
        var text = DisplayLabel(label);
        if (!string.IsNullOrEmpty(text)) DimText(text);

        var width = MathF.Max(ImGui.GetContentRegionAvail().X, 1f);
        var height = MathF.Max(ImGui.GetTextLineHeight() + TerrariaTheme.Px(12f + BorderWidth * 2f),
            TerrariaTheme.Px(34f));

        ImGui.PushID(WidgetId("##" + label));
        try
        {
            var id = ImGui.GetID("##field");
            var clicked = ImGui.InvisibleButton("##field", new Vector2(width, height), ImGuiButtonFlags.EnableNav);
            ImGuiInputText.HandleClick(id, clicked, value);

            // 键盘里的整串文本就是这一帧的值（拼音候选、选词都由系统输入法负责）
            var typed = ImGuiInputText.Pull(id);
            var changed = typed is not null && !string.Equals(typed, value, StringComparison.Ordinal);
            if (changed) value = typed!;

            DrawTextField(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), value,
                ImGuiInputText.IsEditing(id), ImGui.IsItemHovered() || ImGui.IsItemFocused());
            return changed;
        }
        finally
        {
            ImGui.PopID();
        }
    }

    private static void DrawButton(ImDrawListPtr dl, Vector2 min, Vector2 max, string text, bool hovered, bool held)
    {
        TerrariaTheme.ButtonGradient(hovered, held, out var top, out var mid, out var bottom);

        if (held)
        {
            var sink = new Vector2(0f, TerrariaTheme.Px(1f));
            min += sink;
            max += sink;
            TerrariaTheme.Sunken(dl, min, max, top, mid, bottom,
                TerrariaTheme.Px(2f), TerrariaTheme.Px(2f));
        }
        else
        {
            TerrariaTheme.Raised(dl, min, max, top, mid, bottom,
                TerrariaTheme.Px(2f), TerrariaTheme.Px(2f));
        }

        var color = hovered || held ? TerrariaTheme.GoldBright : TerrariaTheme.TextMain;
        var textSize = ImGui.CalcTextSize(text);
        var textInset = TerrariaTheme.Px(BorderWidth);
        TextClipped(dl, min + new Vector2(textInset), max - new Vector2(textInset),
            new Vector2(min.X + (max.X - min.X - textSize.X) * 0.5f,
                min.Y + (max.Y - min.Y - textSize.Y) * 0.5f),
            color, text);
    }

    private static void DrawNavMarker(ImDrawListPtr dl, Vector2 min, Vector2 max)
    {
        var barW = TerrariaTheme.Px(4f);
        var barMin = new Vector2(min.X - TerrariaTheme.Px(8f), min.Y);
        var barMax = new Vector2(barMin.X + barW, max.Y);
        var glow = TerrariaTheme.Alpha(TerrariaTheme.Gold, 0.18f);

        TerrariaTheme.Fill(dl, new Vector2(barMin.X - TerrariaTheme.Px(2f), barMin.Y),
            new Vector2(barMax.X + TerrariaTheme.Px(4f), max.Y), glow);
        TerrariaTheme.Fill(dl, barMin, barMax, TerrariaTheme.Gold);
    }

    private static void DrawCheckbox(ImDrawListPtr dl, Vector2 min, Vector2 max, bool value, bool hovered)
    {
        TerrariaTheme.Sunken(dl, min, max, TerrariaTheme.FieldTop, TerrariaTheme.FieldMid,
            TerrariaTheme.FieldBottom, TerrariaTheme.Px(2f), TerrariaTheme.Px(2f));

        if (hovered)
        {
            var inset = TerrariaTheme.Px(2f);
            TerrariaTheme.Outline(dl, min + new Vector2(inset), max - new Vector2(inset),
                TerrariaTheme.Alpha(TerrariaTheme.Gold, 0.55f), TerrariaTheme.Px(1.5f));
        }

        if (!value) return;

        var w = max.X - min.X;
        var h = max.Y - min.Y;
        var p1 = new Vector2(min.X + w * 0.20f, min.Y + h * 0.52f);
        var p2 = new Vector2(min.X + w * 0.42f, min.Y + h * 0.73f);
        var p3 = new Vector2(min.X + w * 0.80f, min.Y + h * 0.28f);
        var width = TerrariaTheme.Px(3f);

        dl.AddLine(p1, p2, TerrariaTheme.U32(TerrariaTheme.Border), width + TerrariaTheme.Px(2f));
        dl.AddLine(p2, p3, TerrariaTheme.U32(TerrariaTheme.Border), width + TerrariaTheme.Px(2f));
        dl.AddLine(p1, p2, TerrariaTheme.U32(TerrariaTheme.GoldBright), width);
        dl.AddLine(p2, p3, TerrariaTheme.U32(TerrariaTheme.GoldBright), width);
    }

    /// <summary>凹陷的输入框 + 文字：左对齐，文字比框宽就贴着右边显示（打字时能看见末尾）。</summary>
    private static void DrawTextField(Vector2 min, Vector2 max, string text, bool editing, bool hovered)
    {
        var dl = ImGui.GetWindowDrawList();
        TerrariaTheme.Sunken(dl, min, max, TerrariaTheme.FieldTop, TerrariaTheme.FieldMid,
            TerrariaTheme.FieldBottom, TerrariaTheme.Px(BorderWidth), TerrariaTheme.Px(BorderWidth));

        if (editing || hovered)
        {
            var inset = TerrariaTheme.Px(2f);
            TerrariaTheme.Outline(dl, min + new Vector2(inset), max - new Vector2(inset),
                TerrariaTheme.Alpha(TerrariaTheme.Gold, editing ? 0.75f : 0.40f), TerrariaTheme.Px(1.5f));
        }

        var border = TerrariaTheme.Px(BorderWidth);
        var insetX = TerrariaTheme.Px(10f);
        var textSize = ImGui.CalcTextSize(text);
        var visible = MathF.Max(max.X - min.X - insetX * 2f - border * 2f, 1f);
        var x = min.X + border + insetX - MathF.Max(textSize.X - visible, 0f);
        var textY = min.Y + (max.Y - min.Y - textSize.Y) * 0.5f;

        TextClipped(dl, new Vector2(min.X + border, min.Y), new Vector2(max.X - border, max.Y),
            new Vector2(x, textY), TerrariaTheme.TextMain, text);

        // 光标：跟在文字末尾，闪烁（约每秒一次）
        if (editing && (ImGui.GetFrameCount() / 30) % 2 == 0)
        {
            var caretW = TerrariaTheme.Px(2f);
            var caretX = MathF.Min(x + textSize.X, max.X - border - insetX * 0.5f - caretW);
            TerrariaTheme.Fill(dl, new Vector2(caretX, textY), new Vector2(caretX + caretW, textY + textSize.Y),
                TerrariaTheme.TextMain);
        }
    }

    private static void DrawSlider(ImDrawListPtr dl, Vector2 trackMin, Vector2 trackMax, float ratio, bool active)
    {
        TerrariaTheme.Sunken(dl, trackMin, trackMax, TerrariaTheme.FieldTop, TerrariaTheme.FieldMid,
            TerrariaTheme.FieldBottom, TerrariaTheme.Px(2f), TerrariaTheme.Px(2f));

        var inset = TerrariaTheme.Px(2f);
        var innerMin = new Vector2(trackMin.X + inset, trackMin.Y + inset);
        var innerMax = new Vector2(trackMax.X - inset, trackMax.Y - inset);
        var innerWidth = MathF.Max(innerMax.X - innerMin.X, 1f);
        var knobX = innerMin.X + innerWidth * ratio;
        var centerY = (trackMin.Y + trackMax.Y) * 0.5f;

        if (knobX > innerMin.X)
        {
            TerrariaTheme.Gradient(dl, innerMin, new Vector2(knobX, innerMax.Y),
                TerrariaTheme.Alpha(TerrariaTheme.Gold, 0.35f), TerrariaTheme.Alpha(TerrariaTheme.Gold, 0.12f));
        }

        var radius = TerrariaTheme.Px(SliderKnob) * 0.5f;
        var center = new Vector2(knobX, centerY);
        if (active)
            dl.AddCircleFilled(center, radius + TerrariaTheme.Px(3f),
                TerrariaTheme.U32(TerrariaTheme.Alpha(TerrariaTheme.Gold, 0.30f)));

        dl.AddCircleFilled(center, radius, TerrariaTheme.U32(TerrariaTheme.Border));
        dl.AddCircleFilled(center, radius - inset, TerrariaTheme.U32(TerrariaTheme.Gold));
        dl.AddCircleFilled(new Vector2(center.X - radius * 0.22f, center.Y - radius * 0.28f),
            radius * 0.38f, TerrariaTheme.U32(TerrariaTheme.Alpha(TerrariaTheme.GoldBright, 0.85f)));
    }

    private static void DrawComboHeader(ImDrawListPtr dl, Vector2 min, Vector2 max, string text, bool hovered)
    {
        TerrariaTheme.ButtonGradient(hovered, false, out var top, out var mid, out var bottom);
        TerrariaTheme.Raised(dl, min, max, top, mid, bottom,
            TerrariaTheme.Px(2f), TerrariaTheme.Px(2f));

        var textSize = ImGui.CalcTextSize(text);
        var textMin = new Vector2(min.X + TerrariaTheme.Px(BorderWidth), min.Y);
        var textMax = new Vector2(MathF.Max(textMin.X, max.X - TerrariaTheme.Px(30f)), max.Y);
        TextClipped(dl, textMin, textMax,
            new Vector2(min.X + TerrariaTheme.Px(14f + BorderWidth),
                min.Y + (max.Y - min.Y - textSize.Y) * 0.5f),
            hovered ? TerrariaTheme.GoldBright : TerrariaTheme.TextMain, text);

        var cx = max.X - TerrariaTheme.Px(16f);
        var cy = (min.Y + max.Y) * 0.5f;
        var arrow = TerrariaTheme.Px(5f);
        dl.AddTriangleFilled(
            new Vector2(cx - arrow, cy - arrow * 0.5f),
            new Vector2(cx + arrow, cy - arrow * 0.5f),
            new Vector2(cx, cy + arrow * 0.8f),
            TerrariaTheme.U32(TerrariaTheme.Gold));
    }

    private static void DrawComboPopup(string[] items, ref int current, ref bool changed)
    {
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        TerrariaTheme.Raised(ImGui.GetWindowDrawList(), winPos, winPos + winSize,
            TerrariaTheme.PopupTop, TerrariaTheme.PopupMid, TerrariaTheme.PopupBottom,
            TerrariaTheme.Px(2f), TerrariaTheme.Px(2f));

        var textHeight = ImGui.GetTextLineHeight();
        var rowHeight = MathF.Max(textHeight + TerrariaTheme.Px(12f), TerrariaTheme.Px(30f));
        var width = ImGui.GetContentRegionAvail().X;

        for (var i = 0; i < items.Length; i++)
        {
            ImGui.PushID(i.ToString(CultureInfo.InvariantCulture));
            var rowMin = ImGui.GetCursorScreenPos();
            var clicked = ImGui.InvisibleButton("##item", new Vector2(width, rowHeight));
            var hovered = ImGui.IsItemHovered();
            var selected = i == current;
            var rowMax = rowMin + new Vector2(width, rowHeight);
            var dl = ImGui.GetWindowDrawList();

            if (hovered || selected)
            {
                TerrariaTheme.Fill(dl, rowMin, rowMax,
                    TerrariaTheme.Alpha(TerrariaTheme.Gold, hovered ? 0.18f : 0.10f));
            }

            if (selected)
            {
                var cx = rowMin.X + TerrariaTheme.Px(14f);
                var cy = (rowMin.Y + rowMax.Y) * 0.5f;
                var size = TerrariaTheme.Px(4f);
                var color = TerrariaTheme.U32(TerrariaTheme.Gold);
                dl.AddLine(new Vector2(cx, cy - size), new Vector2(cx + size, cy), color, TerrariaTheme.Px(2f));
                dl.AddLine(new Vector2(cx + size, cy), new Vector2(cx, cy + size), color, TerrariaTheme.Px(2f));
            }

            var textSize = ImGui.CalcTextSize(items[i]);
            TextClipped(dl, rowMin, rowMax,
                new Vector2(rowMin.X + TerrariaTheme.Px(selected ? 22f : 14f),
                    rowMin.Y + (rowHeight - textSize.Y) * 0.5f),
                selected ? TerrariaTheme.GoldBright : TerrariaTheme.TextMain, items[i]);

            if (clicked)
            {
                current = i;
                changed = true;
                ImGui.CloseCurrentPopup();
            }
            ImGui.PopID();
        }
    }

    private static float DrawWrappedText(ImDrawListPtr dl, Vector2 pos, float width, Vector4 color, string text)
    {
        var lineHeight = ImGui.GetTextLineHeight();
        if (string.IsNullOrEmpty(text)) return lineHeight;
        if (width <= 0f || ImGui.CalcTextSize(text).X <= width)
        {
            TerrariaTheme.TextOutlined(dl, pos, color, text);
            return lineHeight;
        }

        var y = 0f;
        var start = 0;
        var lines = 0;
        while (start < text.Length)
        {
            var end = start;
            var lineWidth = 0f;
            while (end < text.Length && text[end] != '\n')
            {
                var glyph = ImGui.CalcTextSize(text.Substring(end, 1)).X;
                if (lineWidth + glyph > width && end > start) break;
                lineWidth += glyph;
                end++;
            }

            TerrariaTheme.TextOutlined(dl, new Vector2(pos.X, pos.Y + y), color,
                text.Substring(start, end - start));
            y += lineHeight;
            lines++;
            start = end < text.Length && text[end] == '\n' ? end + 1 : end;
        }
        return lines * lineHeight;
    }

    private static void TextClipped(ImDrawListPtr dl, Vector2 clipMin, Vector2 clipMax,
                                    Vector2 pos, Vector4 color, string text)
    {
        if (clipMax.X <= clipMin.X || clipMax.Y <= clipMin.Y) return;
        dl.PushClipRect(clipMin, clipMax, true);
        TerrariaTheme.TextOutlined(dl, pos, color, text);
        dl.PopClipRect();
    }

    private static string DisplayLabel(string label)
    {
        if (string.IsNullOrEmpty(label)) return string.Empty;
        var mark = label.IndexOf("##", StringComparison.Ordinal);
        return mark < 0 ? label : label[..mark];
    }

    private static string WidgetId(string label) => string.IsNullOrEmpty(label) ? "##widget" : label;
}
