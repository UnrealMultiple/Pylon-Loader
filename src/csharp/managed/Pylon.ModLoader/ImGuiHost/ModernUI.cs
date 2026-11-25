using System.Globalization;
using System.Numerics;
using Pylon.NaviteAPI.ImGui;

namespace Pylon.ModLoader.ImGuiHost;

/// <summary>
/// 现代化控件库：卡片、行、按钮、滑条、开关、复选、分段、下拉、物品行、状态条、主题卡、Toast 与线性图标；
/// 布局全部走 ImGui 流式游标（Dummy/SameLine/SetCursorPos 局部坐标），绘制走窗口 DrawList，
/// 因此在原生滚动容器内排版与裁剪都由内核保证；label 的 ## 约定与旧控件一致。
/// </summary>
public static unsafe class ModernUI
{
    private const float CardPaddingX = 18f;
    private const float CardPaddingY = 16f;
    private const float CardGap = 14f;
    private const float CardHeadGap = 12f;
    private const float RowPadY = 9f;
    private const float ControlGapY = 10f;
    private const float RowLabelWidth = 150f;
    private const float RowGapX = 14f;
    private const float SliderTrackH = 8f;
    private const float SliderHitH = 26f;
    private const float SliderKnob = 16f;
    private const float ToggleW = 42f;
    private const float ToggleH = 24f;
    private const float ToggleKnob = 18f;
    private const float CheckSize = 19f;
    private const float SegPadY = 6f;
    private const float SegPadX = 15f;
    private const float ButtonPadX = 20f;
    private const float ButtonPadY = 9f;
    private const float ButtonFont = 13.5f;
    private const float SmallButtonPadX = 14f;
    private const float SmallButtonPadY = 5f;
    private const float SmallButtonFont = 12f;
    private const float ThemeCardH = 104f;
    private const float ThemeCardMinW = 148f;
    private const float ThemeCardGap = 12f;
    private const float ItemIcon = 38f;
    private const float CapsSpacing = 0.28f;

    public enum Icon
    {
        None,
        Sliders,
        Sparkle,
        Terminal,
        ChevronDown,
        ChevronLeft,
        ChevronRight,
        Close,
        Star,
        Sword,
        Broom,
        Wand,
        Seal,
    }

    /// <summary>当前页的开始时间（用于卡片交错入场）；由覆盖层在切页时重置。</summary>
    public static float PageStart { get; set; } = -1f;

    /// <summary>内容行可用宽度：卡内时在可用宽度上再扣右侧内边距，使行内容与卡片两侧留白对称。</summary>
    internal static float CardAvailX => MathF.Max(ImGui.GetContentRegionAvail().X - TerrariaTheme.Px(_cardInset), 1f);

    private static int _cardIndex;
    private static float _cardFade = 1f;
    private static Vector2 _cardOrigin;
    private static float _cardWidth;
    /// <summary>卡内时为 CardPaddingX：行内容右侧同样要留出内边距（左侧由 Indent 承担）。</summary>
    private static float _cardInset;
    private static float _rowTop;
    private static float _rowLabelBottom;
    private static bool _rowDrawn;
    /// <summary>是否处在 BeginRow/EndRow 之间（行内控件由 EndRow 统一收尾，不再补间距）。</summary>
    private static bool _inRow;
    /// <summary>最近一次 RowLabel 的文字块上下边界（用于把行控件与标签垂直居中对齐）。</summary>
    private static float _labelTop = float.NaN;
    private static float _labelBottom = float.NaN;
    private static bool _headAction;
    private static float _gridCardW = 148f;
    private static string? _statusText;
    private static float _statusShownAt;
    private static string? _toastText;
    private static float _toastShownAt;
    private static readonly Dictionary<uint, float> BlinkTimes = [];
    private static readonly Dictionary<uint, (float Value, string Text)> ValueCache = [];
    private static readonly Dictionary<uint, (Vector2 Min, Vector2 Max)> FrozenTracks = [];
    private static readonly Dictionary<uint, float> ToggleAnims = [];
    private static readonly Dictionary<char, string> GlyphCache = [];
    // NumIds 已移除：原先 PushID(NumIds[i]) 在选项超过 10 个时越界（ComboPopup 17 项直接崩），
    // 统一改用 ImGui.PushID(int) 重载。

    public static void BeginPage()
    {
        _cardIndex = 0;
        _rowDrawn = false;
        _cardInset = 0f;
    }

    /// <summary>卡片容器开始：自动交错入场，头部为「中文标题 + caps」。</summary>
    public static void BeginCard(string title, string caps, string? rightCaps = null)
    {
        BeginCardCore(title, caps, rightCaps, null);
    }

    /// <summary>卡片容器开始（头部右侧带小按钮动作，如「清空」）。</summary>
    public static void BeginCard(string title, string caps, out bool action, string actionLabel)
    {
        BeginCardCore(title, caps, null, actionLabel);
        action = _headAction;
    }

    private static void BeginCardCore(string title, string caps, string? rightCaps, string? actionLabel)
    {
        _rowDrawn = false;
        _headAction = false;
        var (alpha, offsetY) = Motion.CardEnter(_cardIndex++, PageStart);
        _cardFade = alpha;
        UiTheme.PushFade(alpha);
        if (offsetY != 0f) ImGui.SetCursorPosY(ImGui.GetCursorPosY() + offsetY);
        _cardOrigin = ImGui.GetCursorScreenPos();
        _cardWidth = MathF.Max(CardAvailX, 1f);
        _cardInset = CardPaddingX;
        ImGui.Indent(TerrariaTheme.Px(CardPaddingX));
        var dl = ImGui.GetWindowDrawList();
        dl.ChannelsSplit(2);
        dl.ChannelsSetCurrent(1);
        ImGui.Dummy(new Vector2(0f, TerrariaTheme.Px(CardPaddingY)));
        CardHead(title, caps, rightCaps, actionLabel);
    }

    /// <summary>卡片容器结束：补齐底部内边距、画背景与描边，恢复布局游标。</summary>
    public static void EndCard()
    {
        var dl = ImGui.GetWindowDrawList();
        ImGui.Dummy(new Vector2(0f, TerrariaTheme.Px(CardPaddingY)));
        var bottom = ImGui.GetCursorScreenPos().Y;
        dl.ChannelsSetCurrent(0);
        DrawCardBackdrop(dl, _cardOrigin, new Vector2(_cardOrigin.X + _cardWidth, bottom),
            TerrariaTheme.Px(UiTheme.CardRadius));
        dl.ChannelsMerge();
        UiTheme.PopFade();
        ImGui.Unindent(TerrariaTheme.Px(CardPaddingX));
        _cardInset = 0f;
        ImGui.Dummy(new Vector2(0f, TerrariaTheme.Px(CardGap)));
    }

    /// <summary>卡片头：中文标题 + caps 小字，可带右侧说明或小按钮动作。</summary>
    public static void CardHead(string title, string caps, string? rightCaps = null, string? actionLabel = null)
    {
        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var titleTextSize = Measure(title, TerrariaTheme.Px(14.5f), 0.04f);
        DrawText(dl, pos, TerrariaTheme.Px(14.5f), UiTheme.Text, title, null, 0.04f);
        var capsBaseline = pos.Y + (titleTextSize.Y - CapsHeight(9f)) * 0.5f;
        DrawCaps(dl, new Vector2(pos.X + titleTextSize.X + TerrariaTheme.Px(10f), capsBaseline),
            9f, UiTheme.Text3, caps);
        if (actionLabel != null)
        {
            var btnSize = ButtonSize(actionLabel, small: true);
            var bx = _cardOrigin.X + _cardWidth - TerrariaTheme.Px(CardPaddingX) - btnSize.X;
            var by = pos.Y + (titleTextSize.Y - btnSize.Y) * 0.5f;
            ImGui.SetCursorScreenPos(new Vector2(bx, by));
            _headAction = GhostSmall(actionLabel);
            ImGui.SetCursorScreenPos(pos);
        }
        else if (rightCaps != null)
        {
            DrawCaps(dl, new Vector2(_cardOrigin.X + _cardWidth - TerrariaTheme.Px(CardPaddingX) - MeasureCaps(rightCaps, 9f).X,
                capsBaseline), 9f, UiTheme.Text3, rightCaps);
        }
        ImGui.Dummy(new Vector2(_cardWidth - TerrariaTheme.Px(CardPaddingX) * 2f,
            titleTextSize.Y + TerrariaTheme.Px(CardHeadGap)));
    }

    /// <summary>页头：大标题 + caps + 描述行。</summary>
    public static void PageHead(string title, string caps, string desc)
    {
        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = CardAvailX;
        var titleTextSize = Measure(title, TerrariaTheme.Px(24f), 0.05f);
        DrawText(dl, pos, TerrariaTheme.Px(24f), UiTheme.Text, title, null, 0.05f);
        DrawCaps(dl, new Vector2(pos.X + titleTextSize.X + TerrariaTheme.Px(12f),
                pos.Y + (titleTextSize.Y - CapsHeight(9f)) * 0.5f),
            9f, UiTheme.Text3, caps);
        var descTextSize = Measure(desc, TerrariaTheme.Px(12.5f));
        DrawText(dl, new Vector2(pos.X, pos.Y + titleTextSize.Y + TerrariaTheme.Px(5f)),
            TerrariaTheme.Px(12.5f), UiTheme.Text2, desc);
        ImGui.Dummy(new Vector2(width,
            titleTextSize.Y + TerrariaTheme.Px(5f) + descTextSize.Y + TerrariaTheme.Px(18f)));
    }

