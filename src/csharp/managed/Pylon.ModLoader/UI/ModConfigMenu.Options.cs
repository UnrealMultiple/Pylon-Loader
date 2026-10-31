using System.Globalization;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;

namespace Pylon.ModLoader.UI;

public static partial class ModConfigMenu
{
    private static void DrawOption(SettingsOverlay_Layout S, PartsData P, Row row)
    {
        var enabled = ModConfigLoader.IsEnabled(row.Entry, row.O);
        switch (row.Kind)
        {
            case "header": DrawHeader(P, row); break;
            case "toggle": DrawToggle(S, P, row, enabled); break;
            case "range": DrawRange(S, row, enabled); break;
            case "radio": DrawRadio(S, P, row, enabled); break;
            case "dropdown": DrawDropdown(S, row, enabled); break;
            case "cycle": DrawCycle(S, P, row, enabled); break;
            case "color": DrawColor(S, P, row, enabled); break;
            case "hue":
            case "saturation":
            case "lightness": DrawColorPart(S, row, enabled); break;
            case "button":
            case "link": DrawAction(S, row, enabled); break;
        }
    }

    private static void DrawHeader(PartsData P, Row row)
    {
        var r = ControlAnchor._gridItemRegion;
        var divider = P.Divider;
        divider.Size = new Vector2(r.Width, divider.Size.Y);
        divider.Location = new Vector2(r.X + r.Width / 2, r.Y + r.Height - divider.Size.Y / 2);
        GUIPanel.Draw(divider, false, NoColor(), NoColor(), NoColor());
        var align = row.O.Align;
        var x = align switch
        {
            HeaderAlign.Left => r.X + HEADER_PAD,
            HeaderAlign.Right => r.X + r.Width - HEADER_PAD,
            _ => r.X + r.Width / 2,
        };
        P.Header.Alignment = (LayoutCalculator.AnchorType)(MIDDLE | AlignX(align));
        P.Header.Location = new Vector2(x, r.Y + r.Height / 2 - 2);
        GUIString.Draw(P.Header, row.O.Label, HeaderColor(row.O.Color), false);
    }

    private static int AlignX(HeaderAlign align) => align switch
    {
        HeaderAlign.Left => ALIGN_LEFT,
        HeaderAlign.Right => ALIGN_RIGHT,
        _ => ALIGN_CENTER,
    };

    private static Color HeaderColor(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return new Color(255, 255, 255);
        var n = int.Parse(hex[1..], NumberStyles.HexNumber);
        return new Color((n >> 16) & 255, (n >> 8) & 255, n & 255);
    }

    private static void DrawToggle(SettingsOverlay_Layout S, PartsData P, Row row, bool enabled)
    {
        var entry = row.Entry;
        var o = row.O;
        var st = State(row);
        var t = enabled ? S.ToggleTemplate : S.DisabledToggleTemplate;
        var state = GUITransactionButton.Draw(t.ToggleButton, (Item)null!, o.Label, !enabled, ref st.Scale,
            false, Focus(), false, false);
        if (enabled && state == GUITransactionButton.InputState.Clicked && !RowBlocked())
        {
            ModConfigLoader.Set(entry, o, !View(entry).GetBool(o.Key));
            SoundEngine.PlaySound(TICK);
        }
        var on = View(entry).GetBool(o.Key);
        if (on)
        {
            GUIPanel.Draw(t.Option2Disabled, false, NoColor(), NoColor(), NoColor());
            GUIPanel.Draw(t.Option1Enabled, false, NoColor(), NoColor(), NoColor());
        }
        else
        {
            GUIPanel.Draw(t.Option1Disabled, false, NoColor(), NoColor(), NoColor());
            GUIPanel.Draw(t.Option2Enabled, false, NoColor(), NoColor(), NoColor());
        }
        var onColor = on && enabled ? t.Option1Label.Color : t.Option1DisabledLabelColour;
        var offColor = !on && enabled ? t.Option2Label.Color : t.Option2DisabledLabelColour;
        Label(P, t.Option1Label, t.Option1Label.Location.X, t.Option1Enabled.Size.X,
            Locale("Mobile.ToggleSettingOn"), onColor);
        Label(P, t.Option2Label, t.Option2Label.Location.X, t.Option2Enabled.Size.X,
            Locale("Mobile.ToggleSettingOff"), offColor);
    }

    private delegate bool SliderDrawer(Slider_Layout layout, bool disablePick, ref float value,
                                       GUISlider.DragState drag, bool forceOver);

