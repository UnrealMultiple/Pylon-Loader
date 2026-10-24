using System.Numerics;
using Pylon.NaviteAPI.ImGui;

namespace Pylon.ModLoader.ImGuiHost;

/// <summary>单套主题的全部视觉 token，数值与 HTML 原型的 CSS 变量块一一对应。</summary>
public sealed class UiPalette
{
    public string Id = string.Empty;
    public string Cn = string.Empty;
    public string Chip = string.Empty;
    public string En = string.Empty;
    public string Tag = string.Empty;
    public string Phrase = string.Empty;

    public Vector4 Win0;
    public Vector4 Win1;
    public Vector4 WinStroke;
    public Vector4 Side0;
    public Vector4 Side1;
    public Vector4 Hairline;
    public Vector4 Card0;
    public Vector4 Card1;
    public Vector4 CardStroke;
    public Vector4 CardHi;
    public Vector4 Text;
    public Vector4 Text2;
    public Vector4 Text3;
    public Vector4 Accent;
    public Vector4 AccentHi;
    public Vector4 Accent2;
    public Vector4 Soft;
    public Vector4 Glow;
    public Vector4 Good;
    public Vector4 Warn;
    public Vector4 Field;
    public Vector4 FieldStroke;
    public Vector4 SegPill;
    public Vector4 Pop;
    public Vector4 Shadow;

    public float Radius = 20f;
    public float CardRadius = 16f;

    public Vector4 Pv0;
    public Vector4 Pv1;
    public Vector4 PvAcc;
    public Vector4 PvText = UiTheme.Hex(0xFFFFFF);
    public Vector4 PvText2 = new(1f, 1f, 1f, 0.55f);
    public Vector4 PvText3 = new(1f, 1f, 1f, 0.82f);
    public bool PvTextShadow = true;

    public WallpaperParticles.Kind Particle = WallpaperParticles.Kind.None;
    public AmbientWallpaper.Glyph Glyph = AmbientWallpaper.Glyph.Star;
}

/// <summary>
/// 十套主题的 token 集与运行时过渡（切换呼吸、窗口淡入淡出）；ImGui 全局样式也在这里统一应用。
/// </summary>
public static class UiTheme
{
    public const int PaletteCount = 10;
    public const int Mica = 0;
    public const int Obsidian = 1;
    public const int Glacier = 2;
    public const int Blossom = 3;
    public const int Amber = 4;
    public const int Tide = 5;
    public const int Verdant = 6;
    public const int Ember = 7;
    public const int Zephyr = 8;
    public const int Terraria = 9;

    public static readonly UiPalette[] Palettes = [MicaPalette(), ObsidianPalette(), GlacierPalette(), BlossomPalette(),
        AmberPalette(), TidePalette(), VerdantPalette(), EmberPalette(), ZephyrPalette(), TerrariaPalette()];

    private const float SwapSeconds = 0.52f;
    private const float SwapDip = 0.5f;
    private const float SwapDesaturate = 0.45f;
    private const float SwapDim = 0.08f;

    private static int _current = Glacier;
    private static float _swapStart = float.NegativeInfinity;
    private static float _extraFade = 1f;
    private static float _fade = 1f;

    private static bool _idle = true;
    private static float _opacity = 1f;
    private static float _saturation = 1f;
    private static float _brightness = 1f;

    public static int Current
    {
        get => _current;
        set
        {
            var next = Math.Clamp(value, 0, PaletteCount - 1);
            if (next == _current) return;
            _current = next;
            _swapStart = Motion.Time;
            Apply();
        }
    }

    public static UiPalette Palette => Palettes[_current];

    /// <summary>启动时按持久化设置定位主题，不带切换过渡。</summary>
    internal static void Initialize(int index)
    {
        _current = Math.Clamp(index, 0, PaletteCount - 1);
        _swapStart = float.NegativeInfinity;
        Apply();
    }

    internal static float ExtraFade
    {
        get => _extraFade;
        set
        {
            _extraFade = Math.Clamp(value, 0f, 1f);
            Refresh();
        }
    }

    internal static void PushFade(float fade) => _fade = Math.Clamp(fade, 0f, 1f);

    internal static void PopFade() => _fade = 1f;

