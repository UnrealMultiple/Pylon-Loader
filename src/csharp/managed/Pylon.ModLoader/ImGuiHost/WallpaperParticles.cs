using System.Numerics;
using Pylon.NaviteAPI.ImGui;

namespace Pylon.ModLoader.ImGuiHost;

/// <summary>主题粒子层：冰晶雪花 / 樱瓣飘落 / 琥珀岩晶 / 沧海气泡 / 翠芽孢子 / 焰火余烬 / 青岚浮尘，逐主题分支绘制。</summary>
public static unsafe class WallpaperParticles
{
    public enum Kind
    {
        None,
        Geo,
        Dust,
        Bubble,
        Spore,
        Ember,
        Petal,
        Frost,
    }

    internal static void Draw(ImDrawListPtr dl, Kind kind, Vector2 min, Vector2 max, Vector2[]? path, float t)
    {
        var w = max.X - min.X;
        var h = max.Y - min.Y;
        if (w <= 0f || h <= 0f) return;
        switch (kind)
        {
            case Kind.Geo: Geo(dl, min, w, h, t); break;
            case Kind.Dust: Dust(dl, min, w, h, t); break;
            case Kind.Bubble: Bubble(dl, min, w, h, t); break;
            case Kind.Spore: Spore(dl, min, w, h, t); break;
            case Kind.Ember: Ember(dl, min, w, h, t); break;
            case Kind.Petal: Petal(dl, min, w, h, t); break;
            case Kind.Frost: Frost(dl, min, w, h, path, t); break;
        }
    }

    private static void Geo(ImDrawListPtr dl, Vector2 min, float w, float h, float t)
    {
        for (var i = 0; i < 95; i++)
        {
            var depth = .3f + Hash(i, 400) * .7f;
            var u = (Hash(i, 401) + t * (.015f + depth * .018f)) % 1f;
            var x = min.X + w * (Hash(i, 402) + MathF.Sin(t * .12f + i) * .012f);
            var y = min.Y + h * (1.08f - u * 1.2f);
            var a = Ss(0f, .2f, u) * (1f - Ss(.8f, 1f, u)) * (.14f + depth * .35f) * (.7f + .3f * MathF.Sin(t * 2f + i));
            var r = (.7f + depth * 1.2f) * h / 600f;
            Glow(dl, new Vector2(x, y), r * 6f, 0xFFBF50, a * .26f);
            var ry = r * 1.7f;
            dl.AddQuadFilled(
                new Vector2(x, y - ry), new Vector2(x + r, y),
                new Vector2(x, y + ry), new Vector2(x - r, y),
                U32(255, 221, 151, a));
        }
    }

    private static void Dust(ImDrawListPtr dl, Vector2 min, float w, float h, float t)
    {
        for (var i = 0; i < 76; i++)
        {
            var life = 3f + Hash(i, 101) * 7f;
            var age = (t + Hash(i, 102) * life) % life;
            var u = age / life;
            var depth = .25f + Hash(i, 103) * .75f;
            var r = (.8f + depth * 2.1f) * w / 1600f;
            var px = min.X + w * (Hash(i, 104) + MathF.Sin(t * .55f + i) * .006f + age * .003f);
            var py = min.Y + h * (Hash(i, 105) + MathF.Cos(t * .6f + i) * .008f - age * .002f);
            var a = Ss(0f, .3f, u) * (1f - Ss(.7f, 1f, u)) * (.16f + depth * .24f) *
                    (.7f + .3f * MathF.Sin(t * (3f + Hash(i, 106) * 4f) + i));
            Glow(dl, new Vector2(px, py), r * 4f, 0xE8DCA9, a * .25f);
            dl.AddCircleFilled(new Vector2(px, py), MathF.Max(.55f, r * .45f), U32(255, 247, 198, a));
        }
    }

    private static void Bubble(ImDrawListPtr dl, Vector2 min, float w, float h, float t)
    {
        for (var i = 0; i < 22; i++)
        {
            var life = 12f + Hash(i, 81) * 14f;
            var u = ((t + Hash(i, 82) * life) % life) / life;
            var depth = .3f + .7f * Hash(i, 83);
            var r = (1f + depth * 3.3f) * w / 1600f;
            var px = min.X + w * (Hash(i, 84) + MathF.Sin(t * .3f + i) * .012f);
            var py = min.Y + h * (1.06f - u * 1.18f);
            var a = Ss(0f, .1f, u) * (1f - Ss(.88f, 1f, u)) * (.13f + .17f * depth);
            Glow(dl, new Vector2(px, py), r * 3f, 0x70D4F3, a * .18f);
            dl.AddCircle(new Vector2(px, py), r, U32(170, 236, 252, a), 0, MathF.Max(.55f, r * .14f));
            dl.AddCircleFilled(new Vector2(px - r * .32f, py - r * .38f), MathF.Max(.5f, r * .22f),
                U32(235, 255, 255, a));
        }
    }