    private static float? DrawSlider(SettingsOverlay_Layout S, Row row, string title, bool enabled,
        Slider_Layout layout, float value01, SliderDrawer draw)
    {
        var t = S.SliderTemplate;
        var st = State(row);
        GUITransactionButton.Draw(t.Title, (Item)null!, title, !enabled, ref st.Scale, false, Focus(), false, false);
        var drag = st.Drag ??= new GUISlider.DragState();
        st.Value = value01;
        var r = ControlAnchor._gridItemRegion;
        bool OnTrack(float x) => GUISlider.IsCursorOver(new Vector2(x, r.Y + r.Height / 2), layout);
        var pad = r.Height / 2;
        if (enabled && _slider == null && Main.mouseLeft && _pressInList
            && !_dragged && !_guard && !Modal()
            && Main.mouseY >= r.Y && Main.mouseY <= r.Y + r.Height
            && (OnTrack(Main.mouseX) || OnTrack(Main.mouseX - pad) || OnTrack(Main.mouseX + pad)))
        {
            _slider = row.Key;
        }
        var mine = _slider == row.Key;
        var dragging = draw(layout, !mine, ref st.Value, drag, mine && Main.mouseLeft);
        drag.wasDragging = dragging && mine;
        return mine ? st.Value : null;
    }

    private static void DrawValue(SettingsOverlay_Layout S, string text, bool enabled)
    {
        var t = S.SliderTemplate;
        GUIString.Draw(t.Value, text, enabled ? t.Value.Color : t.DisabledValueColor, false);
    }

    private static void DrawRange(SettingsOverlay_Layout S, Row row, bool enabled)
    {
        var entry = row.Entry;
        var o = row.O;
        SliderDrawer draw = (Slider_Layout l, bool disable, ref float value, GUISlider.DragState drag, bool force) =>
            GUISlider.Draw(l, disable, ref value, drag, null, force, -1, -1, false);
        var v = DrawSlider(S, row, o.Label, enabled, S.SliderTemplate.Option,
            (float)((View(entry).GetNumber(o.Key) - o.Min) / (o.Max - o.Min)), draw);
        if (v != null) ModConfigLoader.Set(entry, o, o.Min + v.Value * (o.Max - o.Min));
        var stepText = o.Step.ToString(CultureInfo.InvariantCulture);
        var digits = 0;
        var dot = stepText.IndexOf('.');
        if (dot >= 0) digits = stepText.Length - dot - 1;
        DrawValue(S, View(entry).GetNumber(o.Key).ToString(digits == 0 ? "F0" : "F" + digits, CultureInfo.InvariantCulture)
                      + (o.Suffix ?? ""), enabled);
    }

    private static void DrawRadio(SettingsOverlay_Layout S, PartsData P, Row row, bool enabled)
    {
        var entry = row.Entry;
        var o = row.O;
        var st = State(row);
        var t = S.ToggleTemplate;
        var state = GUITransactionButton.Draw(t.ToggleButton, (Item)null!, o.Label, !enabled, ref st.Scale,
            false, Focus(), false, false);
        var n = o.Choices.Length;
        var seg = Segments(t, n);
        if (enabled && state == GUITransactionButton.InputState.Clicked && !RowBlocked())
        {
            var picked = -1;
            for (var i = 0; i < n; i++)
            {
                var c = seg.AnchorPos(seg.XOf(i));
                if (Math.Abs(Main.mouseX - c.X) <= seg.Each / 2 && Math.Abs(Main.mouseY - c.Y) <= seg.Height / 2 + 4)
                    picked = i;
            }
            if (picked < 0) picked = (IndexOf(o.Choices, View(entry).GetString(o.Key)) + 1) % n;
            ModConfigLoader.Set(entry, o, o.Choices[picked]);
            SoundEngine.PlaySound(TICK);
        }
        var selected = IndexOf(o.Choices, View(entry).GetString(o.Key));
        var order = new List<int>();
        for (var i = 0; i < n; i++)
        {
            if (i != selected) order.Add(i);
        }
        if (selected >= 0) order.Add(selected);
        foreach (var i in order)
        {
            var piece = i == selected && enabled ? P.ChoiceOn : P.ChoiceOff;
            piece.Location = new Vector2(seg.XOf(i), seg.Y);
            piece.Size = new Vector2(seg.Each - 2, seg.Height);
            GUIPanel.Draw(piece, false, NoColor(), NoColor(), NoColor());
        }
        for (var i = 0; i < n; i++)
        {
            Label(P, t.Option1Label, seg.XOf(i), seg.Each, o.ChoiceLabels[i],
                i == selected && enabled ? t.Option1Label.Color : t.Option1DisabledLabelColour, true);
        }
    }