    public static Vector4 Win0 => Adjust(Palette.Win0);
    public static Vector4 Win1 => Adjust(Palette.Win1);
    public static Vector4 WinStroke => Adjust(Palette.WinStroke);
    public static Vector4 Side0 => Adjust(Palette.Side0);
    public static Vector4 Side1 => Adjust(Palette.Side1);
    public static Vector4 Hairline => Adjust(Palette.Hairline);
    public static Vector4 Card0 => Adjust(Palette.Card0);
    public static Vector4 Card1 => Adjust(Palette.Card1);
    public static Vector4 CardStroke => Adjust(Palette.CardStroke);
    public static Vector4 CardHi => Adjust(Palette.CardHi);
    public static Vector4 Text => Adjust(Palette.Text);
    public static Vector4 Text2 => Adjust(Palette.Text2);
    public static Vector4 Text3 => Adjust(Palette.Text3);
    public static Vector4 Accent => Adjust(Palette.Accent);
    public static Vector4 AccentHi => Adjust(Palette.AccentHi);
    public static Vector4 Accent2 => Adjust(Palette.Accent2);
    public static Vector4 Soft => Adjust(Palette.Soft);
    public static Vector4 Glow => Adjust(Palette.Glow);
    public static Vector4 Good => Adjust(Palette.Good);
    public static Vector4 Warn => Adjust(Palette.Warn);
    public static Vector4 Field => Adjust(Palette.Field);
    public static Vector4 FieldStroke => Adjust(Palette.FieldStroke);
    public static Vector4 SegPill => Adjust(Palette.SegPill);
    public static Vector4 Pop => Adjust(Palette.Pop);
    public static Vector4 Shadow => Adjust(Palette.Shadow);

    public static float Radius => Palette.Radius;
    public static float CardRadius => Palette.CardRadius;
    public static float PanelOpacity => 0.32f + 0.68f * UiSettings.Wallpaper;

    public static Vector4 Hex(uint rgb, float alpha = 1f) => new(
        ((rgb >> 16) & 0xFF) / 255f,
        ((rgb >> 8) & 0xFF) / 255f,
        (rgb & 0xFF) / 255f,
        alpha);

    public static Vector4 Alpha(Vector4 color, float alpha) => new(color.X, color.Y, color.Z, alpha);

    internal static void Tick()
    {
        _fade = 1f;
        Refresh();
        if (!_idle && Motion.Time - _swapStart >= SwapSeconds) _idle = true;
    }

    private static void Refresh()
    {
        if (Motion.Enabled && Motion.Time - _swapStart < SwapSeconds)
        {
            var x = Math.Clamp((Motion.Time - _swapStart) / SwapSeconds, 0f, 1f);
            var bell = MathF.Sin(MathF.PI * x);
            _opacity = 1f - SwapDip * bell;
            _saturation = 1f - SwapDesaturate * bell;
            _brightness = 1f - SwapDim * bell;
            _idle = false;
            return;
        }
        _idle = true;
        _opacity = 1f;
        _saturation = 1f;
        _brightness = 1f;
    }

    private static Vector4 Adjust(Vector4 color)
    {
        if (_idle)
            return new(color.X, color.Y, color.Z, color.W * _extraFade * _fade);
        var lum = color.X * 0.299f + color.Y * 0.587f + color.Z * 0.114f;
        return new Vector4(
            (lum + (color.X - lum) * _saturation) * _brightness,
            (lum + (color.Y - lum) * _saturation) * _brightness,
            (lum + (color.Z - lum) * _saturation) * _brightness,
            color.W * _opacity * _extraFade * _fade);
    }

    /// <summary>按当前主题设置 ImGui 全局样式；自定义控件都手绘，这里只兜底弹层/文本等原生路径。</summary>
    public static void Apply() => Apply(UiSettings.Scale);

