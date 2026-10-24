using System.Numerics;

namespace Pylon.ModLoader.ImGuiHost;

/// <summary>动画时长与缓动常量、通用插值工具；所有包络在动效关闭时直达终值。</summary>
internal static class Motion
{
    public const float NavFoldSeconds = 0.32f;
    public const float NavMarkerSeconds = 0.34f;
    public const float CardStaggerDelay = 0.06f;
    public const float CardInDuration = 0.5f;
    public const float CardInOffset = 16f;
    public const float OpenSeconds = 0.65f;
    public const float CloseSeconds = 0.42f;
    public const float ToggleSeconds = 0.22f;
    public const float BumpSeconds = 0.32f;
    public const float StatusPopSeconds = 0.3f;
    public const float ToastSeconds = 0.3f;
    public const float ToastHoldSeconds = 1.8f;
    public const float BallBobSeconds = 3.8f;
    public const float BallBobAmplitude = 5f;
    public const float LogoBobSeconds = 4.5f;
    public const float LogoBobAmplitude = 3f;
    public const float BlobBreathSeconds = 9f;
    public const float GlyphFloatSeconds = 12f;
    public const float PulseSeconds = 2f;
    public const float PageInDuration = 0.32f;
    public const float ScrollbarSeconds = 0.3f;

    public static float Time { get; private set; }

    public static bool Enabled => UiSettings.Animations;

    public static void Tick(float deltaTime) => Time += Math.Clamp(deltaTime, 0f, 0.1f);

    public static float Bob(float seconds, float amplitude, float phase = 0f) =>
        Enabled ? MathF.Sin((Time + phase) * (MathF.PI * 2f / seconds)) * amplitude : 0f;

    public static float Cycle(float seconds, float phase = 0f)
    {
        if (!Enabled) return 0f;
        var t = (Time + phase) / seconds;
        return t - MathF.Floor(t);
    }

    public static float Progress(float start, float seconds)
    {
        if (!Enabled || seconds <= 0f) return 1f;
        return Math.Clamp((Time - start) / seconds, 0f, 1f);
    }

    public static float OutBack(float t, float overshoot = 1.3f)
    {
        var x = t - 1f;
        return 1f + (overshoot + 1f) * x * x * x + overshoot * x * x;
    }

    public static float OutCubic(float t)
    {
        var x = 1f - t;
        return 1f - x * x * x;
    }

    public static float InCubic(float t) => t * t * t;

    public static float InOutSine(float t) => 0.5f - 0.5f * MathF.Cos(MathF.PI * t);

    public static float OutExpo(float t) => t >= 1f ? 1f : 1f - MathF.Pow(2f, -10f * t);

    public static float Bump(float t) => 1f + 0.22f * MathF.Sin(MathF.PI * Math.Clamp(t, 0f, 1f));

    /// <summary>导航指示条的欠阻尼弹簧；进度型动画之外的位置类动效都用它。</summary>
    public static void Spring(ref float value, ref float velocity, float target, float dt,
                              float stiffness = 170f, float damping = 17f)
    {
        if (!Enabled)
        {
            value = target;
            velocity = 0f;
            return;
        }
        dt = Math.Clamp(dt, 0f, 1f / 30f);
        velocity += (target - value) * stiffness * dt;
        velocity *= MathF.Exp(-damping * dt);
        value += velocity * dt;
        if (MathF.Abs(target - value) < 0.25f && MathF.Abs(velocity) < 6f)
        {
            value = target;
            velocity = 0f;
        }
    }

    /// <summary>页面卡片交错入场：返回该卡片的透明度与上浮偏移。</summary>
    public static (float Alpha, float OffsetY) CardEnter(int index, float pageStart)
    {
        if (pageStart < 0f) return (1f, 0f);
        var t = Progress(pageStart + 0.03f + index * CardStaggerDelay, CardInDuration);
        if (t <= 0f) return (0f, CardInOffset);
        return (Math.Clamp(t, 0f, 1f), (1f - OutBack(t, 1.15f)) * CardInOffset);
    }

    public static Vector2 Lerp(Vector2 a, Vector2 b, float t) => new(
        a.X + (b.X - a.X) * t,
        a.Y + (b.Y - a.Y) * t);
}