    private static void DrawTitle(SettingsOverlay_Layout S, string text, bool enabled)
    {
        var p = S.PulldownTemplate;
        GUIString.Draw(p.PulldownTitle, text, enabled ? p.PulldownTitle.Color : p.DisabledPulldownLabelColour, false);
    }

    private static void DrawDropdown(SettingsOverlay_Layout S, Row row, bool enabled)
    {
        var entry = row.Entry;
        var o = row.O;
        var st = State(row);
        var p = S.PulldownTemplate;
        var open = _popup != null && _popup.Row.Key == row.Key;
        var index = IndexOf(o.Choices, View(entry).GetString(o.Key));
        var value = index >= 0 && index < o.ChoiceLabels.Length ? o.ChoiceLabels[index] : "";
        var state = GUITransactionButton.Draw(p.PulldownButton, (Item)null!, value, !enabled, ref st.Scale,
            open, Focus(), false, false);
        DrawTitle(S, o.Label, enabled);
        if (enabled && state == GUITransactionButton.InputState.Clicked && !RowBlocked())
        {
            var popup = new PopupState
            {
                Row = row,
                Rect = ControlAnchor._gridItemRegion,
                Scales = new float[o.Choices.Length],
            };
            for (var i = 0; i < popup.Scales.Length; i++) popup.Scales[i] = 1f;
            _popup = popup;
            SoundEngine.PlaySound(TICK);
        }
    }

    private static void DrawPopup(SettingsOverlay_Layout S)
    {
        var pop = _popup;
        if (pop == null) return;
        var entry = pop.Row.Entry;
        var o = pop.Row.O;
        var P = Parts(S);
        var p = S.PulldownTemplate;
        var batch = Main.spriteBatch;
        batch.End();
        batch.Begin(SpriteSortMode.Deferred, null, null, null, null, null, NoMatrix(), true);
        GUIInputRegionManager.Instance.RegisterInputRegion(Rect(-4000, -4000, 12000, 12000));
        P.Greyout.Location = new Vector2(0, 0);
        P.Greyout.Size = new Vector2(12000, 12000);
        GUIPanel.Draw(P.Greyout, false, NoColor(), NoColor(), NoColor());

        var h = p.Option1.overloadedSize.Y;
        var step = p.Option2.Location.Y - p.Option1.Location.Y;
        var label = p.Option1.Label;
        var font = TryFont(label);
        var widest = 0f;
        if (font != null && label != null)
        {
            foreach (var choice in o.ChoiceLabels)
            {
                widest = Math.Max(widest, font.MeasureString(choice).X * label.Scale);
            }
        }
        var width = Math.Min(pop.Rect.Width, Math.Max(p.Option1.overloadedSize.X, widest + 40));
        var n = o.Choices.Length;
        var blockH = (n - 1) * step + h;
        var x0 = pop.Rect.X + pop.Rect.Width - width - 6;
        var frame = GUIPanel.Region(S.Backing);
        var lowest = frame.Y + frame.Height - blockH - POPUP_MARGIN;
        float y0 = pop.Rect.Y + pop.Rect.Height + 4;
        if (y0 > lowest) y0 = pop.Rect.Y - 4 - blockH;
        y0 = Math.Max(frame.Y + POPUP_MARGIN, Math.Min(y0, lowest));

        var tl = p.PulldownBackingTLOffset;
        var br = p.PulldownBackingBROffset;
        float bx = x0 + tl.X, by = y0 + tl.Y, bw = width - tl.X + br.X, bh = blockH - tl.Y + br.Y;
        P.PopBacking.Location = new Vector2(bx + bw / 2, by + bh / 2);
        P.PopBacking.Size = new Vector2(bw, bh);
        GUIPanel.Draw(P.PopBacking, false, NoColor(), NoColor(), NoColor());

        var saved = ControlAnchor._gridItemRegion;
        P.Option.overloadedSize = new Vector2(width, h);
        P.Option.Location = new Vector2(-width / 2, -h / 2);
        var selected = IndexOf(o.Choices, View(entry).GetString(o.Key));
        var close = false;
        try
        {
            for (var i = 0; i < n; i++)
            {
                ControlAnchor._gridItemRegion = Rect(x0, y0 + i * step, width, h);
                var state = GUITransactionButton.Draw(P.Option, (Item)null!, o.ChoiceLabels[i], false, ref pop.Scales[i],
                    i == selected, Focus(), false, false);
                if (state == GUITransactionButton.InputState.Clicked && pop.Armed && !_guard)
                {
                    ModConfigLoader.Set(entry, o, o.Choices[i]);
                    SoundEngine.PlaySound(TICK);
                    close = true;
                }
            }
        }
        finally
        {
            ControlAnchor._gridItemRegion = saved;
        }

        var inside = Main.mouseX >= bx && Main.mouseX <= bx + bw && Main.mouseY >= by && Main.mouseY <= by + bh;
        if (!Main.mouseLeft && !pop.Down) pop.Armed = true;
        if (pop.Armed && Main.mouseLeft)
        {
            if (!pop.Down)
            {
                pop.Down = true;
                pop.DownInside = inside;
            }
        }
        else if (pop.Down)
        {
            pop.Down = false;
            if (!pop.DownInside) close = true;
        }
        if (close)
        {
            _popup = null;
            Guard();
        }
    }