    /// <summary>
    /// 以 scale 重建全局样式。所有尺寸都是"绝对值 × scale"，不依赖 ImGuiStyle 的增量缩放：
    /// 之前用 byte[] 按 sizeof(ImGuiStyle) 整块 memcpy 做保存/恢复，一旦托管结构与 native 尺寸不一致，
    /// 就会把 ImGuiContext 里紧跟在 Style 之后的字段（字体/纹理表等）覆盖成陈旧数据——
    /// 表现就是拖动"UI 大小比例"后文字变成黑块、卡片全黑。
    /// </summary>
    public static void Apply(float scale)
    {
        var s = Math.Clamp(scale, 0.1f, 4f);
        var style = ImGui.GetStyle();
        style.WindowPadding = new Vector2(12f, 10f) * s;
        style.FramePadding = new Vector2(12f, 8f) * s;
        style.ItemSpacing = new Vector2(8f, 8f) * s;
        style.ItemInnerSpacing = new Vector2(8f, 6f) * s;
        style.WindowRounding = Palette.Radius * s;
        style.ChildRounding = Palette.CardRadius * s;
        style.PopupRounding = 12f * s;
        style.FrameRounding = 10f * s;
        style.GrabRounding = 8f * s;
        style.ScrollbarRounding = 4f * s;
        style.WindowBorderSize = 0f;
        style.ChildBorderSize = 0f;
        style.FrameBorderSize = 0f;
        style.PopupBorderSize = 1f * s;
        style.ScrollbarSize = 8f * s;
        style.GrabMinSize = 16f * s;
        style.ButtonTextAlign = new Vector2(0.5f, 0.5f);
        style.SelectableTextAlign = new Vector2(0f, 0.5f);
        style.Alpha = 1f;

        Set(ImGuiCol.Text, Palette.Text);
        Set(ImGuiCol.TextDisabled, Palette.Text3);
        Set(ImGuiCol.WindowBg, Alpha(Palette.Win1, 0f));
        Set(ImGuiCol.ChildBg, Alpha(Palette.Win1, 0f));
        Set(ImGuiCol.PopupBg, Palette.Pop);
        Set(ImGuiCol.Border, Palette.CardStroke);
        Set(ImGuiCol.BorderShadow, default);
        Set(ImGuiCol.FrameBg, Palette.Field);
        Set(ImGuiCol.FrameBgHovered, Palette.Field);
        Set(ImGuiCol.FrameBgActive, Palette.Field);
        Set(ImGuiCol.Button, Palette.SegPill);
        Set(ImGuiCol.ButtonHovered, Palette.SegPill);
        Set(ImGuiCol.ButtonActive, Palette.SegPill);
        Set(ImGuiCol.Header, Palette.Soft);
        Set(ImGuiCol.HeaderHovered, Palette.Soft);
        Set(ImGuiCol.HeaderActive, Palette.Soft);
        Set(ImGuiCol.ScrollbarBg, Alpha(Palette.Win1, 0f));
        Set(ImGuiCol.ScrollbarGrab, Palette.FieldStroke);
        Set(ImGuiCol.ScrollbarGrabHovered, Palette.Text3);
        Set(ImGuiCol.ScrollbarGrabActive, Palette.Accent);
        Set(ImGuiCol.CheckMark, Palette.Accent);
        Set(ImGuiCol.SliderGrab, Palette.Accent);
        Set(ImGuiCol.SliderGrabActive, Palette.AccentHi);
        Set(ImGuiCol.Separator, Palette.Hairline);
        Set(ImGuiCol.SeparatorHovered, Palette.Accent);
        Set(ImGuiCol.SeparatorActive, Palette.AccentHi);
        Set(ImGuiCol.InputTextCursor, Palette.Accent);
        Set(ImGuiCol.TextSelectedBg, Palette.Soft);
        Set(ImGuiCol.NavCursor, Palette.Accent);
        Set(ImGuiCol.ModalWindowDimBg, new Vector4(0f, 0f, 0f, 0.55f));
        Set(ImGuiCol.NavWindowingDimBg, new Vector4(0f, 0f, 0f, 0.55f));
        Set(ImGuiCol.DragDropTarget, Palette.Accent);
    }

    private static void Set(ImGuiCol index, Vector4 color) => ImGui.GetStyle().Colors[(int)index] = color;

