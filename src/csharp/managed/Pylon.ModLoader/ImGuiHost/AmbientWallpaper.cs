using System.Numerics;
using Pylon.NaviteAPI.ImGui;

namespace Pylon.ModLoader.ImGuiHost;

/// <summary>
/// 氛围装饰层：呼吸光斑与主题水印，全部为 draw list 矢量绘制、不依赖任何贴图。
/// （原壁纸画布/压暗/扫光层与内嵌角色图已随 19MB 程序集减重一并移除。）
/// </summary>
public static unsafe class AmbientWallpaper
{
    public enum Glyph
    {
        Star,
        Frost,
        Petal,
        Geo,
        Hydro,
        Dendro,
    }

    internal static void Draw(ImDrawListPtr dl, Vector2 min, Vector2 max, float radius)
    {
        var path = ModernUI.RoundedRectPath(min, max, radius, ImDrawFlags.RoundCornersAll, 8);
        DrawBlobs(dl, min, max, path);
        WallpaperParticles.Draw(dl, UiTheme.Palette.Particle, min, max, path, Motion.Time);
        DrawGlyph(dl, min, max, path);
    }

    internal static bool PointInPath(Vector2 p, Vector2[] path)
    {
        for (var i = 0; i < path.Length; i++)
        {
            var a = path[i];
            var b = path[(i + 1) % path.Length];
            if ((b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X) < 0f) return false;
        }
        return true;
    }

    internal static Vector2 ClampIntoPolygon(Vector2 p, Vector2[] path)
    {
        if (PointInPath(p, path)) return p;
        Vector2 best = p;
        var bestD = float.MaxValue;
        for (var i = 0; i < path.Length; i++)
        {
            var a = path[i];
            var b = path[(i + 1) % path.Length];
            var abx = b.X - a.X;
            var aby = b.Y - a.Y;
            var len2 = MathF.Max(abx * abx + aby * aby, 0.0001f);
            var t = Math.Clamp(((p.X - a.X) * abx + (p.Y - a.Y) * aby) / len2, 0f, 1f);
            var q = new Vector2(a.X + abx * t, a.Y + aby * t);
            var d = (p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y);
            if (d < bestD)
            {
                bestD = d;
                best = q;
            }
        }
        return best;
    }

    private static void DrawBlobs(ImDrawListPtr dl, Vector2 min, Vector2 max, Vector2[] path)
    {
        var w = max.X - min.X;
        var h = max.Y - min.Y;
        var soft = UiTheme.Soft;
        var glow = UiTheme.Glow;
        DrawBlob(dl, new Vector2(min.X + w * 0.13f, min.Y + h * 0.12f),
            new Vector2(w * 0.23f, h * 0.26f), soft, 0f, path);
        DrawBlob(dl, new Vector2(min.X + w * 0.86f, min.Y + h * 0.88f),
            new Vector2(w * 0.26f, h * 0.28f), glow, -4.5f, path);
    }

    private static void DrawBlob(ImDrawListPtr dl, Vector2 center, Vector2 radius, Vector4 color, float phase,
        Vector2[] path)
    {
        var breathe = 0.5f - 0.5f * MathF.Cos(MathF.PI * 2f * Motion.Cycle(Motion.BlobBreathSeconds, phase));
        var alpha = (0.45f + 0.35f * breathe) * color.W;
        if (alpha <= 0.004f) return;
        var scale = 1f + 0.08f * breathe;
        radius *= scale;
        const int Ring = 20;
        Span<Vector2> points = stackalloc Vector2[Ring + 1];
        points[0] = ClampIntoPolygon(center, path);
        for (var i = 0; i < Ring; i++)
        {
            var angle = i * (MathF.PI * 2f / Ring);
            points[i + 1] = ClampIntoPolygon(center + new Vector2(MathF.Cos(angle) * radius.X, MathF.Sin(angle) * radius.Y),
                path);
        }
        var first = dl.VtxBuffer.Size;
        fixed (Vector2* p = points)
            dl.AddConvexPolyFilled(p, Ring + 1,
                ImGui.ColorConvertFloat4ToU32(new Vector4(color.X, color.Y, color.Z, alpha)));
        var outer = ImGui.ColorConvertFloat4ToU32(new Vector4(color.X, color.Y, color.Z, 0f));
        for (var i = 0; i < Ring; i++)
            ModernUI.WriteFillVertex(dl, first, 1 + i, outer);
    }