    /// <summary>行开始：连续行之间画 1px 发丝分隔线。</summary>
    public static void BeginRow()
    {
        ImGui.Dummy(new Vector2(0f, TerrariaTheme.Px(RowPadY)));
        if (_rowDrawn)
        {
            var pos = ImGui.GetCursorScreenPos();
            ImGui.GetWindowDrawList().AddLine(pos, pos + new Vector2(CardAvailX, 0f),
                TerrariaTheme.U32(UiTheme.Hairline), TerrariaTheme.Px(1f));
        }
        _rowDrawn = true;
        _inRow = true;
        _rowTop = ImGui.GetCursorScreenPos().Y;
        _rowLabelBottom = _rowTop;
    }

    /// <summary>行结束：游标推进到行内容底部并补齐行底部间距。</summary>
    public static void EndRow()
    {
        _inRow = false;
        var cursorY = ImGui.GetCursorScreenPos().Y;
        var need = MathF.Max(_rowLabelBottom, cursorY) - cursorY;
        if (need > 0f) ImGui.Dummy(new Vector2(0f, need));
        ImGui.Dummy(new Vector2(0f, TerrariaTheme.Px(RowPadY)));
    }

    /// <summary>行左侧标签：固定列宽，可带一行小字副标题；调用后游标经 SameLine 停在控件区。</summary>
    public static void RowLabel(string label, string? sub = null, float width = RowLabelWidth)
    {
        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var labelW = TerrariaTheme.Px(width);
        var textSize = Measure(label, TerrariaTheme.Px(14f));
        DrawText(dl, pos, TerrariaTheme.Px(14f), UiTheme.Text, label);
        var height = textSize.Y;
        if (sub != null)
        {
            var subSize = Measure(sub, TerrariaTheme.Px(11f));
            DrawText(dl, new Vector2(pos.X, pos.Y + textSize.Y + TerrariaTheme.Px(2f)),
                TerrariaTheme.Px(11f), UiTheme.Text3, sub);
            height += TerrariaTheme.Px(2f) + subSize.Y;
        }
        _rowLabelBottom = MathF.Max(_rowLabelBottom, pos.Y + height);
        _labelTop = pos.Y;
        _labelBottom = pos.Y + height;
        ImGui.Dummy(new Vector2(labelW, height));
        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + TerrariaTheme.Px(RowGapX));
    }

    /// <summary>整行滑条：左标签 + 渐变填充轨道 + 右侧 accent 数值（变化时弹跳）。</summary>
    public static bool RowSlider(string id, string label, string sub, ref float value,
                                 float min, float max, string format = "0.00", string suffix = "",
                                 float displayScale = 1f)
    {
        BeginRow();
        RowLabel(label, sub);
        var changed = Slider(id, ref value, min, max, format, suffix, displayScale);
        EndRow();
        return changed;
    }

    /// <summary>滑条控件：8px 圆角轨道、accent→accent2 渐变填充、16px 白色圆钮、右侧数值。
    /// displayScale 只作用于右侧数值的显示（如 0..1 存储值 ×100 显示成百分比）。</summary>
    public static bool Slider(string id, ref float value, float min, float max,
                              string format = "0.00", string suffix = "", float displayScale = 1f)
    {
        if (max <= min) return false;
        var avail = CardAvailX;
        var startX = ImGui.GetCursorScreenPos().X;
        var valueText = CachedValue(ImGui.GetID(id), value * displayScale, format, suffix);
        var trackW = MathF.Max(avail - TerrariaTheme.Px(58f) - TerrariaTheme.Px(14f), TerrariaTheme.Px(40f));
        ImGui.PushID(id);
        try
        {
            ImGui.InvisibleButton("##track", new Vector2(trackW, TerrariaTheme.Px(SliderHitH)),
                ImGuiButtonFlags.EnableNav);
            var changed = false;
            var itemId = ImGui.GetItemID();
            var hitMin = ImGui.GetItemRectMin();
            var hitMax = ImGui.GetItemRectMax();
            if (ImGui.IsItemActivated())
                FrozenTracks[itemId] = (hitMin, hitMax);
            if (ImGui.IsItemActive() && FrozenTracks.TryGetValue(itemId, out var frozen))
            {
                hitMin = frozen.Min;
                hitMax = frozen.Max;
            }
            else if (!ImGui.IsItemActive())
            {
                FrozenTracks.Remove(itemId);
            }
            var trackHalf = TerrariaTheme.Px(SliderTrackH) * 0.5f;
            var centerY = (hitMin.Y + hitMax.Y) * 0.5f;
            var trackMin = new Vector2(hitMin.X, centerY - trackHalf);
            var trackMax = new Vector2(hitMax.X, centerY + trackHalf);
            if (ImGui.IsItemActive() && ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                var ratio = Math.Clamp((ImGui.GetMousePos().X - hitMin.X) /
                                       MathF.Max(hitMax.X - hitMin.X, 1f), 0f, 1f);
                var next = min + (max - min) * ratio;
                if (MathF.Abs(next - value) > 0.0001f)
                {
                    value = next;
                    changed = true;
                    BlinkTimes[ImGui.GetID(id)] = Motion.Time;
                }
            }
            var io = ImGui.GetIO();
            UiScroll.KeepDragLock(ImGui.IsItemActive() &&
                                  MathF.Abs(io.MouseDelta.X) > MathF.Abs(io.MouseDelta.Y));
            DrawSliderTrack(ImGui.GetWindowDrawList(), trackMin, trackMax,
                Math.Clamp((value - min) / (max - min), 0f, 1f),
                ImGui.IsItemHovered() || ImGui.IsItemActive());

            var stamp = BlinkTimes.TryGetValue(ImGui.GetID(id), out var t0) ? t0 : -1f;
            var bump = stamp >= 0f ? Motion.Bump(Motion.Progress(stamp, Motion.BumpSeconds)) : 1f;
            var size = TerrariaTheme.Px(14f) * bump;
            var bumpText = bump > 1.001f ? CachedValue(ImGui.GetID(id), value * displayScale, format, suffix) : valueText;
            var textSize = Measure(bumpText, size);
            DrawText(ImGui.GetWindowDrawList(),
                new Vector2(startX + avail - textSize.X, centerY - textSize.Y * 0.5f),
                size, UiTheme.Accent, bumpText);
            return changed;
        }
        finally
        {
            ImGui.PopID();
        }
    }

    /// <summary>开关：42×24 圆角胶囊，knob 弹性滑动，打开时 accent→accent2 渐变与辉光；返回是否被点击。</summary>
    public static bool Toggle(string id, bool value)
    {
        ImGui.PushID(id);
        try
        {
            var clicked = ImGui.InvisibleButton("##toggle",
                new Vector2(TerrariaTheme.Px(ToggleW), TerrariaTheme.Px(ToggleH)), ImGuiButtonFlags.EnableNav);
            var animId = ImGui.GetID(id);
            var anim = ToggleAnims.TryGetValue(animId, out var a) ? a : (value ? 1f : 0f);
            var target = value ? 1f : 0f;
            if (anim != target)
            {
                var dt = ImGui.GetIO().DeltaTime;
                anim += (target - anim) * (1f - MathF.Exp(-dt / MathF.Max(Motion.ToggleSeconds * 0.45f, 0.001f)));
                if (MathF.Abs(target - anim) < 0.02f) anim = target;
                ToggleAnims[animId] = anim;
            }
            DrawToggle(ImGui.GetWindowDrawList(), ImGui.GetItemRectMin(), anim);
            return clicked;
        }
        finally
        {
            ImGui.PopID();
        }
    }

    /// <summary>复选框：19px 圆角方块 + 行内文字；返回是否被点击。</summary>
    public static bool Checkbox(string label, ref bool value)
    {
        var text = DisplayLabel(label);
        var textSize = Measure(text, TerrariaTheme.Px(14f));
        var box = TerrariaTheme.Px(CheckSize);
        var gap = TerrariaTheme.Px(9f);
        var height = MathF.Max(box, textSize.Y);
        ImGui.PushID(label);
        try
        {
            var clicked = ImGui.InvisibleButton("##check",
                new Vector2(box + gap + textSize.X, height), ImGuiButtonFlags.EnableNav);
            if (clicked) value = !value;
            var min = ImGui.GetItemRectMin();
            var boxMin = new Vector2(min.X, min.Y + (height - box) * 0.5f);
            DrawCheckbox(ImGui.GetWindowDrawList(), boxMin, boxMin + new Vector2(box, box), value,
                ImGui.IsItemHovered() || ImGui.IsItemActive());
            DrawText(ImGui.GetWindowDrawList(),
                new Vector2(boxMin.X + box + gap, min.Y + (height - textSize.Y) * 0.5f),
                TerrariaTheme.Px(14f), UiTheme.Text, text);
            return clicked;
        }
        finally
        {
            ImGui.PopID();
        }
    }

    /// <summary>分段选择：胶囊容器 + 圆角药丸选中块；rightAlign 时贴可用宽度右缘。</summary>
    public static bool Seg(string id, ref int current, string[] options, bool rightAlign = false)
    {
        if (options == null || options.Length == 0) return false;
        current = Math.Clamp(current, 0, options.Length - 1);
        var itemH = TerrariaTheme.Px(SegPadY * 2f + 12.5f);
        var widths = new float[options.Length];
        var padIn = TerrariaTheme.Px(3f);
        var total = padIn * 2f;
        for (var i = 0; i < options.Length; i++)
        {
            widths[i] = Measure(options[i], TerrariaTheme.Px(12.5f)).X + TerrariaTheme.Px(SegPadX * 2f);
            total += widths[i] + padIn;
        }
        total -= padIn;
        var outer = new Vector2(total, itemH + padIn * 2f);
        var dy = 0f;
        if (!float.IsNaN(_labelTop) && MathF.Abs(ImGui.GetCursorScreenPos().Y - _labelTop) < 1f)
            dy = MathF.Max(0f, (_labelBottom - _labelTop - outer.Y) * 0.5f);
        if (rightAlign) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + CardAvailX - total);
        var start = ImGui.GetCursorScreenPos() + new Vector2(0f, dy);
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(start, start + outer, TerrariaTheme.U32(UiTheme.Field), TerrariaTheme.Px(11f));
        dl.AddRect(start, start + outer, TerrariaTheme.U32(UiTheme.FieldStroke), TerrariaTheme.Px(11f),
            0, TerrariaTheme.Px(1f));
        var changed = false;
        ImGui.PushID(id);
        try
        {
            var x = start.X + padIn;
            for (var i = 0; i < options.Length; i++)
            {
                ImGui.SetCursorScreenPos(new Vector2(x, start.Y + padIn));
                ImGui.PushID(i.ToString());
                var selected = i == current;
                var clicked = ImGui.InvisibleButton("##seg", new Vector2(widths[i], itemH),
                    ImGuiButtonFlags.EnableNav);
                DrawSegItem(ImGui.GetWindowDrawList(), ImGui.GetItemRectMin(), ImGui.GetItemRectMax(),
                    options[i], selected, ImGui.IsItemHovered());
                if (clicked && !selected)
                {
                    current = i;
                    changed = true;
                }
                ImGui.PopID();
                x += widths[i] + padIn;
            }
            ImGui.SetCursorScreenPos(start);
            ImGui.Dummy(outer);
            StandaloneGap();
        }
        finally
        {
            ImGui.PopID();
        }
        return changed;
    }

    /// <summary>下拉框：field 底圆角按钮 + pop 弹层（选中项 accent 与勾）。</summary>
    public static bool Combo(string label, ref int current, string[] items, float labelWidth = 150f)
    {
        if (items == null || items.Length == 0) return false;
        current = Math.Clamp(current, 0, items.Length - 1);
        BeginRow();
        RowLabel(label, null, labelWidth);
        var changed = ComboField(label, items, ref current);
        EndRow();
        return changed;
    }

    /// <summary>行内下拉框控件。</summary>
    public static bool ComboField(string id, string[] items, ref int current)
    {
        var text = items[current];
        var avail = MathF.Max(CardAvailX, 1f);
        var width = TerrariaTheme.Px(190f);
        for (var i = 0; i < items.Length; i++)
            width = MathF.Max(width, Measure(items[i], TerrariaTheme.Px(13f)).X + TerrariaTheme.Px(46f));
        width = MathF.Min(width, avail);
        var height = TerrariaTheme.Px(14f + 16f);
        ImGui.PushID(id);
        try
        {
            ImGui.InvisibleButton("##combo", new Vector2(width, height), ImGuiButtonFlags.EnableNav);
            if (ImGui.IsItemClicked()) ImGui.OpenPopup("##pop");
            var io = ImGui.GetIO();
            UiScroll.KeepDragLock(ImGui.IsItemActive() &&
                                  MathF.Abs(io.MouseDelta.X) > MathF.Abs(io.MouseDelta.Y));
            DrawComboHeader(ImGui.GetWindowDrawList(), ImGui.GetItemRectMin(), ImGui.GetItemRectMax(),
                text, ImGui.IsPopupOpen("##pop"), ImGui.IsItemHovered());
            var changed = false;
            TerrariaTheme.PushVec2(ImGuiStyleVar.WindowPadding, new Vector2(TerrariaTheme.Px(5f)));
            try
            {
                if (ImGui.BeginPopup("##pop", ImGuiWindowFlags.NoSavedSettings))
                {
                    changed = ComboPopup(items, ref current, width);
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

    private static bool ComboPopup(string[] items, ref int current, float width)
    {
        var changed = false;
        for (var i = 0; i < items.Length; i++)
        {
            ImGui.PushID(i.ToString());
            var rowH = TerrariaTheme.Px(13f + 13f);
            var clicked = ImGui.InvisibleButton("##item", new Vector2(width - TerrariaTheme.Px(10f), rowH));
            DrawComboItem(ImGui.GetWindowDrawList(), ImGui.GetItemRectMin(), ImGui.GetItemRectMax(),
                items[i], i == current, ImGui.IsItemHovered());
            if (clicked)
            {
                current = i;
                changed = true;
                ImGui.CloseCurrentPopup();
            }
            ImGui.PopID();
        }
        return changed;
    }

    /// <summary>文本输入框：外观自绘，编辑交给系统软键盘。</summary>
    public static bool TextInput(string label, ref string value)
    {
        value ??= string.Empty;
        var text = DisplayLabel(label);
        if (!string.IsNullOrEmpty(text))
        {
            DimText(text);
            ImGui.Dummy(new Vector2(0f, TerrariaTheme.Px(6f)));
        }
        var width = MathF.Max(CardAvailX, 1f);
        var height = TerrariaTheme.Px(14f + 14f);
        ImGui.PushID(label);
        try
        {
            var id = ImGui.GetID("##field");
            var clicked = ImGui.InvisibleButton("##field", new Vector2(width, height), ImGuiButtonFlags.EnableNav);
            ImGuiInputText.HandleClick(id, clicked, value);
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

    /// <summary>返回把 size 宽的内容贴到内容区右缘的位置（不移动游标）。</summary>
    public static Vector2 RightAnchor(Vector2 size)
    {
        var pos = ImGui.GetCursorScreenPos();
        return new Vector2(pos.X + CardAvailX - size.X, pos.Y);
    }

    private static Vector2 ButtonSize(string label, bool small)
    {
        var font = TerrariaTheme.Px(small ? SmallButtonFont : ButtonFont);
        var textSize = Measure(DisplayLabel(label), font, 0.03f);
        return new Vector2(textSize.X + TerrariaTheme.Px(small ? SmallButtonPadX : ButtonPadX) * 2f,
            MathF.Max(textSize.Y + TerrariaTheme.Px(small ? SmallButtonPadY : ButtonPadY) * 2f,
                TerrariaTheme.Px(small ? 24f : 34f)));
    }

    /// <summary>主按钮：accent 纵向渐变、辉光投影、悬停上浮、按下缩放。</summary>
    public static bool PrimaryButton(string label, Vector2 size = default, Icon icon = Icon.None, bool rightAlign = false)
    {
        return DrawButton(label, size, icon, primary: true, small: false, rightAlign);
    }

    /// <summary>ghost 按钮：field 底 + 描边，悬停转 accent。</summary>
    public static bool GhostButton(string label, Vector2 size = default, Icon icon = Icon.None, bool rightAlign = false)
    {
        return DrawButton(label, size, icon, primary: false, small: false, rightAlign);
    }

    /// <summary>小号 ghost 按钮（物品行「获取」、卡片头动作）。</summary>
    public static bool GhostSmall(string label)
    {
        return DrawButton(label, default, Icon.None, primary: false, small: true, false);
    }

    /// <summary>兼容签名：默认 ghost 样式按钮。</summary>
    public static bool Button(string label, Vector2 size = default)
    {
        return DrawButton(label, size, Icon.None, primary: false, small: false, false);
    }

    private static bool DrawButton(string label, Vector2 size, Icon icon, bool primary, bool small, bool rightAlign)
    {
        if (size.X <= 0f || size.Y <= 0f)
        {
            var fit = ButtonSize(label, small);
            if (size.X <= 0f) size.X = fit.X;
            if (size.Y <= 0f) size.Y = fit.Y;
        }
        if (rightAlign) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + CardAvailX - size.X);
        ImGui.PushID(label);
        try
        {
            var clicked = ImGui.InvisibleButton("##btn", size, ImGuiButtonFlags.EnableNav);
            var hovered = ImGui.IsItemHovered() || ImGui.IsItemFocused();
            var held = ImGui.IsItemActive();
            var dl = ImGui.GetWindowDrawList();
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            var text = DisplayLabel(label);
            var font = TerrariaTheme.Px(small ? SmallButtonFont : ButtonFont);
            var textSize = Measure(text, font);
            if (primary)
            {
                if (hovered && Motion.Enabled) min.Y -= TerrariaTheme.Px(1f);
                if (held)
                {
                    var inset = size * (1f - 0.965f) * 0.5f;
                    min += inset;
                    max -= inset;
                }
                DrawPrimaryButton(dl, min, max, text, icon, font, textSize, hovered);
            }
            else
            {
                DrawGhostButton(dl, min, max, text, icon, font, textSize, hovered);
            }
            if (!small) StandaloneGap();
            return clicked;
        }
        finally
        {
            ImGui.PopID();
        }
    }

    /// <summary>独立控件的行后间距（对应 HTML flex 容器的 gap；行内由 EndRow 统一收尾）。</summary>
    private static void StandaloneGap()
    {
        if (_inRow) return;
        ImGui.Dummy(new Vector2(0f, TerrariaTheme.Px(ControlGapY)));
    }

    private static void DrawPrimaryButton(ImDrawListPtr dl, Vector2 min, Vector2 max, string text,
        Icon icon, float font, Vector2 textSize, bool hovered)
    {
        var radius = TerrariaTheme.Px(11f);
        var glow = UiTheme.Glow;
        GradientRounded(dl,
            min + new Vector2(0f, TerrariaTheme.Px(hovered ? 7f : 5f)),
            max + new Vector2(0f, TerrariaTheme.Px(hovered ? 24f : 18f)),
            glow, UiTheme.Alpha(glow, 0f), radius, 180f);
        GradientRounded(dl, min, max, UiTheme.AccentHi, UiTheme.Accent, radius, 180f);
        dl.AddLine(min + new Vector2(radius, TerrariaTheme.Px(1f)),
            new Vector2(max.X - radius, min.Y + TerrariaTheme.Px(1f)),
            TerrariaTheme.U32(new Vector4(1f, 1f, 1f, 0.28f)), TerrariaTheme.Px(1f));
        DrawButtonContent(dl, min, max, text, icon, font, textSize, new Vector4(1f, 1f, 1f, 1f), 0.03f);
    }

    private static void DrawGhostButton(ImDrawListPtr dl, Vector2 min, Vector2 max, string text,
        Icon icon, float font, Vector2 textSize, bool hovered, bool small = false)
    {
        var radius = small ? TerrariaTheme.Px(9f) : TerrariaTheme.Px(11f);
        dl.AddRectFilled(min, max, TerrariaTheme.U32(UiTheme.Field), radius);
        dl.AddRect(min, max, TerrariaTheme.U32(hovered ? UiTheme.Alpha(UiTheme.Accent, 0.45f) : UiTheme.FieldStroke), radius);
        DrawButtonContent(dl, min, max, text, icon, font, textSize, hovered ? UiTheme.Accent : UiTheme.Text, 0.03f);
    }

    private static void DrawButtonContent(ImDrawListPtr dl, Vector2 min, Vector2 max, string text,
        Icon icon, float font, Vector2 textSize, Vector4 color, float spacingEm)
    {
        var iconW = icon != Icon.None ? TerrariaTheme.Px(15f + 8f) : 0f;
        var totalW = textSize.X + iconW;
        var x = min.X + ((max.X - min.X) - totalW) * 0.5f;
        var y = min.Y + ((max.Y - min.Y) - textSize.Y) * 0.5f;
        if (icon != Icon.None)
        {
            var box = TerrariaTheme.Px(15f);
            DrawIcon(dl, new Vector2(x, y + (textSize.Y - box) * 0.5f),
                new Vector2(x + box, y + (textSize.Y - box) * 0.5f + box), icon, color, 1.8f);
            x += iconW;
        }
        DrawText(dl, new Vector2(x, y), font, color, text, null, spacingEm);
    }

    /// <summary>主题卡网格：minmax(148px,1fr) 列数自适应；drawCard 在每个卡片位置被调用。</summary>
    public static void ThemeGrid(int count, Func<int, bool> drawCard)
    {
        var avail = MathF.Max(CardAvailX, 1f);
        var cell = TerrariaTheme.Px(ThemeCardMinW + ThemeCardGap);
        var columns = Math.Clamp((int)MathF.Floor((avail + TerrariaTheme.Px(ThemeCardGap)) / cell), 1, count);
        _gridCardW = (avail - TerrariaTheme.Px(ThemeCardGap) * (columns - 1)) / columns;
        var cardH = TerrariaTheme.Px(ThemeCardH);
        var clicked = -1;
        ImGui.PushStyleVarY(ImGuiStyleVar.ItemSpacing, TerrariaTheme.Px(ThemeCardGap));
        try
        {
            for (var i = 0; i < count; i++)
            {
                if (i % columns > 0)
                {
                    ImGui.SameLine();
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + TerrariaTheme.Px(ThemeCardGap));
                }
                if (drawCard(i)) clicked = i;
            }
        }
        finally
        {
            ImGui.PopStyleVar();
        }
        if (clicked >= 0) ImGuiOverlay.RequestThemeSwitch(clicked);
    }

    /// <summary>主题卡：渐变预览底、角标圆点、tag、名称、caps 与短语；点击切换主题。</summary>
    public static bool ThemeCard(int index)
    {
        var palette = UiTheme.Palettes[index];
        var size = new Vector2(_gridCardW, TerrariaTheme.Px(ThemeCardH));
        ImGui.PushID(index.ToString());
        try
        {
            var clicked = ImGui.InvisibleButton("##tcard", size, ImGuiButtonFlags.EnableNav);
            var selected = UiTheme.Current == index;
            var hovered = ImGui.IsItemHovered();
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            if (hovered && Motion.Enabled)
            {
                min.Y -= TerrariaTheme.Px(3f);
                max.Y -= TerrariaTheme.Px(3f);
            }
            var dl = ImGui.GetWindowDrawList();
            if (selected)
            {
                dl.AddRect(min - new Vector2(TerrariaTheme.Px(1f)), max + new Vector2(TerrariaTheme.Px(1f)),
                    TerrariaTheme.U32(UiTheme.Accent), TerrariaTheme.Px(14f), 0, TerrariaTheme.Px(2f));
                dl.AddRectFilled(min - new Vector2(TerrariaTheme.Px(4f)), max + new Vector2(TerrariaTheme.Px(4f)),
                    TerrariaTheme.U32(UiTheme.Glow), TerrariaTheme.Px(14f) + TerrariaTheme.Px(4f));
            }
            DrawCardShadow(dl, min, max, TerrariaTheme.Px(14f), hovered);
            GradientRounded(dl, min, max, palette.Pv0, palette.Pv1, TerrariaTheme.Px(14f), 155f);
            dl.AddRect(min, max, TerrariaTheme.U32(new Vector4(1f, 1f, 1f, 0.14f)), TerrariaTheme.Px(14f));

            var chipD = TerrariaTheme.Px(16f);
            var chipCenter = min + new Vector2(TerrariaTheme.Px(12f), TerrariaTheme.Px(11f)) + new Vector2(chipD * 0.5f);
            dl.AddCircleFilled(chipCenter, chipD * 0.5f, TerrariaTheme.U32(palette.PvAcc));
            dl.AddCircle(chipCenter, chipD * 0.5f + TerrariaTheme.Px(2f),
                TerrariaTheme.U32(UiTheme.Alpha(palette.PvAcc, 0.5f)), 0, TerrariaTheme.Px(2f));

            var tagText = palette.Tag;
            var tagSize = Measure(tagText, TerrariaTheme.Px(10f));
            var tagMin = new Vector2(max.X - TerrariaTheme.Px(10f) - tagSize.X - TerrariaTheme.Px(18f),
                min.Y + TerrariaTheme.Px(10f));
            var tagMax = new Vector2(max.X - TerrariaTheme.Px(10f), tagMin.Y + tagSize.Y + TerrariaTheme.Px(6f));
            dl.AddRectFilled(tagMin, tagMax, TerrariaTheme.U32(palette.PvAcc), TerrariaTheme.Px(10f));
            DrawText(dl, new Vector2(tagMin.X + TerrariaTheme.Px(9f), tagMin.Y + TerrariaTheme.Px(3f)),
                TerrariaTheme.Px(10f), new Vector4(1f, 1f, 1f, 1f), tagText);

            var pad = TerrariaTheme.Px(13f);
            var tx = min.X + pad;
            var nameSize = Measure(palette.Cn, TerrariaTheme.Px(14.5f), 0.05f);
            var enSize = MeasureCaps(palette.En, 8.5f, 0.24f);
            var phraseSize = Measure(palette.Phrase, TerrariaTheme.Px(11f));
            var bottom = max.Y - pad;
            var phraseY = bottom - phraseSize.Y;
            var enY = phraseY - TerrariaTheme.Px(6f) - enSize.Y;
            var nameY = enY - TerrariaTheme.Px(2f) - nameSize.Y;
            var shadow = palette.PvTextShadow ? new Vector4(0f, 0f, 0f, 0.45f) : default(Vector4?);
            DrawText(dl, new Vector2(tx, nameY), TerrariaTheme.Px(14.5f), palette.PvText, palette.Cn, shadow, 0.05f);
            DrawCaps(dl, new Vector2(tx, enY), 8.5f, palette.PvText2, palette.En, 0.24f);
            DrawText(dl, new Vector2(tx, phraseY), TerrariaTheme.Px(11f), palette.PvText3, palette.Phrase, shadow);
            return clicked;
        }
        finally
        {
            ImGui.PopID();
        }
    }

    /// <summary>物品行：38px 渐变图标块 + 名称 + caps + 描述 + 「获取」小按钮。</summary>
    public static bool ItemRow(string name, string caps, string desc, uint ic0, uint ic1, Icon icon, string action)
    {
        var dl = ImGui.GetWindowDrawList();
        var avail = MathF.Max(CardAvailX, 1f);
        var start = ImGui.GetCursorScreenPos();
        var rowH = TerrariaTheme.Px(ItemIcon + 22f);
        var hovered = ImGui.IsMouseHoveringRect(start, start + new Vector2(avail, rowH));
        if (hovered) dl.AddRectFilled(start, start + new Vector2(avail, rowH), TerrariaTheme.U32(UiTheme.Soft), TerrariaTheme.Px(10f));
        if (_rowDrawn)
            dl.AddLine(start, start + new Vector2(avail, 0f), TerrariaTheme.U32(UiTheme.Hairline), TerrariaTheme.Px(1f));
        _rowDrawn = true;

        var iconSize = TerrariaTheme.Px(ItemIcon);
        var iconMin = start + new Vector2(TerrariaTheme.Px(4f), TerrariaTheme.Px(11f));
        var iconMax = iconMin + new Vector2(iconSize, iconSize);
        var iconInset = (iconSize - TerrariaTheme.Px(19f)) * 0.5f;
        dl.AddRectFilled(iconMin + new Vector2(0f, TerrariaTheme.Px(3f)), iconMax + new Vector2(0f, TerrariaTheme.Px(3f)),
            TerrariaTheme.U32(new Vector4(0f, 0f, 0f, 0.30f)), TerrariaTheme.Px(11f));
        GradientRounded(dl, iconMin, iconMax, UiTheme.Hex(ic0), UiTheme.Hex(ic1), TerrariaTheme.Px(11f), 150f);
        DrawIcon(dl, iconMin + new Vector2(iconInset), iconMax - new Vector2(iconInset), icon, new Vector4(1f, 1f, 1f, 1f), 1.7f);

        var textX = iconMax.X + TerrariaTheme.Px(13f);
        var nameSize = Measure(name, TerrariaTheme.Px(14f), 0.03f);
        var descSize = Measure(desc, TerrariaTheme.Px(11.5f));
        var textTop = iconMin.Y + (iconSize - nameSize.Y - TerrariaTheme.Px(2f) - descSize.Y) * 0.5f;
        DrawText(dl, new Vector2(textX, textTop), TerrariaTheme.Px(14f), UiTheme.Text, name, null, 0.03f);
        DrawCaps(dl, new Vector2(textX + nameSize.X + TerrariaTheme.Px(8f),
            textTop + (nameSize.Y - CapsHeight(9f)) * 0.5f), 9f, UiTheme.Text3, caps);
        DrawText(dl, new Vector2(textX, textTop + nameSize.Y + TerrariaTheme.Px(2f)),
            TerrariaTheme.Px(11.5f), UiTheme.Text3, desc);

        var btnSize = ButtonSize(action, small: true);
        ImGui.SetCursorScreenPos(new Vector2(start.X + avail - TerrariaTheme.Px(4f) - btnSize.X,
            start.Y + (rowH - btnSize.Y) * 0.5f));
        bool give;
        ImGui.PushID(name);
        try
        {
            give = GhostSmall(action);
            ImGui.SetCursorScreenPos(start);
            ImGui.Dummy(new Vector2(avail, rowH));
        }
        finally
        {
            ImGui.PopID();
        }
        return give;
    }

    /// <summary>状态条：good 色底 10% + 30% 描边 + 发光圆点，弹出动画。</summary>
    public static void ShowStatus(string text)
    {
        _statusText = text;
        _statusShownAt = Motion.Time;
    }

    /// <summary>
    /// 占位高度与有无状态无关：状态文本进程内只增不清，空时若不占位，首次 ShowStatus 才凭空多出一行，
    /// 会把下方内容整体顶下去一截（点物品行「获取」后卡片跳一下），而且只发生在第一次。
    /// </summary>
    public static void StatusRow()
    {
        var avail = MathF.Max(CardAvailX, 1f);
        var start = ImGui.GetCursorScreenPos();
        // 行高只取决于字号（内核逐行累加字号），空串量出的高度与有字时一致。
        var textSize = Measure(_statusText ?? string.Empty, TerrariaTheme.Px(12.5f));
        var height = textSize.Y + TerrariaTheme.Px(18f);
        ImGui.Dummy(new Vector2(avail, height + TerrariaTheme.Px(12f)));
        if (string.IsNullOrEmpty(_statusText)) return;
        var dl = ImGui.GetWindowDrawList();
        var pop = Motion.Enabled ? Motion.OutBack(Motion.Progress(_statusShownAt, Motion.StatusPopSeconds), 1.4f) : 1f;
        var scale = 0.96f + 0.04f * pop;
        var w = avail * scale;
        var min = start + new Vector2((avail - w) * 0.5f, (1f - pop) * TerrariaTheme.Px(4f));
        var max = min + new Vector2(w, height * scale);
        var good = UiTheme.Good;
        dl.AddRectFilled(min, max, TerrariaTheme.U32(UiTheme.Alpha(good, 0.10f * pop)), TerrariaTheme.Px(10f));
        dl.AddRect(min, max, TerrariaTheme.U32(UiTheme.Alpha(good, 0.30f * pop)), TerrariaTheme.Px(10f));
        var dot = min + new Vector2(TerrariaTheme.Px(14f + 3.5f), (max.Y - min.Y) * 0.5f);
        dl.AddCircleFilled(dot, TerrariaTheme.Px(3.5f), TerrariaTheme.U32(good));
        dl.AddCircleFilled(dot, TerrariaTheme.Px(6f), TerrariaTheme.U32(UiTheme.Alpha(good, 0.25f * pop)));
        DrawText(dl, new Vector2(min.X + TerrariaTheme.Px(29f), min.Y + ((max.Y - min.Y) - textSize.Y) * 0.5f),
            TerrariaTheme.Px(12.5f), UiTheme.Alpha(good, pop), _statusText);
    }

    /// <summary>Toast：顶部居中胶囊提示，1.8s 自动消退。</summary>
    public static void Toast(string message)
    {
        _toastText = message;
        _toastShownAt = Motion.Time;
    }

    internal static void DrawToast()
    {
        if (string.IsNullOrEmpty(_toastText)) return;
        var elapsed = Motion.Time - _toastShownAt;
        if (elapsed > Motion.ToastHoldSeconds + Motion.ToastSeconds)
        {
            _toastText = null;
            return;
        }
        var appear = Motion.Enabled ? Motion.OutBack(Motion.Progress(_toastShownAt, Motion.ToastSeconds), 1.2f) : 1f;
        var fade = Motion.Enabled && elapsed > Motion.ToastHoldSeconds
            ? 1f - Motion.OutCubic(Motion.Progress(_toastShownAt + Motion.ToastHoldSeconds, Motion.ToastSeconds)) : 1f;
        var alpha = Math.Clamp(appear * fade, 0f, 1f);
        if (alpha <= 0.01f) return;
        var dl = ImGui.GetBackgroundDrawList();
        var (displayWidth, _) = ImGuiNativeApi.DisplaySize;
        var size = TerrariaTheme.Px(13f);
        var textSize = Measure(_toastText, size);
        var w = textSize.X + TerrariaTheme.Px(40f);
        var h = textSize.Y + TerrariaTheme.Px(18f);
        var min = new Vector2((displayWidth - w) * 0.5f,
            TerrariaTheme.Px(22f) - (1f - appear) * TerrariaTheme.Px(16f));
        var max = min + new Vector2(w, h);
        dl.AddRectFilled(min, max, TerrariaTheme.U32(UiTheme.Alpha(UiTheme.Pop, alpha)), TerrariaTheme.Px(24f));
        dl.AddRect(min, max, TerrariaTheme.U32(UiTheme.Alpha(UiTheme.CardStroke, alpha)), TerrariaTheme.Px(24f));
        DrawText(dl, new Vector2(min.X + TerrariaTheme.Px(20f), min.Y + (h - textSize.Y) * 0.5f),
            size, UiTheme.Alpha(UiTheme.Text, alpha), _toastText);
    }

    public static void Text(string text) => TextColored(text, UiTheme.Text);

    public static void DimText(string text) => TextColored(text, UiTheme.Text2);

    /// <summary>成功色状态文本（旧 Status 语义）。</summary>
    public static void StatusGood(string text) => TextColored(text, UiTheme.Good);

    /// <summary>自动换行的文本块。</summary>
    public static void TextColored(string text, Vector4 color)
    {
        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var avail = CardAvailX;
        var size = TerrariaTheme.Px(14f);
        var bounds = MeasureWrapped(text, size, avail);
        DrawTextWrapped(dl, pos, size, color, text, avail);
        ImGui.Dummy(new Vector2(MathF.Max(avail, 1f), MathF.Max(bounds.Y, size)));
    }

    /// <summary>行内文本：只推进一个文本宽度，可与同行后续控件拼接。</summary>
    public static void InlineText(string text, Vector4 color, float designSize = 13f)
    {
        var size = TerrariaTheme.Px(designSize);
        var bounds = Measure(text, size);
        DrawText(ImGui.GetWindowDrawList(), ImGui.GetCursorScreenPos(), size, color, text);
        ImGui.Dummy(bounds);
    }

    /// <summary>段落小标题（旧 SeparatorText 语义，供示例 Mod 使用）。</summary>
    public static void SectionTitle(string label)
    {
        ImGui.Dummy(new Vector2(0f, TerrariaTheme.Px(10f)));
        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var avail = CardAvailX;
        var size = TerrariaTheme.Px(12.5f);
        DrawText(dl, pos, size, UiTheme.Text2, label);
        ImGui.Dummy(new Vector2(MathF.Max(avail, 1f), Measure(label, size).Y + TerrariaTheme.Px(6f)));
    }

    /// <summary>等宽终端行：时间戳 + 着色级别标签 + 来源 + 消息，超宽自动换行。</summary>
    public static void TermLine(string time, string tag, string level, string src, string message)
    {
        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var avail = CardAvailX;
        var size = TerrariaTheme.Px(11.5f);
        var x = pos.X;
        DrawText(dl, new Vector2(x, pos.Y), size, UiTheme.Text3, time);
        x += TerrariaTheme.Px(64f);
        var levelColor = level switch
        {
            "ok" => UiTheme.Good,
            "warn" => UiTheme.Warn,
            "error" => UiTheme.Warn,
            _ => UiTheme.Accent,
        };
        DrawText(dl, new Vector2(x, pos.Y), size, levelColor, tag);
        x += Measure(tag, size).X + TerrariaTheme.Px(4f);
        DrawText(dl, new Vector2(x, pos.Y), size, UiTheme.Text3, src);
        x += Measure(src, size).X + TerrariaTheme.Px(4f);
        var wrapWidth = MathF.Max(pos.X + avail - x, 1f);
        var messageBounds = MeasureWrapped(message, size, wrapWidth);
        DrawTextWrapped(dl, new Vector2(x, pos.Y), size, UiTheme.Text2, message, wrapWidth);
        ImGui.Dummy(new Vector2(MathF.Max(avail, 1f), MathF.Max(messageBounds.Y, size)));
    }

    /// <summary>caps 小字（字母间距 0.28em）。</summary>
    public static void CapsText(string text, float designSize, Vector4? color = null)
    {
        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var size = TerrariaTheme.Px(designSize);
        DrawCaps(dl, pos, size, color ?? UiTheme.Text3, text);
        ImGui.Dummy(new Vector2(CardAvailX, MeasureCaps(text, designSize).Y));
    }

    public static float CapsHeight(float sizePx) => MeasureCaps("A", sizePx).Y;

    internal static void ResetTransient()
    {
        _statusText = null;
        _toastText = null;
        BlinkTimes.Clear();
        ValueCache.Clear();
        ToggleAnims.Clear();
        FrozenTracks.Clear();
    }

    private static void DrawSliderTrack(ImDrawListPtr dl, Vector2 min, Vector2 max, float ratio, bool hovered)
    {
        var radius = TerrariaTheme.Px(5f);
        dl.AddRectFilled(min, max, TerrariaTheme.U32(UiTheme.Field), radius);
        dl.AddRect(min, max, TerrariaTheme.U32(UiTheme.FieldStroke), radius);
        var knob = TerrariaTheme.Px(SliderKnob);
        var knobR = knob * 0.5f;
        var fillMax = new Vector2(min.X + (max.X - min.X) * ratio, max.Y);
        if (fillMax.X > min.X + radius)
            GradientRounded(dl, min, fillMax, UiTheme.Accent, UiTheme.Accent2, radius, 90f);
        var center = new Vector2(Math.Clamp(min.X + (max.X - min.X) * ratio, min.X + knobR, max.X - knobR),
            (min.Y + max.Y) * 0.5f);
        dl.AddCircleFilled(center, knobR + TerrariaTheme.Px(hovered ? 5f : 4f),
            TerrariaTheme.U32(hovered ? UiTheme.Glow : UiTheme.Soft));
        dl.AddCircleFilled(center, knobR, TerrariaTheme.U32(new Vector4(1f, 1f, 1f, 1f)));
    }

    private static void DrawToggle(ImDrawListPtr dl, Vector2 min, float anim)
    {
        var size = new Vector2(TerrariaTheme.Px(ToggleW), TerrariaTheme.Px(ToggleH));
        var max = min + size;
        var radius = TerrariaTheme.Px(13f);
        dl.AddRectFilled(min, max, TerrariaTheme.U32(UiTheme.Field), radius);
        dl.AddRect(min, max, TerrariaTheme.U32(UiTheme.FieldStroke), radius);
        if (anim > 0.001f)
        {
            if (Motion.Enabled)
                dl.AddRectFilled(min - new Vector2(TerrariaTheme.Px(4f)), max + new Vector2(TerrariaTheme.Px(4f)),
                    TerrariaTheme.U32(UiTheme.Glow), radius);
            GradientRounded(dl, min, max,
                UiTheme.Alpha(UiTheme.Accent, anim), UiTheme.Alpha(UiTheme.Accent2, anim), radius, 90f);
        }
        var knob = TerrariaTheme.Px(ToggleKnob);
        var x = min.X + TerrariaTheme.Px(2f) + (size.X - knob - TerrariaTheme.Px(3f)) * anim;
        var knobCenter = new Vector2(x + knob * 0.5f, min.Y + TerrariaTheme.Px(2f) + knob * 0.5f);
        dl.AddCircleFilled(knobCenter, knob * 0.5f, TerrariaTheme.U32(UiTheme.Alpha(UiTheme.Text3, 1f - anim)));
        dl.AddCircleFilled(knobCenter, knob * 0.5f, TerrariaTheme.U32(UiTheme.Alpha(new Vector4(1f, 1f, 1f, 1f), anim)));
    }

    private static void DrawCheckbox(ImDrawListPtr dl, Vector2 min, Vector2 max, bool value, bool hovered)
    {
        var radius = TerrariaTheme.Px(6f);
        dl.AddRectFilled(min, max, TerrariaTheme.U32(UiTheme.Field), radius);
        dl.AddRect(min, max, TerrariaTheme.U32(UiTheme.FieldStroke), radius);
        if (!value) return;
        if (Motion.Enabled)
            dl.AddRectFilled(min - new Vector2(TerrariaTheme.Px(2.5f)), max + new Vector2(TerrariaTheme.Px(2.5f)),
                TerrariaTheme.U32(UiTheme.Glow), radius);
        GradientRounded(dl, min, max, UiTheme.Accent, UiTheme.Accent2, radius, 150f);
        var w = max.X - min.X;
        var h = max.Y - min.Y;
        var p1 = new Vector2(min.X + w * 0.22f, min.Y + h * 0.52f);
        var p2 = new Vector2(min.X + w * 0.42f, min.Y + h * 0.73f);
        var p3 = new Vector2(min.X + w * 0.80f, min.Y + h * 0.28f);
        dl.AddLine(p1, p2, TerrariaTheme.U32(new Vector4(1f, 1f, 1f, 1f)), TerrariaTheme.Px(3.4f));
        dl.AddLine(p2, p3, TerrariaTheme.U32(new Vector4(1f, 1f, 1f, 1f)), TerrariaTheme.Px(3.4f));
    }

    private static void DrawSegItem(ImDrawListPtr dl, Vector2 min, Vector2 max, string text, bool selected, bool hovered)
    {
        var radius = TerrariaTheme.Px(8f);
        if (selected)
        {
            dl.AddRectFilled(min + new Vector2(0f, TerrariaTheme.Px(2f)),
                max + new Vector2(0f, TerrariaTheme.Px(5f)),
                TerrariaTheme.U32(new Vector4(0f, 0f, 0f, 0.10f)), radius);
            dl.AddRectFilled(min + new Vector2(0f, TerrariaTheme.Px(1f)),
                max + new Vector2(0f, TerrariaTheme.Px(3f)),
                TerrariaTheme.U32(new Vector4(0f, 0f, 0f, 0.12f)), radius);
            dl.AddRectFilled(min, max, TerrariaTheme.U32(UiTheme.SegPill), radius);
        }
        var color = selected || hovered ? UiTheme.Text : UiTheme.Text2;
        var size = TerrariaTheme.Px(12.5f);
        var textSize = Measure(text, size);
        DrawText(dl, new Vector2(min.X + ((max.X - min.X) - textSize.X) * 0.5f,
            min.Y + ((max.Y - min.Y) - textSize.Y) * 0.5f), size, color, text);
    }

    private static void DrawComboHeader(ImDrawListPtr dl, Vector2 min, Vector2 max, string text, bool open, bool hovered)
    {
        var radius = TerrariaTheme.Px(10f);
        dl.AddRectFilled(min, max, TerrariaTheme.U32(UiTheme.Field), radius);
        dl.AddRect(min, max, TerrariaTheme.U32(hovered ? UiTheme.Alpha(UiTheme.Accent, 0.45f) : UiTheme.FieldStroke), radius);
        var size = TerrariaTheme.Px(14f);
        var textSize = Measure(text, size);
        DrawText(dl, new Vector2(min.X + TerrariaTheme.Px(12f), min.Y + ((max.Y - min.Y) - textSize.Y) * 0.5f),
            size, UiTheme.Text, text);
        var cx = max.X - TerrariaTheme.Px(14f);
        var cy = (min.Y + max.Y) * 0.5f;
        var dirY = open ? -1f : 1f;
        var arrow = TerrariaTheme.Px(4.5f);
        dl.AddTriangleFilled(
            new Vector2(cx - arrow, cy + arrow * 0.5f * dirY),
            new Vector2(cx + arrow, cy + arrow * 0.5f * dirY),
            new Vector2(cx, cy - arrow * 0.8f * dirY),
            TerrariaTheme.U32(UiTheme.Accent));
    }

    private static void DrawComboItem(ImDrawListPtr dl, Vector2 min, Vector2 max, string text, bool selected, bool hovered)
    {
        var radius = TerrariaTheme.Px(8f);
        if (hovered) dl.AddRectFilled(min, max, TerrariaTheme.U32(UiTheme.Soft), radius);
        var size = TerrariaTheme.Px(14f);
        var textSize = Measure(text, size);
        DrawText(dl, new Vector2(min.X + TerrariaTheme.Px(12f), min.Y + ((max.Y - min.Y) - textSize.Y) * 0.5f),
            size, selected ? UiTheme.Accent : UiTheme.Text2, text);
        if (!selected) return;
        var right = max.X - TerrariaTheme.Px(12f);
        var cy = (min.Y + max.Y) * 0.5f;
        var s = TerrariaTheme.Px(5f);
        dl.AddLine(new Vector2(right - s, cy), new Vector2(right - s * 0.3f, cy + s * 0.6f),
            TerrariaTheme.U32(UiTheme.Accent), TerrariaTheme.Px(2f));
        dl.AddLine(new Vector2(right - s * 0.3f, cy + s * 0.6f), new Vector2(right + s, cy - s * 0.7f),
            TerrariaTheme.U32(UiTheme.Accent), TerrariaTheme.Px(2f));
    }

    private static void DrawTextField(Vector2 min, Vector2 max, string text, bool editing, bool hovered)
    {
        var dl = ImGui.GetWindowDrawList();
        var radius = TerrariaTheme.Px(10f);
        dl.AddRectFilled(min, max, TerrariaTheme.U32(UiTheme.Field), radius);
        dl.AddRect(min, max, TerrariaTheme.U32(editing ? UiTheme.Accent : UiTheme.FieldStroke), radius);
        if (editing)
            dl.AddRect(min - new Vector2(TerrariaTheme.Px(3f)), max + new Vector2(TerrariaTheme.Px(3f)),
                TerrariaTheme.U32(UiTheme.Soft), radius);
        var size = TerrariaTheme.Px(14f);
        var textSize = Measure(text, size);
        var insetX = TerrariaTheme.Px(12f);
        var visible = MathF.Max(max.X - min.X - insetX * 2f, 1f);
        var x = min.X + insetX - MathF.Max(textSize.X - visible, 0f);
        var y = min.Y + ((max.Y - min.Y) - textSize.Y) * 0.5f;
        dl.PushClipRect(min, max, true);
        DrawText(dl, new Vector2(x, y), size, UiTheme.Text, text);
        dl.PopClipRect();
        if (editing && (ImGui.GetFrameCount() / 30) % 2 == 0)
        {
            var caretW = TerrariaTheme.Px(2f);
            var caretX = MathF.Min(x + textSize.X, max.X - insetX * 0.5f - caretW);
            dl.AddRectFilled(new Vector2(caretX, y), new Vector2(caretX + caretW, y + textSize.Y),
                TerrariaTheme.U32(UiTheme.Text));
        }
    }

    private static void DrawCardBackdrop(ImDrawListPtr dl, Vector2 min, Vector2 max, float radius)
    {
        var hovered = ImGui.IsMouseHoveringRect(min, max);
        var fade = _cardFade;
        DrawCardShadow(dl, min, max, radius, hovered, fade);
        var stroke = hovered ? Mix(UiTheme.CardStroke, UiTheme.Accent, 0.3f) : UiTheme.CardStroke;
        GradientRounded(dl, min, max, UiTheme.Card0, UiTheme.Card1, radius, 180f);
        dl.AddRect(min, max, TerrariaTheme.U32(UiTheme.Alpha(stroke, stroke.W * fade)), radius);
        dl.AddLine(min + new Vector2(radius, TerrariaTheme.Px(1f)),
            new Vector2(max.X - radius, min.Y + TerrariaTheme.Px(1f)),
            TerrariaTheme.U32(UiTheme.Alpha(UiTheme.CardHi, UiTheme.CardHi.W * fade)), TerrariaTheme.Px(1f));
    }

    private static void DrawCardShadow(ImDrawListPtr dl, Vector2 min, Vector2 max, float radius, bool hovered, float fade = 1f)
    {
        (float Expand, float OffsetY, float Alpha)[] layers =
            [(2f, 4f, .055f), (6f, 8f, .04f), (11f, 12f, .028f), (17f, 16f, .018f), (24f, 20f, .01f)];
        foreach (var (expand, offsetY, alpha) in layers)
        {
            var e = TerrariaTheme.Px(expand);
            var lo = min - new Vector2(e);
            var hi = max + new Vector2(e);
            dl.AddRectFilled(lo, hi,
                TerrariaTheme.U32(UiTheme.Alpha(new Vector4(0f, 0f, 0f, 1f), alpha * fade)), radius + e);
        }
    }

    private static string CachedValue(uint id, float value, string format, string suffix)
    {
        if (ValueCache.TryGetValue(id, out var cached) && MathF.Abs(cached.Value - value) < 0.0001f)
            return cached.Text;
        var text = value.ToString(format, CultureInfo.InvariantCulture) + suffix;
        ValueCache[id] = (value, text);
        return text;
    }

    private static string DisplayLabel(string label)
    {
        if (string.IsNullOrEmpty(label)) return string.Empty;
        var mark = label.IndexOf("##", StringComparison.Ordinal);
        return mark < 0 ? label : label[..mark];
    }

    private static Vector4 Mix(Vector4 a, Vector4 b, float t) => new(
        a.X + (b.X - a.X) * t,
        a.Y + (b.Y - a.Y) * t,
        a.Z + (b.Z - a.Z) * t,
        a.W + (b.W - a.W) * t);

    private static string Glyph(char c) =>
        GlyphCache.TryGetValue(c, out var s) ? s : GlyphCache[c] = c.ToString();

    internal static Vector2 Measure(string text, float sizePx, float spacingEm = 0f)
    {
        sizePx = ImGuiContext.SnapSize(sizePx);
        var v = ImGui.GetFont().CalcTextSizeA(sizePx, float.MaxValue, 0f, text, null, out _);
        return spacingEm > 0f ? new Vector2(v.X + sizePx * spacingEm * text.Length, v.Y) : v;
    }

    internal static Vector2 MeasureWrapped(string text, float sizePx, float wrapWidth) =>
        ImGui.GetFont().CalcTextSizeA(ImGuiContext.SnapSize(sizePx), float.MaxValue, wrapWidth, text, null, out _);

    internal static void DrawText(ImDrawListPtr dl, Vector2 pos, float sizePx, Vector4 color, string text,
        Vector4? shadow = null, float spacingEm = 0f)
    {
        if (string.IsNullOrEmpty(text)) return;
        sizePx = ImGuiContext.SnapSize(sizePx);
        if (spacingEm > 0f)
        {
            var spacing = sizePx * spacingEm;
            for (var i = 0; i < text.Length; i++)
            {
                var glyph = Glyph(text[i]);
                if (shadow is { W: > 0 } shadowTint)
                    dl.AddText(ImGui.GetFont(), sizePx, pos + new Vector2(TerrariaTheme.Px(1f)),
                        TerrariaTheme.U32(shadowTint), glyph, null, 0f, null);
                dl.AddText(ImGui.GetFont(), sizePx, pos, TerrariaTheme.U32(color), glyph, null, 0f, null);
                pos.X += Measure(glyph, sizePx).X + spacing;
            }
            return;
        }
        if (shadow is { W: > 0 } tint)
            dl.AddText(ImGui.GetFont(), sizePx, pos + new Vector2(TerrariaTheme.Px(1f)),
                TerrariaTheme.U32(tint), text, null, 0f, null);
        dl.AddText(ImGui.GetFont(), sizePx, pos, TerrariaTheme.U32(color), text, null, 0f, null);
    }

    internal static void DrawTextWrapped(ImDrawListPtr dl, Vector2 pos, float sizePx, Vector4 color,
        string text, float wrapWidth)
    {
        if (string.IsNullOrEmpty(text)) return;
        dl.AddText(ImGui.GetFont(), ImGuiContext.SnapSize(sizePx), pos, TerrariaTheme.U32(color), text, null, wrapWidth, null);
    }

    internal static void DrawCaps(ImDrawListPtr dl, Vector2 pos, float sizePx, Vector4 color, string text,
        float spacingEm = 0.28f)
    {
        if (string.IsNullOrEmpty(text)) return;
        sizePx = ImGuiContext.SnapSize(sizePx);
        var spacing = sizePx * spacingEm;
        for (var i = 0; i < text.Length; i++)
        {
            var glyph = Glyph(text[i]);
            dl.AddText(ImGui.GetFont(), sizePx, pos, TerrariaTheme.U32(color), glyph, null, 0f, null);
            pos.X += Measure(glyph, sizePx).X + spacing;
        }
    }

    internal static Vector2 MeasureCaps(string text, float sizePx, float spacingEm = 0.28f)
    {
        if (string.IsNullOrEmpty(text)) return default;
        sizePx = ImGuiContext.SnapSize(sizePx);
        var spacing = sizePx * spacingEm;
        var total = 0f;
        var height = 0f;
        for (var i = 0; i < text.Length; i++)
        {
            var glyphSize = Measure(Glyph(text[i]), sizePx);
            total += glyphSize.X + spacing;
            height = MathF.Max(height, glyphSize.Y);
        }
        return new Vector2(MathF.Max(total - spacing, 0f), height);
    }

    internal static void DrawIcon(ImDrawListPtr dl, Vector2 min, Vector2 max, Icon icon, Vector4 color, float stroke)
    {
        var size = max - min;
        var thickness = TerrariaTheme.Px(stroke);
        var col = TerrariaTheme.U32(color);
        Vector2 P(float x, float y) => min + new Vector2(x / 24f * size.X, y / 24f * size.Y);
        switch (icon)
        {
            case Icon.Sliders:
                dl.AddLine(P(4, 8), P(20, 8), col, thickness);
                dl.AddLine(P(4, 16), P(20, 16), col, thickness);
                dl.AddCircleFilled(P(15, 8), TerrariaTheme.Px(2.2f), col);
                dl.AddCircleFilled(P(9, 16), TerrariaTheme.Px(2.2f), col);
                break;
            case Icon.Sparkle:
                Sparkle(dl, P, 12f, 9f, 8f, col);
                Sparkle(dl, P, 18.6f, 17.2f, 3.6f, col);
                break;
            case Icon.Terminal:
                dl.PathLineTo(P(3, 7));
                dl.PathLineTo(P(6, 4));
                dl.PathLineTo(P(18, 4));
                dl.PathLineTo(P(21, 7));
                dl.PathLineTo(P(21, 17));
                dl.PathLineTo(P(18, 20));
                dl.PathLineTo(P(6, 20));
                dl.PathLineTo(P(3, 17));
                dl.PathStroke(col, ImDrawFlags.Closed, thickness);
                dl.PathLineTo(P(7, 9));
                dl.PathLineTo(P(10, 12));
                dl.PathLineTo(P(7, 15));
                dl.PathStroke(col, ImDrawFlags.None, thickness);
                dl.PathLineTo(P(12.5f, 15));
                dl.PathLineTo(P(17, 15));
                dl.PathStroke(col, ImDrawFlags.None, thickness);
                break;
            case Icon.ChevronDown:
                dl.PathLineTo(P(6, 9));
                dl.PathLineTo(P(12, 15));
                dl.PathLineTo(P(18, 9));
                dl.PathStroke(col, ImDrawFlags.None, thickness);
                break;
            case Icon.ChevronLeft:
                dl.PathLineTo(P(15, 6));
                dl.PathLineTo(P(9, 12));
                dl.PathLineTo(P(15, 18));
                dl.PathStroke(col, ImDrawFlags.None, thickness);
                break;
            case Icon.ChevronRight:
                dl.PathLineTo(P(9, 6));
                dl.PathLineTo(P(15, 12));
                dl.PathLineTo(P(9, 18));
                dl.PathStroke(col, ImDrawFlags.None, thickness);
                break;
            case Icon.Close:
                dl.AddLine(P(6, 6), P(18, 18), col, thickness);
                dl.AddLine(P(18, 6), P(6, 18), col, thickness);
                break;
            case Icon.Star:
                dl.PathLineTo(P(12, 2));
                dl.PathLineTo(P(14.4f, 7.8f));
                dl.PathLineTo(P(20, 10));
                dl.PathLineTo(P(14.4f, 12.2f));
                dl.PathLineTo(P(12, 18));
                dl.PathLineTo(P(9.6f, 12.2f));
                dl.PathLineTo(P(4, 10));
                dl.PathLineTo(P(9.6f, 7.8f));
                dl.PathFillConvex(col);
                Sparkle(dl, P, 19f, 18f, 4f, col);
                break;
            case Icon.Sword:
                dl.PathLineTo(P(14.5f, 4));
                dl.PathLineTo(P(20, 9.5f));
                dl.PathLineTo(P(8.5f, 21));
                dl.PathLineTo(P(3, 21));
                dl.PathLineTo(P(3, 15.5f));
                dl.PathStroke(col, ImDrawFlags.Closed, thickness);
                dl.AddLine(P(13, 6), P(18, 11), col, thickness);
                break;
            case Icon.Broom:
                dl.PathLineTo(P(17, 3));
                dl.PathLineTo(P(21, 7));
                dl.PathLineTo(P(12.5f, 15.5f));
                dl.PathLineTo(P(8.5f, 11.5f));
                dl.PathStroke(col, ImDrawFlags.Closed, thickness);
                dl.PathLineTo(P(8.5f, 11.5f));
                dl.PathLineTo(P(4, 16));
                dl.PathLineTo(P(3, 21));
                dl.PathLineTo(P(8, 20));
                dl.PathLineTo(P(12.5f, 15.5f));
                dl.PathStroke(col, ImDrawFlags.None, thickness);
                break;
            case Icon.Wand:
                dl.AddLine(P(4, 20), P(15, 9), col, thickness);
                dl.PathLineTo(P(17.5f, 3.5f));
                dl.PathLineTo(P(18.5f, 6));
                dl.PathLineTo(P(21, 7));
                dl.PathLineTo(P(18.5f, 8));
                dl.PathLineTo(P(17.5f, 10.5f));
                dl.PathLineTo(P(16.5f, 8));
                dl.PathLineTo(P(14, 7));
                dl.PathLineTo(P(16.5f, 6));
                dl.PathFillConvex(col);
                break;
            case Icon.Seal:
                dl.AddCircle(P(12, 12), TerrariaTheme.Px(8f), col, 0, thickness);
                dl.PathLineTo(P(12, 7));
                dl.PathLineTo(P(15.5f, 12));
                dl.PathLineTo(P(12, 17));
                dl.PathLineTo(P(8.5f, 12));
                dl.PathStroke(col, ImDrawFlags.None, thickness);
                break;
        }
    }

    private static void Sparkle(ImDrawListPtr dl, Func<float, float, Vector2> pointAt, float cx, float cy, float r, uint col)
    {
        dl.PathLineTo(pointAt(cx, cy - r));
        dl.PathLineTo(pointAt(cx + r * 0.3f, cy - r * 0.3f));
        dl.PathLineTo(pointAt(cx + r, cy));
        dl.PathLineTo(pointAt(cx + r * 0.3f, cy + r * 0.3f));
        dl.PathLineTo(pointAt(cx, cy + r));
        dl.PathLineTo(pointAt(cx - r * 0.3f, cy + r * 0.3f));
        dl.PathLineTo(pointAt(cx - r, cy));
        dl.PathLineTo(pointAt(cx - r * 0.3f, cy - r * 0.3f));
        dl.PathFillConvex(col);
    }

    /// <summary>带圆角的线性渐变矩形：凸多边形逐顶点按渐变方向取色。</summary>
    internal static void GradientRounded(ImDrawListPtr dl, Vector2 min, Vector2 max,
        Vector4 c0, Vector4 c1, float radius, float angleDeg)
        => GradientRoundedCorners(dl, min, max, c0, c1, radius, angleDeg, ImDrawFlags.RoundCornersAll);

    /// <summary>
    /// 选择性圆角的线性渐变矩形：corners 指定要圆的角，其余角保持直角（直角走尖角顶点）。
    /// 侧栏这类一侧贴窗口边缘的构件用它只圆外面那个角。
    /// </summary>
    /// <summary>
    /// 圆角矩形路径点（窗口填充与壁纸网格共用同一份构造）：每角 arcSegments 个弧点，
    /// 直角处 1 个点，顺序 左上弧→右上弧→右下弧→左下弧，屏幕坐标系顺时针闭合。
    /// </summary>
    internal static Vector2[] RoundedRectPath(Vector2 min, Vector2 max, float radius,
        ImDrawFlags corners, int arcSegments)
    {
        radius = Math.Clamp(radius, 0f, MathF.Min(max.X - min.X, max.Y - min.Y) * 0.5f);
        Span<Vector2> centers =
        [
            new(min.X + radius, min.Y + radius), new(max.X - radius, min.Y + radius),
            new(max.X - radius, max.Y - radius), new(min.X + radius, max.Y - radius),
        ];
        Span<float> arcStart = [MathF.PI, MathF.PI * 1.5f, 0f, MathF.PI * 0.5f];
        Span<ImDrawFlags> cornerFlag =
        [
            ImDrawFlags.RoundCornersTopLeft, ImDrawFlags.RoundCornersTopRight,
            ImDrawFlags.RoundCornersBottomRight, ImDrawFlags.RoundCornersBottomLeft,
        ];
        Span<Vector2> sharp = [min, new(max.X, min.Y), max, new(min.X, max.Y)];
        var points = new Vector2[4 * arcSegments];
        var count = 0;
        for (var c = 0; c < 4; c++)
        {
            if (radius > 0.5f && (corners & cornerFlag[c]) != 0)
            {
                for (var k = 0; k < arcSegments; k++)
                {
                    var a = arcStart[c] + k * (MathF.PI * 0.5f / (arcSegments - 1));
                    points[count++] = centers[c] + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius;
                }
            }
            else
            {
                points[count++] = sharp[c];
            }
        }
        return points[..count];
    }

    internal static void GradientRoundedCorners(ImDrawListPtr dl, Vector2 min, Vector2 max,
        Vector4 c0, Vector4 c1, float radius, float angleDeg, ImDrawFlags corners)
    {
        if (MathF.Max(c0.W, c1.W) <= 0f) return;
        var points = RoundedRectPath(min, max, radius, corners, corners == ImDrawFlags.RoundCornersAll ? 8 : 4);
        var rad = angleDeg * (MathF.PI / 180f);
        var dir = new Vector2(MathF.Sin(rad), -MathF.Cos(rad));
        var p0 = Vector2.Dot(min, dir);
        var p1 = Vector2.Dot(max, dir);
        var lo = MathF.Min(p0, p1);
        var span = MathF.Max(MathF.Abs(p1 - p0), 0.001f);
        var first = dl.VtxBuffer.Size;
        fixed (Vector2* p = points)
            dl.AddConvexPolyFilled(p, points.Length, TerrariaTheme.U32(c0));
        for (var k = 0; k < points.Length; k++)
        {
            var t = Math.Clamp((Vector2.Dot(points[k], dir) - lo) / span, 0f, 1f);
            WriteFillVertex(dl, first, k, TerrariaTheme.U32(new Vector4(
                c0.X + (c1.X - c0.X) * t,
                c0.Y + (c1.Y - c0.Y) * t,
                c0.Z + (c1.Z - c0.Z) * t,
                c0.W + (c1.W - c0.W) * t)));
        }
    }

    internal static int FillVertex(ImDrawListPtr dl, int first, int k) =>
        (dl.Flags & ImDrawListFlags.AntiAliasedFill) != 0 ? first + (k << 1) : first + k;

    internal static void WriteFillVertex(ImDrawListPtr dl, int first, int k, uint col) =>
        ((ImDrawVert*)dl.NativePtr->VtxBuffer.Data)[FillVertex(dl, first, k)].col = col;
}