    private static UiPalette MicaPalette() => new()
    {
        Id = "mica", Cn = "云母", Chip = "云母 · MICA", En = "MICA · LIGHT", Tag = "基础", Phrase = "晨光",
        Win0 = new(251 / 255f, 252 / 255f, 254 / 255f, .97f),
        Win1 = new(240 / 255f, 243 / 255f, 248 / 255f, .98f),
        WinStroke = new(1f, 1f, 1f, .85f),
        Side0 = new(247 / 255f, 249 / 255f, 252 / 255f, .96f),
        Side1 = new(236 / 255f, 240 / 255f, 246 / 255f, .97f),
        Hairline = new(30 / 255f, 40 / 255f, 60 / 255f, .10f),
        Card0 = new(1f, 1f, 1f, .94f),
        Card1 = new(250 / 255f, 251 / 255f, 254 / 255f, .88f),
        CardStroke = new(30 / 255f, 40 / 255f, 60 / 255f, .13f),
        CardHi = new(1f, 1f, 1f, .9f),
        Text = Hex(0x111A2B),
        Text2 = Hex(0x5B6678),
        Text3 = Hex(0x98A2B3),
        Accent = Hex(0x0A84FF),
        AccentHi = Hex(0x3D9BFF),
        Accent2 = Hex(0x5AC8FA),
        Soft = new(10 / 255f, 132 / 255f, 1f, .10f),
        Glow = new(10 / 255f, 132 / 255f, 1f, .30f),
        Good = Hex(0x12B981),
        Warn = Hex(0xE8A33D),
        Field = new(30 / 255f, 40 / 255f, 60 / 255f, .08f),
        FieldStroke = new(30 / 255f, 40 / 255f, 60 / 255f, .16f),
        SegPill = new(1f, 1f, 1f, 1f),
        Pop = Hex(0xFBFCFE),
        Shadow = new(30 / 255f, 40 / 255f, 60 / 255f, .35f),
        Pv0 = Hex(0xF0F2F6),
        Pv1 = Hex(0xD8DFEA),
        PvAcc = Hex(0x0A84FF),
        PvText = Hex(0x1E283C),
        PvText2 = new(30 / 255f, 40 / 255f, 60 / 255f, .55f),
        PvText3 = new(30 / 255f, 40 / 255f, 60 / 255f, .75f),
        PvTextShadow = false,
        Glyph = AmbientWallpaper.Glyph.Star,
    };

    private static UiPalette ObsidianPalette() => new()
    {
        Id = "obsidian", Cn = "曜石", Chip = "曜石 · OBSIDIAN", En = "OBSIDIAN · DARK", Tag = "基础", Phrase = "静夜",
        Win0 = new(23 / 255f, 27 / 255f, 33 / 255f, .96f),
        Win1 = new(15 / 255f, 18 / 255f, 22 / 255f, .97f),
        WinStroke = new(1f, 1f, 1f, .14f),
        Side0 = new(19 / 255f, 22 / 255f, 27 / 255f, .95f),
        Side1 = new(11 / 255f, 13 / 255f, 16 / 255f, .96f),
        Hairline = new(1f, 1f, 1f, .08f),
        Card0 = new(1f, 1f, 1f, .09f),
        Card1 = new(1f, 1f, 1f, .045f),
        CardStroke = new(1f, 1f, 1f, .11f),
        CardHi = new(1f, 1f, 1f, .10f),
        Text = Hex(0xF5F6F8),
        Text2 = Hex(0x9BA3AF),
        Text3 = Hex(0x5E6672),
        Accent = Hex(0x4DA3FF),
        AccentHi = Hex(0x6FB6FF),
        Accent2 = Hex(0x9BE0FF),
        Soft = new(77 / 255f, 163 / 255f, 1f, .14f),
        Glow = new(77 / 255f, 163 / 255f, 1f, .38f),
        Good = Hex(0x3DDC97),
        Warn = Hex(0xFFB74D),
        Field = new(1f, 1f, 1f, .07f),
        FieldStroke = new(1f, 1f, 1f, .14f),
        SegPill = new(1f, 1f, 1f, .16f),
        Pop = Hex(0x14181E),
        Shadow = new(0f, 0f, 0f, .55f),
        Pv0 = Hex(0x1C2129),
        Pv1 = Hex(0x0C0E11),
        PvAcc = Hex(0x4DA3FF),
        Glyph = AmbientWallpaper.Glyph.Star,
    };