    private static void DrawCycle(SettingsOverlay_Layout S, PartsData P, Row row, bool enabled)
    {
        var entry = row.Entry;
        var o = row.O;
        var st = State(row);
        var index = IndexOf(o.Choices, View(entry).GetString(o.Key));
        var state = GUITransactionButton.Draw(S.ToggleTemplate.ToggleButton, (Item)null!, o.Label, !enabled,
            ref st.Scale, false, Focus(), false, false);
        var value = S.SliderTemplate.Value;
        P.Cycle.Location = new Vector2(CYCLE_X, value.Location.Y);
        GUIString.Draw(P.Cycle, "<  " + (index >= 0 ? o.ChoiceLabels[index] : "") + "  >",
            enabled ? value.Color : S.SliderTemplate.DisabledValueColor, false);
        if (enabled && state == GUITransactionButton.InputState.Clicked && !RowBlocked())
        {
            ModConfigLoader.Set(entry, o, o.Choices[(index + 1) % o.Choices.Length]);
            SoundEngine.PlaySound(TICK);
        }
    }

    private static float[] HexToHsl(string hex)
    {
        var r = int.Parse(hex[1..3], NumberStyles.HexNumber) / 255f;
        var g = int.Parse(hex[3..5], NumberStyles.HexNumber) / 255f;
        var b = int.Parse(hex[5..7], NumberStyles.HexNumber) / 255f;
        float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        if (Math.Abs(max - min) < 1e-9f) return [0f, 0f, l];
        var d = max - min;
        var s = l > 0.5f ? d / (2 - max - min) : d / (max + min);
        float h;
        if (Math.Abs(max - r) < 1e-9f) h = (g - b) / d + (g < b ? 6 : 0);
        else if (Math.Abs(max - g) < 1e-9f) h = (b - r) / d + 2;
        else h = (r - g) / d + 4;
        return [h / 6f, s, l];
    }

