using System;

namespace Pylon.ImGui;

public static class Fx
{
    public static readonly Random Rnd = new();

    public static float Rnd01() => (float)Rnd.NextDouble();
    public static float RndRange(float a, float b) => a + (b - a) * Rnd01();
    public static uint Col(byte r, byte g, byte b, byte a)
        => ImGuiNative.igColor4(r / 255f, g / 255f, b / 255f, a / 255f);
    public static uint WithAlpha(uint col, float a)
    {
        uint a8 = (uint)Math.Clamp(a * 255f, 0f, 255f);
        return (col & 0x00FFFFFFu) | (a8 << 24);
    }
}

/// <summary>点阵背景（GlassKit.h DrawDotGrid）。</summary>
public struct DotGrid
{
    public void Draw(DrawList dl, ImVec2 min, ImVec2 max, uint dotColor,
                     float spacing = 20f, float cornerRad = 0f)
    {
        dl.PushClipRect(min, max, true);
        for (float y = min.Y + spacing * 0.5f; y < max.Y; y += spacing)
        {
            for (float x = min.X + spacing * 0.5f; x < max.X; x += spacing)
            {
                if (cornerRad > 0.5f)
                {
                    float cx = -1f, cy = -1f;
                    if (x < min.X + cornerRad && y < min.Y + cornerRad) { cx = min.X + cornerRad; cy = min.Y + cornerRad; }
                    else if (x > max.X - cornerRad && y < min.Y + cornerRad) { cx = max.X - cornerRad; cy = min.Y + cornerRad; }
                    else if (x < min.X + cornerRad && y > max.Y - cornerRad) { cx = min.X + cornerRad; cy = max.Y - cornerRad; }
                    else if (x > max.X - cornerRad && y > max.Y - cornerRad) { cx = max.X - cornerRad; cy = max.Y - cornerRad; }
                    if (cx >= 0f)
                    {
                        float dx = x - cx, dy = y - cy;
                        if (dx * dx + dy * dy > cornerRad * cornerRad) continue;
                    }
                }
                dl.AddRectFilled(new ImVec2(x - 0.8f, y - 0.8f), new ImVec2(x + 0.8f, y + 0.8f), dotColor);
            }
        }
        dl.PopClipRect();
    }
}

/// <summary>彩色光束雨（GlassKit.h BeamRain）：下落光束 + 落地溅射。</summary>
public class BeamRain
{
    private struct Beam { public float X, Y, Len, Speed, Life, MaxLife; public bool Hit; }
    private struct Splash
    {
        public float X, Y, Life, MaxLife;
        public float[] PvX, PvY, PpX, PpY, Ps, Pk;
        public int Pn;
        public bool Alive;
    }

    private const int kMax = 22;
    private const int kSplash = 14;
    private readonly Beam[] _beams = new Beam[kMax];
    private readonly Splash[] _splashes = new Splash[kSplash];
    private bool _inited;

    /// <summary>彩色光束的调色板（GlassKit 固定四色：紫/蓝/粉/绿）。</summary>
    private static readonly uint[] kTints =
    {
        Fx.Col(139, 124, 255, 255),
        Fx.Col(90, 156, 255, 255),
        Fx.Col(192, 132, 252, 255),
        Fx.Col(58, 214, 126, 255),
    };

    public void Update(float dt)
    {
        for (int i = 0; i < kMax; i++)
        {
            var b = _beams[i];
            b.Y += b.Speed * dt;
            _beams[i] = b;
        }
        for (int i = 0; i < kSplash; i++)
        {
            var s = _splashes[i];
            if (!s.Alive) continue;
            s.Life -= dt;
            for (int k = 0; k < s.Pn; k++)
            {
                s.PvY[k] += 55f * dt;
                s.PvX[k] *= (1f - 1.4f * dt);
                s.PvY[k] *= (1f - 1.4f * dt);
                s.PpX[k] += s.PvX[k] * dt;
                s.PpY[k] += s.PvY[k] * dt;
            }
            if (s.Life <= 0f) s.Alive = false;
            _splashes[i] = s;
        }
    }