    private static UiPalette GlacierPalette() => new()
    {
        Id = "glacier", Cn = "冰晶", Chip = "冰晶 · FROST", En = "GLACIER · FROST", Tag = "冰", Phrase = "霜凝清夜",
        Win0 = new(228 / 255f, 242 / 255f, 250 / 255f, .96f),
        Win1 = new(206 / 255f, 228 / 255f, 241 / 255f, .97f),
        WinStroke = new(1f, 1f, 1f, .85f),
        Side0 = new(216 / 255f, 234 / 255f, 245 / 255f, .95f),
        Side1 = new(196 / 255f, 221 / 255f, 236 / 255f, .96f),
        Hairline = new(40 / 255f, 90 / 255f, 130 / 255f, .14f),
        Card0 = new(1f, 1f, 1f, .86f),
        Card1 = new(1f, 1f, 1f, .72f),
        CardStroke = new(40 / 255f, 90 / 255f, 130 / 255f, .16f),
        CardHi = new(1f, 1f, 1f, .85f),
        Text = Hex(0x16283A),
        Text2 = Hex(0x4E6E86),
        Text3 = Hex(0x8AA6B8),
        Accent = Hex(0x2C9AD6),
        AccentHi = Hex(0x56B4E4),
        Accent2 = Hex(0x9AD6F0),
        Soft = new(44 / 255f, 154 / 255f, 214 / 255f, .12f),
        Glow = new(44 / 255f, 154 / 255f, 214 / 255f, .30f),
        Good = Hex(0x12B981),
        Warn = Hex(0xE8A33D),
        Field = new(40 / 255f, 90 / 255f, 130 / 255f, .08f),
        FieldStroke = new(40 / 255f, 90 / 255f, 130 / 255f, .20f),
        SegPill = new(1f, 1f, 1f, .9f),
        Pop = Hex(0xEAF4FA),
        Shadow = new(40 / 255f, 60 / 255f, 80 / 255f, .35f),
        Pv0 = Hex(0xBFE0EF),
        Pv1 = Hex(0x8FBFD9),
        PvAcc = Hex(0x2C9AD6),
        PvText = Hex(0x16324A),
        PvText2 = new(30 / 255f, 60 / 255f, 85 / 255f, .55f),
        PvText3 = new(30 / 255f, 60 / 255f, 85 / 255f, .75f),
        PvTextShadow = false,
        Particle = WallpaperParticles.Kind.Frost,
        Glyph = AmbientWallpaper.Glyph.Frost,
    };

    private static UiPalette BlossomPalette() => new()
    {
        Id = "blossom", Cn = "绯樱", Chip = "绯樱 · BLOSSOM", En = "SAKURA · BLOSSOM", Tag = "樱", Phrase = "落樱织夜",
        Win0 = new(50 / 255f, 23 / 255f, 39 / 255f, .95f),
        Win1 = new(35 / 255f, 15 / 255f, 28 / 255f, .97f),
        WinStroke = new(1f, 180 / 255f, 200 / 255f, .22f),
        Side0 = new(42 / 255f, 19 / 255f, 33 / 255f, .95f),
        Side1 = new(29 / 255f, 12 / 255f, 24 / 255f, .97f),
        Hairline = new(1f, 180 / 255f, 200 / 255f, .12f),
        Card0 = new(1f, 150 / 255f, 175 / 255f, .11f),
        Card1 = new(1f, 150 / 255f, 175 / 255f, .05f),
        CardStroke = new(1f, 180 / 255f, 200 / 255f, .18f),
        CardHi = new(1f, 205 / 255f, 220 / 255f, .14f),
        Text = Hex(0xFBEFF3),
        Text2 = Hex(0xD0A9B8),
        Text3 = Hex(0x8E6B7C),
        Accent = Hex(0xF08CAC),
        AccentHi = Hex(0xF7A6BC),
        Accent2 = Hex(0xFFC4D8),
        Soft = new(240 / 255f, 140 / 255f, 172 / 255f, .15f),
        Glow = new(240 / 255f, 140 / 255f, 172 / 255f, .42f),
        Good = Hex(0x7FE8A8),
        Warn = Hex(0xF0C674),
        Field = new(22 / 255f, 9 / 255f, 18 / 255f, .55f),
        FieldStroke = new(1f, 180 / 255f, 200 / 255f, .20f),
        SegPill = new(1f, 150 / 255f, 175 / 255f, .22f),
        Pop = Hex(0x24101E),
        Shadow = new(0f, 0f, 0f, .55f),
        Pv0 = Hex(0x8C3E62),
        Pv1 = Hex(0x2B0E1E),
        PvAcc = Hex(0xF08CAC),
        Particle = WallpaperParticles.Kind.Petal,
        Glyph = AmbientWallpaper.Glyph.Petal,
    };