    private static readonly Vector2[] GlyphFrostA = [new(12f, 2f), new(12f, 20f)];
    private static readonly Vector2[] GlyphFrostB = [new(2f, 12f), new(20f, 12f)];
    private static readonly Vector2[] GlyphFrostC = [new(5f, 5f), new(19f, 19f)];
    private static readonly Vector2[] GlyphFrostD = [new(19f, 5f), new(5f, 19f)];
    private static readonly Vector2[] GlyphPetalTop =
        [new(12f, 3f), new(14.2f, 6f), new(14.8f, 10f), new(13.5f, 13.2f),
         new(12f, 14f), new(10.5f, 13.2f), new(9.2f, 10f), new(9.8f, 6f)];
    private static readonly Vector2[] GlyphPetalLeft =
        [new(4.5f, 10.5f), new(8f, 12f), new(11f, 15f), new(11.5f, 17.5f),
         new(10f, 18.5f), new(7.5f, 17.5f), new(5f, 14.5f), new(4.3f, 12.3f)];
    private static readonly Vector2[] GlyphPetalRight =
        [new(19.5f, 10.5f), new(16f, 12f), new(13f, 15f), new(12.5f, 17.5f),
         new(14f, 18.5f), new(16.5f, 17.5f), new(19f, 14.5f), new(19.7f, 12.3f)];
    private static readonly Vector2[] GlyphGeoOuter =
        [new(12f, 3.51f), new(20.49f, 12f), new(12f, 20.49f), new(3.51f, 12f)];
    private static readonly Vector2[] GlyphGeoInner =
        [new(12f, 7.76f), new(16.24f, 12f), new(12f, 16.24f), new(7.76f, 12f)];
    private static readonly Vector2[] GlyphHydroDrop =
        [new(12f, 3f), new(14.4f, 6.8f), new(16.4f, 10.2f), new(17f, 13.2f), new(16f, 16.2f),
         new(12f, 18f), new(8f, 16.2f), new(7f, 13.2f), new(7.6f, 10.2f), new(9.6f, 6.8f)];
    private static readonly Vector2[] GlyphHydroArc = [new(9.5f, 13.5f), new(10.5f, 15.3f), new(12f, 15.8f)];
    private static readonly Vector2[] GlyphDendroStem = [new(12f, 21f), new(12f, 12f)];
    private static readonly Vector2[] GlyphDendroLeafA =
        [new(12f, 12f), new(10.5f, 8.6f), new(7.8f, 6f), new(4.5f, 5f),
         new(4.9f, 8f), new(7.3f, 10.3f)];
    private static readonly Vector2[] GlyphDendroLeafB =
        [new(12f, 15f), new(12.4f, 12f), new(14.2f, 10.2f), new(16.8f, 9f),
         new(19.5f, 9f), new(19f, 12.4f), new(16.3f, 14.6f)];
    private static readonly Vector2[] GlyphStarBig =
        [new(12f, 2f), new(14.4f, 7.8f), new(20f, 10f), new(14.4f, 12.2f),
         new(12f, 18f), new(9.6f, 12.2f), new(4f, 10f), new(9.6f, 7.8f)];
    private static readonly Vector2[] GlyphStarSmall =
        [new(19f, 15f), new(19.9f, 17.1f), new(22f, 18f), new(19.9f, 18.9f),
         new(19f, 21f), new(18.1f, 18.9f), new(16f, 18f), new(18.1f, 17.1f)];