    private static void Spore(ImDrawListPtr dl, Vector2 min, float w, float h, float t)
    {
        for (var i = 0; i < 72; i++)
        {
            var life = 10f + Hash(i, 61) * 15f;
            var age = (t * .8f + Hash(i, 62) * life) % life;
            var u = age / life;
            var depth = .25f + .75f * Hash(i, 63);
            var x = min.X + (1.08f - u * 1.2f) * w + MathF.Sin(age * .85f + i) * w * .012f;
            var y = min.Y + Hash(i, 64) * h + age * h * .008f + MathF.Cos(age * .7f + i) * h * .012f;
            var a = Ss(0f, .1f, u) * (1f - Ss(.88f, 1f, u)) * (.18f + .28f * depth);
            var r = (1f + depth * 4f) * w / 1600f;
            Glow(dl, new Vector2(x, y), r * (i % 4 != 0 ? 1.8f : 4f), 0xF0FFCE, a * .5f);
            if (i % 4 != 0)
                dl.AddCircleFilled(new Vector2(x, y), MathF.Max(.6f, r * .38f), U32(245, 255, 214, a));
        }
    }

    private static void Ember(ImDrawListPtr dl, Vector2 min, float w, float h, float t)
    {
        for (var i = 0; i < 34; i++)
        {
            var life = 6f + Hash(i, 51) * 7f;
            var age = (t + Hash(i, 52) * life) % life;
            var u = age / life;
            var x = min.X + Hash(i, 53) * w + MathF.Sin(t * .55f + i) * w * .008f;
            var y = min.Y + (1.08f - u * 1.2f) * h;
            var a = Ss(0f, .1f, u) * (1f - Ss(.8f, 1f, u)) * (.2f + .3f * Hash(i, 54));
            var z = (.7f + Hash(i, 55)) * w / 1600f;
            Glow(dl, new Vector2(x, y), z * 7f, 0xF9874B, a * .4f);
            dl.AddLine(new Vector2(x, y),
                new Vector2(x + z * .4f, y + z * (2f + Hash(i, 56) * 3f)),
                U32(255, 220, 167, a), MathF.Max(.5f, z));
        }
    }

    private static readonly Vector2[] PetalBuffer = new Vector2[14];

    private static void Petal(ImDrawListPtr dl, Vector2 min, float w, float h, float t)
    {
        var petalPoints = PetalBuffer;
        for (var i = 0; i < 32; i++)
        {
            var depth = .35f + .65f * Hash(i, 30);
            var life = 16f + Hash(i, 31) * 15f;
            var age = (t + Hash(i, 32) * life) % life;
            var u = age / life;
            var x = min.X + ((Hash(i, 33) + u * .18f + MathF.Sin(t * .17f + i) * .025f + 1f) % 1f) * w;
            var y = min.Y + (-.08f + u * 1.16f) * h;
            var a = Ss(0f, .08f, u) * (1f - Ss(.88f, 1f, u));
            var r = (3f + depth * 4f) * w / 1600f;
            var turn = t * (.35f + Hash(i, 34) * .5f) + i;
            var cos = MathF.Cos(turn);
            var sin = MathF.Sin(turn);
            var g = 195f + depth * 30f;
            var alpha = a * (80f + depth * 65f) / 255f;
            SamplePetal(petalPoints, 0, x, y, cos, sin, r, -1f, 0f, -.7f, -1.15f, .4f, -.9f, 1f, 0f);
            SamplePetal(petalPoints, 7, x, y, cos, sin, r, 1f, 0f, .1f, .8f, -.5f, .7f, -1f, 0f);
            fixed (Vector2* p = petalPoints)
                dl.AddConvexPolyFilled(p, 13, U32(255, (int)g, 228, alpha));
            if (i % 4 == 0) Glow(dl, new Vector2(x, y), r * 4f, 0xDC92ED, a * .065f);
        }
    }

    private static void SamplePetal(Vector2[] points, int start, float x, float y,
        float cos, float sin, float r,
        float ax, float ay, float bx, float by, float cx, float cy, float dx, float dy)
    {
        for (var s = 0; s < 7; s++)
        {
            var k = s / 6f;
            var m = 1f - k;
            var a0 = m * m * m;
            var a1 = 3f * m * m * k;
            var a2 = 3f * m * k * k;
            var a3 = k * k * k;
            var qx = 0f;
            var qy = 0f;
            SamplePetalPoint(ref qx, ref qy, x, y, cos, sin, r, ax, ay, a0);
            SamplePetalPoint(ref qx, ref qy, x, y, cos, sin, r, bx, by, a1);
            SamplePetalPoint(ref qx, ref qy, x, y, cos, sin, r, cx, cy, a2);
            SamplePetalPoint(ref qx, ref qy, x, y, cos, sin, r, dx, dy, a3);
            points[start + s] = new Vector2(qx, qy);
        }
    }