    private static UiPalette AmberPalette() => new()
    {
        Id = "amber", Cn = "琥珀", Chip = "琥珀 · AMBER", En = "AMBER · GILT", Tag = "金", Phrase = "玄金磐石",
        Win0 = new(38 / 255f, 30 / 255f, 23 / 255f, .95f),
        Win1 = new(25 / 255f, 19 / 255f, 15 / 255f, .97f),
        WinStroke = new(240 / 255f, 200 / 255f, 140 / 255f, .22f),
        Side0 = new(32 / 255f, 25 / 255f, 19 / 255f, .95f),
        Side1 = new(21 / 255f, 16 / 255f, 12 / 255f, .97f),
        Hairline = new(240 / 255f, 200 / 255f, 140 / 255f, .12f),
        Card0 = new(232 / 255f, 180 / 255f, 110 / 255f, .11f),
        Card1 = new(232 / 255f, 180 / 255f, 110 / 255f, .05f),
        CardStroke = new(240 / 255f, 200 / 255f, 140 / 255f, .18f),
        CardHi = new(1f, 225 / 255f, 180 / 255f, .14f),
        Text = Hex(0xF7F0E4),
        Text2 = Hex(0xC9B69C),
        Text3 = Hex(0x8A7A64),
        Accent = Hex(0xE6B45C),
        AccentHi = Hex(0xF0C47A),
        Accent2 = Hex(0xFFD98A),
        Soft = new(230 / 255f, 180 / 255f, 92 / 255f, .15f),
        Glow = new(230 / 255f, 180 / 255f, 92 / 255f, .42f),
        Good = Hex(0x8FE0A0),
        Warn = Hex(0xF0C674),
        Field = new(18 / 255f, 13 / 255f, 9 / 255f, .55f),
        FieldStroke = new(240 / 255f, 200 / 255f, 140 / 255f, .20f),
        SegPill = new(232 / 255f, 180 / 255f, 110 / 255f, .22f),
        Pop = Hex(0x1B1510),
        Shadow = new(0f, 0f, 0f, .55f),
        Pv0 = Hex(0x8A6B3A),
        Pv1 = Hex(0x241A10),
        PvAcc = Hex(0xE6B45C),
        Particle = WallpaperParticles.Kind.Geo,
        Glyph = AmbientWallpaper.Glyph.Geo,
    };

    private static UiPalette TidePalette() => new()
    {
        Id = "tide", Cn = "沧海", Chip = "沧海 · TIDE", En = "TIDE · AZURE", Tag = "水", Phrase = "万水共生",
        Win0 = new(16 / 255f, 34 / 255f, 58 / 255f, .95f),
        Win1 = new(9 / 255f, 20 / 255f, 38 / 255f, .97f),
        WinStroke = new(140 / 255f, 200 / 255f, 240 / 255f, .22f),
        Side0 = new(13 / 255f, 29 / 255f, 50 / 255f, .95f),
        Side1 = new(8 / 255f, 17 / 255f, 32 / 255f, .97f),
        Hairline = new(140 / 255f, 200 / 255f, 240 / 255f, .12f),
        Card0 = new(110 / 255f, 180 / 255f, 230 / 255f, .11f),
        Card1 = new(110 / 255f, 180 / 255f, 230 / 255f, .05f),
        CardStroke = new(140 / 255f, 200 / 255f, 240 / 255f, .17f),
        CardHi = new(190 / 255f, 230 / 255f, 1f, .14f),
        Text = Hex(0xEFF6FB),
        Text2 = Hex(0xA4BDD4),
        Text3 = Hex(0x5E7E99),
        Accent = Hex(0x5FB8E0),
        AccentHi = Hex(0x7FC8EC),
        Accent2 = Hex(0xA8DDF5),
        Soft = new(95 / 255f, 184 / 255f, 224 / 255f, .15f),
        Glow = new(95 / 255f, 184 / 255f, 224 / 255f, .42f),
        Good = Hex(0x7FE8C8),
        Warn = Hex(0xF0C674),
        Field = new(5 / 255f, 14 / 255f, 26 / 255f, .55f),
        FieldStroke = new(140 / 255f, 200 / 255f, 240 / 255f, .20f),
        SegPill = new(110 / 255f, 180 / 255f, 230 / 255f, .22f),
        Pop = Hex(0x0B1A2C),
        Shadow = new(0f, 0f, 0f, .55f),
        Pv0 = Hex(0x1E4E78),
        Pv1 = Hex(0x0A1B33),
        PvAcc = Hex(0x5FB8E0),
        Particle = WallpaperParticles.Kind.Bubble,
        Glyph = AmbientWallpaper.Glyph.Hydro,
    };

