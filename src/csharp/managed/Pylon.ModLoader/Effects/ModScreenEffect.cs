using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Content;

namespace Pylon.ModLoader.Effects;

/// <summary>
/// 一个可生成的屏幕后处理效果。派生类覆写 <see cref="SetDefaults"/> 配参数、
/// <see cref="Draw"/> 提交 shader（或用 <see cref="DrawDistortion"/> 做扭曲）。
/// 注册由框架自动完成：类型被内容扫描发现后进 <see cref="ScreenEffects"/> 的注册表。
/// </summary>
public abstract class ModScreenEffect : ModType
{
    private bool _killed;

    internal Func<Vector2?>? follow;

    /// <summary>本效果用的着色器。null 时按 <see cref="Source"/> / <see cref="ShaderPath"/> 解析。</summary>
    public ScreenGraphics.Shader? Shader { get; set; }

    public ScreenEffects.Layer Layer { get; set; }

    /// <summary>效果中心（世界坐标）。null = 整屏。</summary>
    public Vector2? Center { get; set; }

    /// <summary>半径（像素）。</summary>
    public float Radius { get; set; } = 64;

    public float Intensity { get; set; } = 1;

    public Color? Color { get; set; }

    /// <summary>寿命（tick）；0 = 无限，靠 <see cref="Kill"/> 结束。</summary>
    public float Duration { get; set; }

    public float FadeIn { get; set; }

    public float FadeOut { get; set; }

    /// <summary>同 key 再生成会续命而不是叠加。</summary>
    public string? Key { get; set; }

    /// <summary>同一帧内的绘制顺序（小的先画）。</summary>
    public int Order { get; set; }

    /// <summary>包围盒在半径外多留的边（像素），给发光/模糊留余量。</summary>
    public float CaptureMargin { get; set; } = 8;

    public bool Fullscreen { get; set; }

    public ScreenUtils.Mask? Mask { get; set; }

    /// <summary>额外 uniform（值类型见 <see cref="ScreenCommands.ToUniform"/> 支持的集合）。</summary>
    public Dictionary<string, object> Uniforms { get; } = new();

    public float Age { get; set; }

    public bool Ended { get; internal set; }

    public bool Following => follow != null;

    /// <summary>内联 GLSL（覆写它就等于自带 shader）；null = 用 <see cref="ShaderPath"/> 的文件。</summary>
    public virtual string? Source => null;

    /// <summary>默认 shader 文件路径：命名空间/类名，按 <c>.frag</c> 解析。</summary>
    public virtual string ShaderPath => (GetType().Namespace + "." + Name).Replace('.', '/');

    public bool Active => !_killed;

    public float Progress => Duration > 0 ? MathF.Min(Age / Duration, 1) : 0;

    public float TimeLeft => Duration > 0 ? MathF.Max(Duration - Age, 0) : float.PositiveInfinity;

    public float Opacity
    {
        get
        {
            float opacity = 1;
            if (FadeIn > 0) opacity *= MathF.Min(Age / FadeIn, 1);
            if (Duration > 0 && FadeOut > 0) opacity *= MathF.Min(TimeLeft / FadeOut, 1);
            return opacity;
        }
    }

    public virtual void SetDefaults() { }

    public virtual void OnSpawn() { }

    public virtual void Update() { }

    public virtual bool ShouldDraw() => true;

    public virtual void DrawDistortion(DistortionMap d) { }

    public virtual void Draw(ScreenCommands cmd)
    {
        if (Shader != null) cmd.DrawShader();
    }

    public virtual void OnKill() { }

    public void Kill() => _killed = true;

    public ScreenUtils.Tween Tween(string property, float to, float duration, Func<float, float>? easing = null,
        float delay = 0, Action<object>? onDone = null) =>
        ScreenUtils.Tween.To(this, property, to, duration, easing, delay, onDone);
}