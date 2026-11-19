using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;

namespace Pylon.ModLoader.Effects;

/// <summary>一步绘制的可选覆盖项：不填就取效果的默认值。</summary>
public sealed class ScreenDrawOptions
{
    public Vector2? Center { get; set; }

    public float? Radius { get; set; }

    public float? Margin { get; set; }

    public bool? Fullscreen { get; set; }

    public Dictionary<string, object> Uniforms { get; } = new();

    public Dictionary<string, ScreenGraphics.Target> Textures { get; } = new();

    public ScreenUtils.Mask? Mask { get; set; }

    public ScreenGraphics.Target? Source { get; set; }

    public ScreenEffects.Layer? Layer { get; set; }
}

/// <summary>效果提交一帧时的绘制指令：世界坐标自动换算成 UV、包围盒、uniform 与纹理绑定。</summary>
public sealed class ScreenCommands
{
    private readonly ModScreenEffect _effect;
    private readonly ScreenFrame _frame;

    internal ScreenCommands(ModScreenEffect effect, ScreenFrame frame)
    {
        _effect = effect;
        _frame = frame;
    }

    public ScreenGraphics.Target Target(string name, float scale = 1, bool persistent = false) =>
        _frame.Target(Qualify(name), scale, persistent);

    public void DrawShader(ScreenDrawOptions? options = null) => DrawShader(_effect.Shader, options);

    public void DrawShader(ScreenGraphics.Shader? shader, ScreenDrawOptions? options = null) =>
        Push(shader, null, options ?? new ScreenDrawOptions(), null, "DrawShader");

    public void Blit(ScreenGraphics.Target source, ScreenGraphics.Target dest,
        ScreenGraphics.Shader? shader = null, ScreenDrawOptions? options = null)
    {
        if (dest == null || dest.Texture <= 0) throw new ArgumentException("cmd.Blit: dest 必须是 cmd.Target(...)");
        Push(shader ?? ScreenGraphics.Passes.Copy, dest, options ?? new ScreenDrawOptions(), source, "Blit");
    }

    private string Qualify(string name)
    {
        var prefix = _effect.GetType().Name + ":";
        return name.StartsWith(prefix, StringComparison.Ordinal) ? name : prefix + name;
    }

    private void Push(ScreenGraphics.Shader? shader, ScreenGraphics.Target? dest, ScreenDrawOptions options,
        ScreenGraphics.Target? source, string what)
    {
        ModScreenEffect fx = _effect;
        if (shader == null)
            throw new InvalidOperationException(
                $"cmd.{what}: {fx.GetType().Name} 没有 shader（放一个同名 .frag、覆写 Source，或在 SetDefaults 里给 Shader）");

        Vector2? center = options.Center ?? fx.Center;
        float radius = options.Radius ?? fx.Radius;
        float margin = options.Margin ?? fx.CaptureMargin;
        bool fullscreen = options.Fullscreen ?? fx.Fullscreen;
        Vector2 uv = center is Vector2 c ? ScreenUtils.Space.WorldToUV(c) : new Vector2(0.5f, 0.5f);
        float r = ScreenUtils.Space.PixelsToUV(radius);
        ScreenUtils.Mask mask = options.Mask ?? fx.Mask ?? ScreenUtils.Mask.None();
        float[] box = fullscreen || center == null
            ? new float[] { 0, 0, 1, 1 }
            : Box(uv, r + ScreenUtils.Space.PixelsToUV(margin));
        if (mask.Bounds() is float[] limit)
            box = new[] { MathF.Max(box[0], limit[0]), MathF.Max(box[1], limit[1]), MathF.Min(box[2], limit[2]), MathF.Min(box[3], limit[3]) };

        var draw = new ScreenEffect(shader)
        {
            Layer = options.Layer ?? fx.Layer,
            Dest = dest?.Texture ?? 0,
            Source = options.Source ?? source,
            Box = box,
            Center = new UnityEngine.Vector2(uv.X, uv.Y),
            Radius = r,
            Intensity = fx.Intensity * fx.Opacity,
            Progress = fx.Progress,
            Age = fx.Age / 60f,
            Color = fx.Color is Color color ? ToUnityColor(color) : new UnityEngine.Color(1f, 1f, 1f, 1f),
        };
        var uniforms = new Dictionary<string, object>();
        foreach (var map in new[] { mask.ToUniforms(), fx.Uniforms, options.Uniforms })
            foreach (var pair in map)
                uniforms[pair.Key] = pair.Value;
        foreach (var pair in uniforms)
            if (ToUniform(pair.Value) is (int components, float[] values))
                draw.Uniform(pair.Key, components, values);
        var textures = new Dictionary<string, ScreenGraphics.Target>(options.Textures);
        if (mask.TextureSource is ScreenGraphics.Target maskTexture) textures["uMaskTexture"] = maskTexture;
        foreach (var pair in textures)
            draw.Texture(pair.Key, pair.Value);
        _frame.Add(draw);
    }

    private static float[] Box(Vector2 uv, float r)
    {
        float rx = r / ScreenUtils.Space.Aspect;
        return new[] { uv.X - rx, uv.Y - r, uv.X + rx, uv.Y + r };
    }

    private static UnityEngine.Color ToUnityColor(Color color) =>
        new(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);

    /// <summary>把作者给的 uniform 值转成 (分量数, 浮点数组)；不认识的类型返回 null（跳过）。</summary>
    public static (int Components, float[] Values)? ToUniform(object? value) => value switch
    {
        null => null,
        ScreenGraphics.UniformArray array => (array.Components, array.Values),
        float f => (1, new[] { f }),
        double d => (1, new[] { (float)d }),
        int i => (1, new float[] { i }),
        bool b => (1, new float[] { b ? 1 : 0 }),
        Vector2 v => (2, new[] { v.X, v.Y }),
        Vector3 v => (3, new[] { v.X, v.Y, v.Z }),
        Vector4 v => (4, new[] { v.X, v.Y, v.Z, v.W }),
        Color c => (4, ScreenUtils.ColorMath.ToFloats(c)),
        string hex => (4, ScreenUtils.ColorMath.ToFloats(ScreenUtils.ColorMath.FromHex(hex))),
        float[] a when a.Length is > 0 and <= 4 => (a.Length, (float[])a.Clone()),
        _ => null,
    };
}