    private static UiPalette VerdantPalette() => new()
    {
        Id = "verdant", Cn = "翠芽", Chip = "翠芽 · VERDANT", En = "VERDANT · LEAF", Tag = "叶", Phrase = "新芽承露",
        Win0 = new(22 / 255f, 38 / 255f, 26 / 255f, .95f),
        Win1 = new(13 / 255f, 24 / 255f, 17 / 255f, .97f),
        WinStroke = new(150 / 255f, 220 / 255f, 130 / 255f, .22f),
        Side0 = new(18 / 255f, 32 / 255f, 22 / 255f, .95f),
        Side1 = new(11 / 255f, 20 / 255f, 14 / 255f, .97f),
        Hairline = new(150 / 255f, 220 / 255f, 130 / 255f, .12f),
        Card0 = new(140 / 255f, 216 / 255f, 108 / 255f, .11f),
        Card1 = new(140 / 255f, 216 / 255f, 108 / 255f, .05f),
        CardStroke = new(150 / 255f, 220 / 255f, 130 / 255f, .17f),
        CardHi = new(200 / 255f, 245 / 255f, 170 / 255f, .14f),
        Text = Hex(0xF0F7EC),
        Text2 = Hex(0xAEC9A4),
        Text3 = Hex(0x6E8A64),
        Accent = Hex(0x8CD86C),
        AccentHi = Hex(0xA4E486),
        Accent2 = Hex(0xC0EDA0),
        Soft = new(140 / 255f, 216 / 255f, 108 / 255f, .15f),
        Glow = new(140 / 255f, 216 / 255f, 108 / 255f, .40f),
        Good = Hex(0x9FE8A5),
        Warn = Hex(0xF0C674),
        Field = new(6 / 255f, 12 / 255f, 8 / 255f, .55f),
        FieldStroke = new(150 / 255f, 220 / 255f, 130 / 255f, .20f),
        SegPill = new(140 / 255f, 216 / 255f, 108 / 255f, .22f),
        Pop = Hex(0x121E15),
        Shadow = new(0f, 0f, 0f, .55f),
        Pv0 = Hex(0x3A7A44),
        Pv1 = Hex(0x12281A),
        PvAcc = Hex(0x8CD86C),
        Particle = WallpaperParticles.Kind.Spore,
        Glyph = AmbientWallpaper.Glyph.Dendro,
    };

    private static UiPalette EmberPalette() => new()
    {
        Id = "ember", Cn = "焰火", Chip = "焰火 · EMBER", En = "EMBER · FLARE", Tag = "火", Phrase = "夏夜花火",
        Win0 = new(50 / 255f, 24 / 255f, 16 / 255f, .95f),
        Win1 = new(34 / 255f, 15 / 255f, 10 / 255f, .97f),
        WinStroke = new(1f, 180 / 255f, 120 / 255f, .22f),
        Side0 = new(42 / 255f, 20 / 255f, 13 / 255f, .95f),
        Side1 = new(29 / 255f, 12 / 255f, 8 / 255f, .97f),
        Hairline = new(1f, 180 / 255f, 120 / 255f, .12f),
        Card0 = new(240 / 255f, 138 / 255f, 74 / 255f, .11f),
        Card1 = new(240 / 255f, 138 / 255f, 74 / 255f, .05f),
        CardStroke = new(1f, 180 / 255f, 120 / 255f, .18f),
        CardHi = new(1f, 215 / 255f, 180 / 255f, .14f),
        Text = Hex(0xFBF1E8),
        Text2 = Hex(0xD4A896),
        Text3 = Hex(0x8E6E5C),
        Accent = Hex(0xF08A4A),
        AccentHi = Hex(0xF7A266),
        Accent2 = Hex(0xFFB36B),
        Soft = new(240 / 255f, 138 / 255f, 74 / 255f, .15f),
        Glow = new(240 / 255f, 138 / 255f, 74 / 255f, .42f),
        Good = Hex(0x7FE8A8),
        Warn = Hex(0xF0C674),
        Field = new(20 / 255f, 8 / 255f, 5 / 255f, .55f),
        FieldStroke = new(1f, 180 / 255f, 120 / 255f, .20f),
        SegPill = new(240 / 255f, 138 / 255f, 74 / 255f, .22f),
        Pop = Hex(0x22100B),
        Shadow = new(0f, 0f, 0f, .55f),
        Pv0 = Hex(0xB8542A),
        Pv1 = Hex(0x2E100A),
        PvAcc = Hex(0xF08A4A),
        Particle = WallpaperParticles.Kind.Ember,
        Glyph = AmbientWallpaper.Glyph.Star,
    };