    private static void SamplePetalPoint(ref float qx, ref float qy, float x, float y,
        float cos, float sin, float r, float px, float py, float w)
    {
        var lx = x + (cos * px - sin * py) * r;
        var ly = y + (sin * px + cos * py) * r;
        qx += lx * w;
        qy += ly * w;
    }

    private static void Frost(ImDrawListPtr dl, Vector2 min, float w, float h, Vector2[]? path, float t)
    {
        var w0 = UiTheme.Win0;
        var light = w0.X * .299f + w0.Y * .587f + w0.Z * .114f > .6f;
        var (dr, dg, db) = light ? (96, 128, 166) : (242, 250, 255);
        var glowRgb = light ? 0x7FA8C9u : 0xBFE3FFu;
        for (var i = 0; i < 64; i++)
        {
            var depth = .25f + .75f * Hash(i, 21);
            var life = 9f + Hash(i, 22) * 8f;
            var age = (t * (.5f + depth * .8f) + Hash(i, 23) * life) % life;
            var u = age / life;
            var sway = MathF.Sin(t * (.4f + Hash(i, 24) * .6f) + i * 1.7f);
            var x = min.X + w * (Hash(i, 25) + sway * (.01f + depth * .02f));
            var y = min.Y + h * (u * 1.14f - .07f);
            var p = path != null ? AmbientWallpaper.ClampIntoPolygon(new Vector2(x, y), path) : new Vector2(x, y);
            var a = Ss(0f, .12f, u) * (1f - Ss(.84f, 1f, u)) * (.2f + depth * .45f);
            a *= .7f + .3f * MathF.Sin(t * (2f + Hash(i, 26) * 3f) + i);
            var r = (.8f + depth * 1.5f) * w / 1600f;
            if (depth > .7f)
            {
                Glow(dl, p, r * 5f, glowRgb, a * .3f);
                Crystal(dl, p, r * 2.6f, t * (.4f + Hash(i, 27) * .5f) + i, dr, dg, db, a);
            }
            else
            {
                dl.AddCircleFilled(p, MathF.Max(.7f, r), U32(dr, dg, db, a));
            }
        }
    }

    private static void Crystal(ImDrawListPtr dl, Vector2 p, float r, float rot,
        int r8, int g8, int b8, float a)
    {
        var thickness = MathF.Max(.7f, r * .16f);
        for (var k = 0; k < 3; k++)
        {
            var ang = rot + k * (MathF.PI / 3f);
            var dx = MathF.Cos(ang) * r;
            var dy = MathF.Sin(ang) * r;
            dl.AddLine(new Vector2(p.X - dx, p.Y - dy), new Vector2(p.X + dx, p.Y + dy),
                U32(r8, g8, b8, a), thickness);
        }
        dl.AddCircleFilled(p, thickness * .9f, U32(r8, g8, b8, Math.Min(a * 1.4f, 1f)));
    }

    private static float Hash(int i, int j)
    {
        var v = (uint)(i * 12539 + j * 8641 + 7937);
        v = unchecked((v ^ (v >> 16)) * 0x45d9f3b);
        return (v & 0xFFFFFF) / 16777216f;
    }

    private static float Ss(float a, float b, float x)
    {
        x = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return x * x * (3f - 2f * x);
    }

    private static uint U32(int r, int g, int b, float a) =>
        ImGui.ColorConvertFloat4ToU32(new Vector4(r / 255f, g / 255f, b / 255f, Math.Clamp(a, 0f, 1f)));

    /// <summary>径向发光：中心着色、边缘透明的多边形 fan（近似径向渐变）。</summary>
    private static void Glow(ImDrawListPtr dl, Vector2 center, float radius, uint rgb, float alpha)
    {
        if (alpha <= 0.004f || radius <= 0f) return;
        const int Ring = 10;
        var r = ((rgb >> 16) & 0xFF) / 255f;
        var g = ((rgb >> 8) & 0xFF) / 255f;
        var b = (rgb & 0xFF) / 255f;
        Span<Vector2> points = stackalloc Vector2[Ring + 1];
        points[0] = center;
        for (var i = 0; i < Ring; i++)
        {
            var angle = i * (MathF.PI * 2f / Ring);
            points[i + 1] = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
        }
        var first = dl.VtxBuffer.Size;
        fixed (Vector2* p = points)
            dl.AddConvexPolyFilled(p, Ring + 1, ImGui.ColorConvertFloat4ToU32(new Vector4(r, g, b, Math.Clamp(alpha, 0f, 1f))));
        var stride = ModernUI.FillVertexStride(dl, first, Ring + 1);
        var outer = ImGui.ColorConvertFloat4ToU32(new Vector4(r, g, b, 0f));
        for (var i = 0; i < Ring; i++)
            ModernUI.WriteFillVertex(dl, first, stride, 1 + i, outer);
    }
}
