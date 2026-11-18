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
        switch (row.Kind)
        {
            case "header": DrawHeader(P, row); break;
            case "toggle": DrawToggle(S, P, row); break;
            case "range": DrawRange(S, row); break;
            case "cycle": DrawCycle(S, P, row); break;
            case "color": DrawColor(S, P, row); break;
            case "hue":
            case "saturation":
            case "lightness": DrawColorPart(S, row); break;
            case "data": DrawData(S, P, row); break;
        }
    }

    private static bool Bool(ModConfigEntry e, ConfigOption o) => ModConfigLoader.Read(e, o) is bool b && b;

    private static double Num(ModConfigEntry e, ConfigOption o) =>
        ModConfigLoader.Read(e, o) is IConvertible v ? v.ToDouble(CultureInfo.InvariantCulture) : 0;

    private static int Index(ModConfigEntry e, ConfigOption o)
    {
        var value = ModConfigLoader.Read(e, o);
        for (var i = 0; i < o.Choices.Length; i++)
        {
            if (Equals(o.Choices[i], value)) return i;
            if (string.Equals(o.Choices[i]?.ToString(), value?.ToString(), StringComparison.Ordinal)) return i;
        }

        return -1;
    }

    private static string Hex(Color color) =>
        "#" + color.R.ToString("x2") + color.G.ToString("x2") + color.B.ToString("x2");

    private static void DrawHeader(PartsData P, Row row)
    {
        var r = ControlAnchor._gridItemRegion;
        var divider = P.Divider;
        divider.Size = new Vector2(r.Width, divider.Size.Y);
        divider.Location = new Vector2(r.X + r.Width / 2, r.Y + r.Height - divider.Size.Y / 2);
        GUIPanel.Draw(divider, false, NoColor(), NoColor(), NoColor());
        P.Header.Alignment = (LayoutCalculator.AnchorType)(MIDDLE | ALIGN_CENTER);
        P.Header.Location = new Vector2(r.X + r.Width / 2, r.Y + r.Height / 2 - 2);
        GUIString.Draw(P.Header, row.O.HeaderText(row.Entry.Instance) ?? "", new Color(255, 255, 255), false);
    }

    private static void DrawToggle(SettingsOverlay_Layout S, PartsData P, Row row)
    {
        var entry = row.Entry;
        var o = row.O;
        var st = State(row);
        var t = S.ToggleTemplate;
        var state = GUITransactionButton.Draw(t.ToggleButton, (Item)null!, o.Label(entry.Instance), false, ref st.Scale,
            false, Focus(), false, false);
        if (state == GUITransactionButton.InputState.Clicked && !RowBlocked())
        {
            ModConfigLoader.Set(entry, o, !Bool(entry, o));
            SoundEngine.PlaySound(TICK);
        }
        var on = Bool(entry, o);
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
        var onColor = on ? t.Option1Label.Color : t.Option1DisabledLabelColour;
        var offColor = !on ? t.Option2Label.Color : t.Option2DisabledLabelColour;
        Label(P, t.Option1Label, t.Option1Label.Location.X, t.Option1Enabled.Size.X,
            Locale("Mobile.ToggleSettingOn"), onColor);
        Label(P, t.Option2Label, t.Option2Label.Location.X, t.Option2Enabled.Size.X,
            Locale("Mobile.ToggleSettingOff"), offColor);
    }

    private delegate bool SliderDrawer(Slider_Layout layout, bool disablePick, ref float value,
                                       GUISlider.DragState drag, bool forceOver);

    private static float? DrawSlider(SettingsOverlay_Layout S, Row row, string title, Slider_Layout layout, SliderDrawer draw, float value01)
    {
        var t = S.SliderTemplate;
        var st = State(row);
        GUITransactionButton.Draw(t.Title, (Item)null!, title, false, ref st.Scale, false, Focus(), false, false);
        var drag = st.Drag ??= new GUISlider.DragState();
        st.Value = value01;
        var r = ControlAnchor._gridItemRegion;
        bool OnTrack(float x) => GUISlider.IsCursorOver(new Vector2(x, r.Y + r.Height / 2), layout);
        var pad = r.Height / 2;
        if (_slider == null && Main.mouseLeft && _pressInList
            && !_dragged && !_guard
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

    private static void DrawValue(SettingsOverlay_Layout S, string text)
    {
        var t = S.SliderTemplate;
        GUIString.Draw(t.Value, text, t.Value.Color, false);
    }

    private static void DrawRange(SettingsOverlay_Layout S, Row row)
    {
        var entry = row.Entry;
        var o = row.O;
        SliderDrawer draw = (Slider_Layout l, bool disable, ref float value, GUISlider.DragState drag, bool force) =>
            GUISlider.Draw(l, disable, ref value, drag, null, force, -1, -1, false);
        var v = DrawSlider(S, row, o.Label(entry.Instance), S.SliderTemplate.Option, draw,
            (float)((Num(entry, o) - o.Min) / (o.Max - o.Min)));
        if (v != null) ModConfigLoader.Set(entry, o, o.Min + v.Value * (o.Max - o.Min));
        var stepText = o.Step.ToString(CultureInfo.InvariantCulture);
        var digits = 0;
        var dot = stepText.IndexOf('.');
        if (dot >= 0) digits = stepText.Length - dot - 1;
        DrawValue(S, Num(entry, o).ToString(digits == 0 ? "F0" : "F" + digits, CultureInfo.InvariantCulture));
    }

    private static void DrawCycle(SettingsOverlay_Layout S, PartsData P, Row row)
    {
        var entry = row.Entry;
        var o = row.O;
        var st = State(row);
        var index = Index(entry, o);
        var state = GUITransactionButton.Draw(S.ToggleTemplate.ToggleButton, (Item)null!, o.Label(entry.Instance), false,
            ref st.Scale, false, Focus(), false, false);
        var value = S.SliderTemplate.Value;
        P.Cycle.Location = new Vector2(CYCLE_X, value.Location.Y);
        var text = index >= 0 ? o.ChoiceLabel(entry.Instance, index) : "";
        GUIString.Draw(P.Cycle, "<  " + text + "  >",
            value.Color, false);
        if (state == GUITransactionButton.InputState.Clicked && !RowBlocked())
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
        var key = row.Entry.Name + "." + row.O.Name;
        if (!ColorStates.TryGetValue(key, out var state))
        {
            ColorStates[key] = state = new ColorState();
        }
        var hex = Hex((Il2CppMicrosoft.Xna.Framework.Graphics.Color)(ModConfigLoader.Read(row.Entry, row.O) ?? new Il2CppMicrosoft.Xna.Framework.Graphics.Color(255, 255, 255)));
        if (state.Hex != hex)
        {
            state.Hex = hex;
            state.Hsl = HexToHsl(hex);
        }
        return state;
    }

    private static void DrawColor(SettingsOverlay_Layout S, PartsData P, Row row)
    {
        var entry = row.Entry;
        var o = row.O;
        var st = State(row);
        var t = S.ToggleTemplate;
        GUITransactionButton.Draw(t.ToggleButton, (Item)null!, o.Label(entry.Instance), false, ref st.Scale,
            false, Focus(), false, false);
        var hex = Hex((Il2CppMicrosoft.Xna.Framework.Graphics.Color)(ModConfigLoader.Read(entry, o) ?? new Il2CppMicrosoft.Xna.Framework.Graphics.Color(255, 255, 255)));
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
        GUIString.Draw(value, hex, value.Color, false);
    }

    private static void DrawColorPart(SettingsOverlay_Layout S, Row row)
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
            v = DrawSlider(S, row, title, layout, draw, cs.Hsl[index]);
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
        DrawValue(S, (int)Math.Round(cs.Hsl[index] * 100, MidpointRounding.AwayFromZero) + "%");
    }

    private static void DrawData(SettingsOverlay_Layout S, PartsData P, Row row)
    {
        var entry = row.Entry;
        var o = row.O;
        var value = ModConfigLoader.Read(entry, o);
        var t = S.SliderTemplate;
        var st = State(row);
        GUITransactionButton.Draw(t.Title, (Item)null!, o.Label(entry.Instance), false, ref st.Scale, false, Focus(), false, false);
        DrawValue(S, value is Il2CppMicrosoft.Xna.Framework.Graphics.Color color
            ? ConfigOption.FormatColor(color)
            : value?.ToString() ?? "");
    }
}

