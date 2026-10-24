using System.Diagnostics;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;

namespace Pylon.ModLoader.Effects;

/// <summary>屏幕效果调试：画每个效果/扭曲 pass 的包围盒，并在屏幕上列当前效果与统计。</summary>
internal static class ScreenDebug
{
    private const int Max = 32;
    private const int Lines = 8;

    private const string Source = @"
#include <bl/color>
uniform vec4 uBoxes[32];
uniform vec4 uCircles[32];
uniform float uCount;

void main() {
    vec2 px = vUV * uResolution;
    for (int i = 0; i < 32; i++) {
        if (float(i) >= uCount) break;
        vec4 c = uCircles[i];
        vec3 color = c.w < 0.0 ? vec3(1.0) : blHsvToRgb(vec3(c.w, 0.7, 1.0));
        if (c.z > 0.0) {
            vec2 d = (vUV - c.xy) * vec2(uAspect, 1.0);
            if (abs(length(d) - c.z) * uResolution.y < 1.25) {
                fragColor = vec4(color, 1.0);
                return;
            }
        }
        vec4 b = uBoxes[i] * uResolution.xyxy;
        vec2 lo = px - b.xy, hi = b.zw - px;
        float edge = min(min(lo.x, lo.y), min(hi.x, hi.y));
        if (edge > -1.0 && edge < 1.25 && mod(floor((px.x + px.y) / 6.0), 2.0) < 1.0) {
            fragColor = vec4(color, 1.0);
            return;
        }
    }
    discard;
}
";

    private static bool _enabled;
    private static bool _installed;
    private static ScreenGraphics.Shader? _shader;
    private static readonly List<float> Boxes = new();
    private static readonly List<float> Circles = new();
    private static (long At, ScreenStats Stats)? _mark;
    private static string _summary = "";
    private static int _frames;

    public static bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            _mark = null;
            _summary = "";
            if (value) Install();
        }
    }

    private static void Install()
    {
        if (_installed) return;
        _installed = true;
        try { On.Terraria.Main.DrawInterface_15_InvasionProgressBars += DrawPanel; }
        catch (Exception e)
        {
            _installed = false;
            ScreenReport.Report("ScreenDebug.Install", e);
        }
    }

    private static float Hue(ModScreenEffect fx)
    {
        string text = fx.GetType().Name + ":" + (fx.Key ?? "");
        uint h = 0;
        foreach (char c in text) h = unchecked(h * 31 + c);
        return h % 1000 / 1000f;
    }

    public static void Track(ModScreenEffect fx, IReadOnlyList<ScreenEffect> draws, int from)
    {
        float hue = Hue(fx);
        for (int i = from; i < draws.Count; i++)
        {
            ScreenEffect d = draws[i];
            if (d.Dest != 0) continue;
            float radius = fx.Center == null ? 0 : fx.Fullscreen ? ScreenUtils.Space.PixelsToUV(6) : d.Radius;
            Add(d.Box, d.Center.x, d.Center.y, radius, hue);
        }
    }

    public static void Decorate(ScreenFrame frame, ScreenEffect? distortion)
    {
        if (distortion != null) Add(distortion.Box, 0, 0, 0, -1);
        int count = Boxes.Count / 4;
        if (count == 0) return;
        _shader ??= ScreenGraphics.Shader.FromSource(Source, "bl/ScreenDebug");
        var draw = new ScreenEffect(_shader)
        {
            Layer = ScreenEffects.Layer.Screen,
        };
        draw.Uniform("uBoxes", 4, Boxes.ToArray());
        draw.Uniform("uCircles", 4, Circles.ToArray());
        draw.Uniform("uCount", count);
        frame.Add(draw);
        Boxes.Clear();
        Circles.Clear();
    }

    private static void Add(float[]? box, float cx, float cy, float radius, float hue)
    {
        if (box is not { Length: 4 }) return;
        if (Boxes.Count / 4 >= Max) return;
        Boxes.AddRange(box);
        Circles.AddRange(new[] { cx, cy, radius, hue });
    }

    private static string Summary()
    {
        if (_frames++ % 30 != 0 && _summary.Length > 0) return _summary;
        var now = (At: Stopwatch.GetTimestamp(), Stats: ScreenStats.Read());
        var last = _mark;
        _mark = now;
        string text = $"屏幕效果 {ScreenEffects.Active.Count}";
        if (last is { } m)
        {
            ScreenStats a = m.Stats, b = now.Stats;
            long worldFrames = Math.Max(b.WorldFrames - a.WorldFrames, 0);
            long samples = b.GpuSamples - a.GpuSamples;
            double seconds = Math.Max((now.At - m.At) / (double)Stopwatch.Frequency, 1e-3);
            text += "  ·  " + (worldFrames > 0 ? ((b.Passes - a.Passes) / (double)worldFrames).ToString("0.0") : "0") + " pass";
            if (samples > 0) text += $"  ·  GPU {(b.GpuNs - a.GpuNs) / (double)samples / 1e6:0.00} ms";
            text += $"  ·  {worldFrames / seconds:0} fps";
        }
        if (!ScreenEffects.Enabled) text += "  ·  已关闭";
        if (ScreenEffects.HotReload) text += "  ·  热重载";
        return _summary = text;
    }

    private static void DrawPanel(On.Terraria.Main.orig_DrawInterface_15_InvasionProgressBars orig)
    {
        orig();
        if (!_enabled || Main.gameMenu) return;
        try
        {
            SpriteBatch batch = Main.spriteBatch;
            float x = MathF.Round(Main.screenWidth / 2f);
            float y = MathF.Round(Main.screenHeight * 0.13f);
            void Draw(string text, float at, Color color) =>
                Utils.DrawBorderString(batch, text, new Vector2(x, at), color, 0.8f, 0.5f, 0, -1);
            Draw(Summary(), y, new Color(255, 255, 255, 255));
            IReadOnlyList<ModScreenEffect> active = ScreenEffects.Active;
            for (int i = 0; i < active.Count && i < Lines; i++)
            {
                y += 20;
                Draw(Describe(active[i]), y, ScreenUtils.ColorMath.Hsv(Hue(active[i]) * 360, 0.45f, 1));
            }
            if (active.Count > Lines) Draw("+" + (active.Count - Lines), y + 20, new Color(255, 255, 255, 255));
        }
        catch (Exception e) { ScreenReport.Report("ScreenDebug", e); }
    }

    internal static string Describe(ModScreenEffect fx)
    {
        string text = fx.GetType().Name;
        if (fx.Key != null) text += $" [{fx.Key}]";
        text += fx.Fullscreen || fx.Center == null ? "  整屏" : $"  r {MathF.Round(fx.Radius)}";
        text += $"  {fx.Age / 60:0.0} s";
        if (fx.Duration > 0) text += $" / {fx.Duration / 60:0.0} s";
        float strength = fx.Intensity * fx.Opacity;
        if (strength < 0.999f) text += $"  x{strength:0.00}";
        if (fx.Shader?.Status == ScreenGraphics.ShaderStatus.Failed) text += "  shader 失败";
        return text;
    }
}