    private static UiPalette ZephyrPalette() => new()
    {
        Id = "zephyr", Cn = "青岚", Chip = "青岚 · ZEPHYR", En = "ZEPHYR · TEAL", Tag = "风", Phrase = "风起青萍",
        Win0 = new(14 / 255f, 40 / 255f, 42 / 255f, .95f),
        Win1 = new(8 / 255f, 26 / 255f, 28 / 255f, .97f),
        WinStroke = new(110 / 255f, 220 / 255f, 205 / 255f, .22f),
        Side0 = new(11 / 255f, 33 / 255f, 35 / 255f, .95f),
        Side1 = new(7 / 255f, 22 / 255f, 24 / 255f, .97f),
        Hairline = new(110 / 255f, 220 / 255f, 205 / 255f, .12f),
        Card0 = new(70 / 255f, 200 / 255f, 184 / 255f, .11f),
        Card1 = new(70 / 255f, 200 / 255f, 184 / 255f, .05f),
        CardStroke = new(110 / 255f, 220 / 255f, 205 / 255f, .17f),
        CardHi = new(170 / 255f, 240 / 255f, 230 / 255f, .14f),
        Text = Hex(0xEEF7F5),
        Text2 = Hex(0x9EC4BE),
        Text3 = Hex(0x5E8A84),
        Accent = Hex(0x46C8B8),
        AccentHi = Hex(0x64D8CA),
        Accent2 = Hex(0x9AE8DC),
        Soft = new(70 / 255f, 200 / 255f, 184 / 255f, .15f),
        Glow = new(70 / 255f, 200 / 255f, 184 / 255f, .40f),
        Good = Hex(0x7FE8B0),
        Warn = Hex(0xF0C674),
        Field = new(4 / 255f, 14 / 255f, 15 / 255f, .55f),
        FieldStroke = new(110 / 255f, 220 / 255f, 205 / 255f, .20f),
        SegPill = new(70 / 255f, 200 / 255f, 184 / 255f, .22f),
        Pop = Hex(0x0A1E20),
        Shadow = new(0f, 0f, 0f, .55f),
        Pv0 = Hex(0x1E6E68),
        Pv1 = Hex(0x0A2628),
        PvAcc = Hex(0x46C8B8),
        Particle = WallpaperParticles.Kind.Dust,
        Glyph = AmbientWallpaper.Glyph.Star,
    };

    private static UiPalette TerrariaPalette() => new()
    {
        Id = "terraria", Cn = "泰拉蓝钢", Chip = "泰拉蓝钢 · CLASSIC", En = "TERRARIA · CLASSIC", Tag = "怀旧", Phrase = "经典蓝钢与鎏金",
        Win0 = new(62 / 255f, 74 / 255f, 120 / 255f, .96f),
        Win1 = new(42 / 255f, 51 / 255f, 84 / 255f, .97f),
        WinStroke = new(0f, 0f, 0f, .55f),
        Side0 = new(44 / 255f, 51 / 255f, 84 / 255f, .96f),
        Side1 = new(35 / 255f, 42 / 255f, 69 / 255f, .97f),
        Hairline = new(1f, 1f, 1f, .10f),
        Card0 = new(1f, 1f, 1f, .09f),
        Card1 = new(1f, 1f, 1f, .04f),
        CardStroke = new(0f, 0f, 0f, .35f),
        CardHi = new(1f, 1f, 1f, .10f),
        Text = Hex(0xFFFFFF),
        Text2 = Hex(0xA9B2D6),
        Text3 = Hex(0x6F7CA6),
        Accent = Hex(0xE9C15B),
        AccentHi = Hex(0xF2CF7C),
        Accent2 = Hex(0xFFE28A),
        Soft = new(233 / 255f, 193 / 255f, 91 / 255f, .15f),
        Glow = new(233 / 255f, 193 / 255f, 91 / 255f, .38f),
        Good = Hex(0x9FE8A5),
        Warn = Hex(0xF0C674),
        Field = new(10 / 255f, 14 / 255f, 24 / 255f, .60f),
        FieldStroke = new(0f, 0f, 0f, .45f),
        SegPill = new(233 / 255f, 193 / 255f, 91 / 255f, .20f),
        Pop = Hex(0x1A2036),
        Shadow = new(0f, 0f, 0f, .55f),
        Pv0 = Hex(0x3E4A78),
        Pv1 = Hex(0x232A45),
        PvAcc = Hex(0xE9C15B),
        Radius = 12f,
        CardRadius = 10f,
        Particle = WallpaperParticles.Kind.Frost,
        Glyph = AmbientWallpaper.Glyph.Star,
    };
}