    public void Draw(DrawList dl, ImVec2 min, ImVec2 max, uint color, float alphaScale = 1f)
    {
        if (!_inited)
        {
            for (int i = 0; i < kMax; i++) Respawn(i, min, max);
            _inited = true;
        }
        float floorY = max.Y - 3f;
        dl.PushClipRect(min, max, true);

        for (int i = 0; i < kMax; i++)
        {
            var b = _beams[i];
            if (!b.Hit && b.Y >= floorY)
            {
                b.Hit = true;
                SpawnSplash(b.X, floorY);
            }
            if (b.Y - b.Len > floorY) { Respawn(i, min, max); continue; }
            float fade = 1f;
            if (b.Y < min.Y + 40f) fade = (b.Y - (min.Y - b.Len)) / (b.Len + 40f);
            if (fade < 0f) fade = 0f;
            if (fade > 1f) fade = 1f;
            float y0 = b.Y < floorY ? b.Y : floorY;
            float y1 = b.Y - b.Len;

            uint cu = (i % 4 == 0) ? color : kTints[i % 4];
            float r = ((cu >> 0) & 0xFF) / 255f, g = ((cu >> 8) & 0xFF) / 255f, bl = ((cu >> 16) & 0xFF) / 255f;

            dl.AddLine(new ImVec2(b.X, y1 + b.Len * 0.12f), new ImVec2(b.X, y0),
                       Fx.WithAlpha(cu, 0.18f * fade * alphaScale), 5f);
            dl.AddLine(new ImVec2(b.X, y1), new ImVec2(b.X, y0),
                       Fx.WithAlpha(cu, 0.09f * fade * alphaScale), 10f);
            const int seg = 12;
            for (int s = 0; s < seg; s++)
            {
                float t0 = (float)s / seg, t1 = (float)(s + 1) / seg;
                var p0 = new ImVec2(b.X, y1 + (y0 - y1) * t0);
                var p1 = new ImVec2(b.X, y1 + (y0 - y1) * t1);
                dl.AddLine(p0, p1, Fx.WithAlpha(cu, (0.10f + 0.88f * t0 * t0) * fade * alphaScale), 2f);
            }
            if (b.Y < floorY)
                dl.AddCircleFilled(new ImVec2(b.X, b.Y), 2.2f, Fx.WithAlpha(cu, 0.90f * fade * alphaScale), 8);
            _beams[i] = b;
        }

        for (int i = 0; i < kSplash; i++)
        {
            var s = _splashes[i];
            if (!s.Alive) continue;
            float k = s.Life / s.MaxLife;
            float age = 1f - k;
            for (int g = 3; g >= 1; g--)
            {
                float hw = (8f + age * 14f) * (0.6f + 0.4f * g);
                float hh = 2.6f * g;
                dl.AddRectFilled(new ImVec2(s.X - hw, s.Y - hh), new ImVec2(s.X + hw, s.Y + hh),
                                 Fx.WithAlpha(color, 0.32f * k / g), hh);
            }
            for (int q = 0; q < s.Pn; q++)
            {
                float pk = k * s.Pk[q];
                if (pk <= 0f) continue;
                float sz = s.Ps[q] * (0.5f + 0.5f * pk);
                dl.AddRectFilled(new ImVec2(s.PpX[q] - sz, s.PpY[q] - sz), new ImVec2(s.PpX[q] + sz, s.PpY[q] + sz),
                                 Fx.WithAlpha(color, pk), 0.8f);
            }
        }
        dl.PopClipRect();
    }

    private void Respawn(int idx, ImVec2 min, ImVec2 max)
    {
        var b = _beams[idx];
        b.X = Fx.RndRange(min.X + 12, max.X - 12);
        b.Len = Fx.RndRange(90, 240);
        b.Y = min.Y - b.Len - Fx.RndRange(0, 420);
        b.Speed = Fx.RndRange(140, 320);
        b.MaxLife = 1f; b.Life = 1f;
        b.Hit = false;
        _beams[idx] = b;
    }

    private void SpawnSplash(float x, float y)
    {
        int idx = -1;
        for (int i = 0; i < kSplash; i++) if (!_splashes[i].Alive) { idx = i; break; }
        if (idx < 0) return;
        var s = _splashes[idx];
        s.X = x; s.Y = y;
        s.MaxLife = 0.8f; s.Life = s.MaxLife;
        s.Alive = true;
        s.Pn = 7 + Fx.Rnd.Next(3);
        if (s.PvX == null)
        {
            s.PvX = new float[10]; s.PvY = new float[10];
            s.PpX = new float[10]; s.PpY = new float[10];
            s.Ps = new float[10]; s.Pk = new float[10];
        }
        for (int i = 0; i < s.Pn; i++)
        {
            float ang = -1.5708f + Fx.RndRange(-1f, 1f);
            float spd = Fx.RndRange(40, 130);
            s.PvX[i] = (float)Math.Cos(ang) * spd;
            s.PvY[i] = (float)Math.Sin(ang) * spd;
            s.PpX[i] = x + Fx.RndRange(-2, 2);
            s.PpY[i] = y - Fx.RndRange(0, 4);
            s.Ps[i] = Fx.RndRange(1.3f, 3.4f);
            s.Pk[i] = Fx.RndRange(0.6f, 1f);
        }
        _splashes[idx] = s;
    }
}