    private static void DrawGlyph(ImDrawListPtr dl, Vector2 min, Vector2 max, Vector2[] path)
    {
        var w = max.X - min.X;
        var h = max.Y - min.Y;
        var size = TerrariaTheme.Px(150f);
        var origin = new Vector2(min.X + w * 0.95f - size, min.Y + h * 0.11f);
        var breathe = 0.5f - 0.5f * MathF.Cos(MathF.PI * 2f * Motion.Cycle(Motion.GlyphFloatSeconds));
        var rotate = breathe * 6f * (MathF.PI / 180f);
        var center = origin + new Vector2(size * 0.5f, size * 0.5f + breathe * -10f);
        var scale = size / 24f;
        var col = TerrariaTheme.U32(UiTheme.Alpha(UiTheme.Accent, 0.07f));
        var thickness = TerrariaTheme.Px(1f) * scale;
        switch (UiTheme.Palette.Glyph)
        {
            case Glyph.Frost:
                Stroke(dl, center, scale, rotate, GlyphFrostA, false, col, thickness, path);
                Stroke(dl, center, scale, rotate, GlyphFrostB, false, col, thickness, path);
                Stroke(dl, center, scale, rotate, GlyphFrostC, false, col, thickness, path);
                Stroke(dl, center, scale, rotate, GlyphFrostD, false, col, thickness, path);
                dl.AddCircle(ClampIntoPolygon(Transform(center, scale, rotate, new Vector2(12f, 12f)), path),
                    scale * 3.2f, col, 0, thickness);
                break;
            case Glyph.Petal:
                Fill(dl, center, scale, rotate, GlyphPetalTop, col, path);
                Fill(dl, center, scale, rotate, GlyphPetalLeft, col, path);
                Fill(dl, center, scale, rotate, GlyphPetalRight, col, path);
                break;
            case Glyph.Geo:
                Stroke(dl, center, scale, rotate, GlyphGeoOuter, true, col, thickness, path);
                Stroke(dl, center, scale, rotate, GlyphGeoInner, true, col, thickness, path);
                break;
            case Glyph.Hydro:
                Stroke(dl, center, scale, rotate, GlyphHydroDrop, true, col, thickness, path);
                Stroke(dl, center, scale, rotate, GlyphHydroArc, false, col, thickness, path);
                break;
            case Glyph.Dendro:
                Stroke(dl, center, scale, rotate, GlyphDendroStem, false, col, thickness, path);
                Stroke(dl, center, scale, rotate, GlyphDendroLeafA, true, col, thickness, path);
                Stroke(dl, center, scale, rotate, GlyphDendroLeafB, true, col, thickness, path);
                break;
            default:
                Stroke(dl, center, scale, rotate, GlyphStarBig, true, col, thickness, path);
                Stroke(dl, center, scale, rotate, GlyphStarSmall, true, col, thickness, path);
                break;
        }
    }

    private static Vector2 Transform(Vector2 center, float scale, float rotate, Vector2 p)
    {
        var q = (p - new Vector2(12f, 12f)) * scale;
        var cos = MathF.Cos(rotate);
        var sin = MathF.Sin(rotate);
        return center + new Vector2(q.X * cos - q.Y * sin, q.X * sin + q.Y * cos);
    }

    private static void Stroke(ImDrawListPtr dl, Vector2 center, float scale, float rotate,
        Vector2[] points, bool closed, uint col, float thickness, Vector2[] path)
    {
        Span<Vector2> buffer = stackalloc Vector2[points.Length];
        for (var i = 0; i < points.Length; i++)
            buffer[i] = ClampIntoPolygon(Transform(center, scale, rotate, points[i]), path);
        fixed (Vector2* p = buffer)
            dl.AddPolyline(p, buffer.Length, col,
                closed ? ImDrawFlags.Closed : ImDrawFlags.None, thickness);
    }

    private static void Fill(ImDrawListPtr dl, Vector2 center, float scale, float rotate,
        Vector2[] points, uint col, Vector2[] path)
    {
        Span<Vector2> buffer = stackalloc Vector2[points.Length];
        for (var i = 0; i < points.Length; i++)
            buffer[i] = ClampIntoPolygon(Transform(center, scale, rotate, points[i]), path);
        fixed (Vector2* p = buffer)
            dl.AddConvexPolyFilled(p, buffer.Length, col);
    }
}