    private static float Channel(float p, float q, float t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1f / 6f) return p + (q - p) * 6 * t;
        if (t < 1f / 2f) return q;
        if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6;
        return p;
    }

    private static string HslToHex(float h, float s, float l)
    {
        float r = l, g = l, b = l;
        if (s > 0)
        {
            var q = l < 0.5f ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;
            r = Channel(p, q, h + 1f / 3f);
            g = Channel(p, q, h);
            b = Channel(p, q, h - 1f / 3f);
        }
        string Hex(float v) =>
            ((int)Math.Round(Math.Min(1, Math.Max(0, v)) * 255, MidpointRounding.AwayFromZero)).ToString("x2");
        return ("#" + Hex(r) + Hex(g) + Hex(b)).ToUpperInvariant();
    }

    private static ColorState ConfigColorState(Row row)
    {
        var key = row.Entry.Name + "." + row.O.Key;
        if (!ColorStates.TryGetValue(key, out var state))
        {
            ColorStates[key] = state = new ColorState();
        }
        var hex = View(row.Entry).GetString(row.O.Key);
        if (state.Hex != hex)
        {
            state.Hex = hex;
            state.Hsl = HexToHsl(hex);
        }
        return state;
    }

    private static void DrawColor(SettingsOverlay_Layout S, PartsData P, Row row, bool enabled)
    {
        var entry = row.Entry;
        var o = row.O;
        var st = State(row);
        var t = S.ToggleTemplate;
        GUITransactionButton.Draw(t.ToggleButton, (Item)null!, o.Label, !enabled, ref st.Scale,
            false, Focus(), false, false);
        var hex = View(entry).GetString(o.Key);
        var seg = Segments(t, 1);
        P.Swatch.Location = new Vector2(seg.XOf(0), seg.Y);
        P.Swatch.Size = new Vector2(seg.Each - 2, seg.Height);
        var r = int.Parse(hex[1..3], NumberStyles.HexNumber);
        var g = int.Parse(hex[3..5], NumberStyles.HexNumber);
        var b = int.Parse(hex[5..7], NumberStyles.HexNumber);
        GUIPanel.Draw(P.Swatch, false, NoColor(), NoColor(), NoColor());
        var box = GUIPanel.Region(P.Swatch);
        var inset = (int)SWATCH_INSET;
        Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value,
            Rect(box.X + inset, box.Y + inset, box.Width - inset * 2, box.Height - inset * 2),
            NoRect(), new Color(r, g, b, 255));
        var value = S.SliderTemplate.Value;
        GUIString.Draw(value, hex, enabled ? value.Color : S.SliderTemplate.DisabledValueColor, false);
    }

    private static void DrawColorPart(SettingsOverlay_Layout S, Row row, bool enabled)
    {
        var entry = row.Entry;
        var o = row.O;
        var t = S.SliderTemplate;
        var cs = ConfigColorState(row);
        var index = row.Kind switch
        {
            "hue" => 0,
            "saturation" => 1,
            _ => 2,
        };
        var title = "   " + Locale(row.Kind switch
        {
            "hue" => "Mobile.Hue",
            "saturation" => "Mobile.Saturation",
            _ => "Mobile.Lightness",
        });
        var layout = row.Kind switch
        {
            "hue" => t.HueOption,
            "saturation" => t.SaturationOption,
            _ => t.LightOption,
        };
        SliderDrawer draw = row.Kind == "hue"
            ? (Slider_Layout l, bool disable, ref float value, GUISlider.DragState drag, bool force) =>
                GUISlider.Draw(l, disable, ref value, drag, null, force, -1, -1, false)
            : row.Kind == "saturation"
                ? (Slider_Layout l, bool disable, ref float value, GUISlider.DragState drag, bool force) =>
                    GUISlider.DrawSaturation(l, disable, ref value, drag, force)
                : (Slider_Layout l, bool disable, ref float value, GUISlider.DragState drag, bool force) =>
                    GUISlider.DrawLightness(l, disable, ref value, drag, force);
        var savedH = Main.hBar;
        var savedS = Main.sBar;
        var savedL = Main.lBar;
        Main.hBar = cs.Hsl[0];
        Main.sBar = cs.Hsl[1];
        Main.lBar = cs.Hsl[2];
        float? v;
        try
        {
            v = DrawSlider(S, row, title, enabled, layout, cs.Hsl[index], draw);
        }
        finally
        {
            Main.hBar = savedH;
            Main.sBar = savedS;
            Main.lBar = savedL;
        }
        if (v != null && v.Value != cs.Hsl[index])
        {
            cs.Hsl[index] = v.Value;
            cs.Hex = HslToHex(cs.Hsl[0], cs.Hsl[1], cs.Hsl[2]);
            ModConfigLoader.Set(entry, o, cs.Hex);
        }
        DrawValue(S, (int)Math.Round(cs.Hsl[index] * 100, MidpointRounding.AwayFromZero) + "%", enabled);
    }

    private static void DrawAction(SettingsOverlay_Layout S, Row row, bool enabled)
    {
        var entry = row.Entry;
        var o = row.O;
        var st = State(row);
        var link = o.Kind == OptionKind.Link;
        // 两个模板类不是同一个类型，就在这里分开拿
        TransactionButton_Layout main, caption;
        if (link)
        {
            var t = S.LinkTemplate;
            main = t.LinkButton;
            caption = t.Caption;
        }
        else
        {
            var t = S.UIButtonOptionTemplate;
            main = t.Option;
            caption = t.Caption;
        }
        var captionText = Fit(caption, o.Text ?? "", caption.overloadedSize.X - 16);
        var focus = Focus();
        var s1 = GUITransactionButton.Draw(main, (Item)null!, o.Label, !enabled, ref st.Scale, false, focus, false, false);
        var s2 = GUITransactionButton.Draw(caption, (Item)null!, captionText, !enabled, ref st.Scale2, false, focus, false, false);
        if (!enabled || (s1 != GUITransactionButton.InputState.Clicked && s2 != GUITransactionButton.InputState.Clicked) || RowBlocked())
            return;
        SoundEngine.PlaySound(TICK);
        if (link) UnityEngine.Application.OpenURL(o.Url);
        else ModConfigLoader.Run(entry, o);
    }
}