/// <summary>上浮光尘（GlassKit.h FloatParticles）：摆动 + 淡入淡出。</summary>
public class FloatParticles
{
    private struct P
    {
        public float X, Y, Speed, Size, Phase, Wobble;
        public float Life, MaxLife, Alpha;
        public uint Col;
    }

    private const int kMax = 22;
    private readonly P[] _parts = new P[kMax];
    private float _t;
    private ImVec2 _min, _max;

    private static readonly uint[] kCols =
    {
        Fx.Col(139, 124, 255, 255), Fx.Col(90, 156, 255, 255),
        Fx.Col(192, 132, 252, 255), Fx.Col(255, 255, 255, 255),
        Fx.Col(58, 214, 126, 255),
    };

    public void Update(float dt)
    {
        _t += dt;
        for (int i = 0; i < kMax; i++)
        {
            var p = _parts[i];
            if (p.MaxLife <= 0f) { Respawn(i); continue; }
            p.Life += dt;
            if (p.Life >= p.MaxLife || p.Y < _min.Y - 30f) { Respawn(i); continue; }
            p.Y -= p.Speed * dt;
            p.X += (float)Math.Sin(_t * p.Wobble + p.Phase) * 18f * dt;
            _parts[i] = p;
        }
    }

    public void Draw(DrawList dl, ImVec2 min, ImVec2 max, float alphaScale = 1f)
    {
        _min = min; _max = max;
        dl.PushClipRect(min, max, true);
        for (int i = 0; i < kMax; i++)
        {
            var p = _parts[i];
            if (p.MaxLife <= 0f || p.Life >= p.MaxLife) continue;
            float k = 1f - p.Life / p.MaxLife;
            float a = 1f;
            if (k < 0.12f) a = k / 0.12f;
            else if (k > 0.88f) a = (1f - k) / 0.12f;
            if (a <= 0.01f) continue;
            float rad = p.Size * (0.6f + 0.4f * k);
            var pos = new ImVec2(p.X, p.Y);
            dl.AddCircleFilled(pos, rad * 2.4f, Fx.WithAlpha(p.Col, a * p.Alpha * alphaScale * 0.35f), 10);
            dl.AddCircleFilled(pos, rad, Fx.WithAlpha(p.Col, a * p.Alpha * alphaScale), 12);
        }
        dl.PopClipRect();
    }

    private void Respawn(int idx)
    {
        if (_max.Y <= _min.Y) { _parts[idx] = new P(); return; }
        var p = _parts[idx];
        p.X = Fx.RndRange(_min.X, _max.X);
        p.Y = _max.Y + Fx.RndRange(0f, 60f);
        p.Speed = Fx.RndRange(24f, 70f);
        p.Size = Fx.RndRange(1.6f, 4.2f);
        p.Phase = Fx.RndRange(0f, 6.2831853f);
        p.Wobble = Fx.RndRange(0.4f, 1.4f);
        p.MaxLife = (_max.Y - _min.Y + 60f) / p.Speed + Fx.RndRange(0f, 2f);
        p.Life = 0f;
        p.Alpha = Fx.RndRange(0.45f, 0.85f);
        p.Col = kCols[Fx.Rnd.Next(5)];
        _parts[idx] = p;
    }
}

/// <summary>闪烁星光（GlassKit.h TwinkleStars）：白色光点 + 淡紫光晕。</summary>
public class TwinkleStars
{
    private struct S { public float X, Y, Size, Phase, Speed; }
    private const int kCount = 18;
    private readonly S[] _stars = new S[kCount];
    private float _t;
    private bool _inited;

    public void Update(float dt) => _t += dt;

    public void Draw(DrawList dl, ImVec2 min, ImVec2 max)
    {
        if (!_inited) { Init(min, max); _inited = true; }
        dl.PushClipRect(min, max, true);
        for (int i = 0; i < kCount; i++)
        {
            var s = _stars[i];
            float a = 0.10f + 0.70f * (0.5f + 0.5f * (float)Math.Sin(s.Phase + _t * s.Speed));
            if (a < 0.02f) continue;
            var pos = new ImVec2(s.X, s.Y);
            dl.AddCircleFilled(pos, s.Size, Fx.WithAlpha(0xFFFFFFFF, a), 8);
            dl.AddCircleFilled(pos, s.Size * 3f, Fx.Col(0x8C, 0x7D, 0xFF, (byte)Math.Clamp(a * 0.30f * 255f, 0, 255)), 10);
        }
        dl.PopClipRect();
    }

    private void Init(ImVec2 min, ImVec2 max)
    {
        for (int i = 0; i < kCount; i++)
        {
            var s = _stars[i];
            s.X = Fx.RndRange(min.X, max.X);
            s.Y = Fx.RndRange(min.Y, max.Y);
            s.Size = Fx.RndRange(1f, 2.2f);
            s.Phase = Fx.RndRange(0f, 6.2831853f);
            s.Speed = Fx.RndRange(0.6f, 2.2f);
            _stars[i] = s;
        }
    }
}

/// <summary>极光（GlassKit.h AuroraWave）：顶部渐变光带 + 飘动幕帘。</summary>
public class AuroraWave
{
    private float _t;

    public void Update(float dt) => _t += dt;

    public void Draw(DrawList dl, ImVec2 min, ImVec2 max, float alphaScale = 1f, float cornerRad = 0f)
    {
        // 首帧布局前尺寸可能为 0 或负：非法区域不画（否则 Math.Clamp 因 min>max 抛异常，原生绘制也断言）。
        float W = max.X - min.X;
        float H = max.Y - min.Y;
        if (W <= 0f || H <= 0f) return;

        dl.PushClipRect(min, max, true);
        for (int b = 0; b < 2; b++)
        {
            float phase = b == 0 ? 0f : 2.1f;
            float breathe = 0.62f + 0.38f * (float)Math.Sin(_t * 0.28f + phase);
            float hgt = H * (b == 0 ? 0.38f : 0.28f);
            int al = (int)Math.Clamp((b == 0 ? 150f : 105f) * breathe * alphaScale, 0f, 255f);
            if (al <= 1) continue;
            int cR = b == 0 ? 139 : 90;
            int cG = b == 0 ? 124 : 156;
            // 顶部光带圆角 = 窗口圆角（clamp 到光带高度一半），与 Chrome 窗口圆角贴合
            float rounding = Math.Clamp(cornerRad, 0f, hgt * 0.5f);
            dl.AddRoundedGradientRectTop(new ImVec2(min.X, min.Y), new ImVec2(max.X, min.Y + hgt),
                                         rounding,
                                         Fx.Col((byte)cR, (byte)cG, 255, (byte)al),
                                         Fx.Col((byte)cR, (byte)cG, 255, 0));
        }
        const int slices = 6;
        for (int i = 0; i < 4; i++)
        {
            float ph = i * 2.1f;
            float cx = min.X + W * (0.18f + 0.21f * i) + (float)Math.Sin(_t * 0.15f + ph) * W * 0.10f;
            float halfW = W * (0.17f + 0.04f * i);
            float curtainH = H * 0.52f;
            int cr = i == 1 ? 90 : 139;
            int cg = i == 1 ? 156 : 124;
            float baseA = (i == 1 ? 62f : 78f) * alphaScale;
            float breathe = 0.72f + 0.28f * (float)Math.Sin(_t * 0.30f + ph);
            float x0 = cx - halfW, x1 = cx + halfW, xm = cx;
            for (int k = 0; k < slices; k++)
            {
                float f0 = (float)k / slices;
                float f1 = (float)(k + 1) / slices;
                float y0 = min.Y + curtainH * f0;
                float y1 = min.Y + curtainH * f1;
                int aTop = (int)Math.Clamp(baseA * (1f - f0) * breathe, 0f, 255f);
                int aBot = (int)Math.Clamp(baseA * (1f - f1) * breathe, 0f, 255f);
                if (aTop <= 1 && aBot <= 1) continue;
                uint cT = Fx.Col((byte)cr, (byte)cg, 255, (byte)aTop);
                uint cB = Fx.Col((byte)cr, (byte)cg, 255, (byte)aBot);
                uint c0 = Fx.Col((byte)cr, (byte)cg, 255, 0);
                dl.AddRectFilledMultiColor(new ImVec2(x0, y0), new ImVec2(xm, y1), c0, cT, cB, c0);
                dl.AddRectFilledMultiColor(new ImVec2(xm, y0), new ImVec2(x1, y1), cT, c0, c0, cB);
            }
        }
        dl.PopClipRect();
    }
}