using Pylon.ModLoader.Textures;
using Pylon.NaviteAPI.Effects;
using UnityEngine;

namespace Pylon.ModLoader.Effects;

/// <summary>
/// 屏幕效果底层门面：shader / 纹理 / 目标 / 固定 pass / 一帧提交。
/// 作者通常不需要直接碰这里——用 <see cref="ModScreenEffect"/> + <see cref="ScreenCommands"/>。
/// </summary>
public static partial class ScreenGraphics
{
    /// <summary>混合模式（与 <c>bl/color</c> 的 BL_BLEND_* 编号一致）。</summary>
    public enum BlendMode
    {
        Normal = 0,
        Multiply = 1,
        Screen = 2,
        Overlay = 3,
        SoftLight = 4,
        Add = 5,
        Color = 6,
        Luminosity = 7,
    }

    public enum ShaderStatus
    {
        Failed = -1,
        Pending = 0,
        Ready = 1,
    }

    /// <summary>数组 uniform 的显式打包（1..4 个分量一组）。</summary>
    public static UniformArray Array(int components, IEnumerable<float> values) =>
        new(components, values.ToArray());

    public sealed class UniformArray
    {
        public int Components { get; }

        public float[] Values { get; }

        public UniformArray(int components, float[] values)
        {
            if (components < 1 || components > 4)
                throw new ArgumentOutOfRangeException(nameof(components), "ScreenGraphics.Array: components 必须是 1..4");
            Components = components;
            Values = values ?? throw new ArgumentNullException(nameof(values));
        }
    }

    private static bool _installTried;
    private static readonly float[] BoxBuf = new float[4];
    private static readonly float[] CenterBuf = new float[2];
    private static readonly float[] ColorBuf = new float[4];

    public static bool Installed
    {
        get
        {
            try { return ScreenEffectsNative.Available() != 0; }
            catch { return false; }
        }
    }

    public static void Install()
    {
        if (_installTried) return;
        _installTried = true;
        if (!PylonTexture.IsGameThread)
        {
            _installTried = false;
            throw new ScreenEffectsException("ScreenGraphics.Install 只能在游戏线程(UnityMain)上调用");
        }
        try
        {
            var ok = ScreenEffectsNative.Install() != 0;
            UI.ModUI.Logger?.Info($"屏幕效果：hooks {(ok ? "已装" : "安装失败（见日志）")}");
        }
        catch (Exception e)
        {
            _installTried = false;
            UI.ModUI.Logger?.Warn($"屏幕效果安装异常：{e.Message}");
        }
    }

    public static void Submit(ScreenFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Draws.Count == 0) return;
        Install();

        ScreenEffectsNative.SubmitBegin(frame.History ? 1 : 0);
        try
        {
            foreach (var target in frame.Targets)
                ScreenEffectsNative.SubmitAddTarget(target.Texture, target.Name, target.Scale, target.Persistent ? 1 : 0);

            foreach (var draw in frame.Draws)
            {
                int handle;
                if (draw.Mesh)
                {
                    var verts = draw.MeshVertices;
                    if (verts is null || verts.Length == 0 || verts.Length % 12 != 0) continue;
                    var count = verts.Length / 12;
                    if (count % 3 != 0) continue;
                    handle = ScreenEffectsNative.SubmitAddMesh(
                        draw.Shader?.Id ?? 0,
                        (int)draw.Layer,
                        draw.Dest,
                        (int)draw.MeshBlend,
                        count,
                        verts);
                    if (handle <= 0) continue;
                }
                else
                {
                    float[]? box = null;
                    if (draw.Box is { Length: 4 } candidate)
                    {
                        box = candidate;
                        System.Array.Copy(candidate, BoxBuf, 4);
                    }
                    CenterBuf[0] = draw.Center.x;
                    CenterBuf[1] = draw.Center.y;
                    ColorBuf[0] = draw.Color.r;
                    ColorBuf[1] = draw.Color.g;
                    ColorBuf[2] = draw.Color.b;
                    ColorBuf[3] = draw.Color.a;
                    handle = ScreenEffectsNative.SubmitAddDraw(
                        draw.Shader?.Id ?? 0,
                        (int)draw.Layer,
                        draw.Dest,
                        draw.Source?.Texture ?? kTexNone,
                        box is null ? null : BoxBuf,
                        CenterBuf,
                        draw.Radius,
                        draw.Intensity,
                        draw.Progress,
                        draw.Age,
                        ColorBuf);
                    if (handle <= 0) continue;
                }
                draw.Submit(handle);
            }
        }
        finally
        {
            ScreenEffectsNative.SubmitEnd();
        }
    }

    public static void RequestTrace() => ScreenEffectsNative.RequestTrace();

    public static void SetMeshStreaming(bool enabled) => ScreenEffectsNative.SetMeshStreaming(enabled ? 1 : 0);

    /// <summary>常用 pass：拷贝、阈值、横/竖模糊（给 Blit 与 Bloom/Blur 这类多步效果用）。</summary>
    public static class Passes
    {
        private static readonly Dictionary<string, Shader> Cache = new(StringComparer.Ordinal);

        private static Shader Get(string name, string source)
        {
            lock (Cache)
            {
                if (!Cache.TryGetValue(name, out var shader))
                    Cache[name] = shader = Shader.FromSource(source, "bl/" + name);
                return shader;
            }
        }

        private static string Blur(string axis) => $@"
uniform float uBlurRadius;

void main() {{
    vec2 stride = {axis} * max(uBlurRadius, 0.0) / 4.0;
    vec4 sum = blSource(vUV) * 0.2270270270;
    sum += (blSource(vUV + stride * 1.3846153846) + blSource(vUV - stride * 1.3846153846)) * 0.3162162162;
    sum += (blSource(vUV + stride * 3.2307692308) + blSource(vUV - stride * 3.2307692308)) * 0.0702702703;
    fragColor = sum;
}}
";

        public static Shader Copy => Get("Copy", "void main() {\n    fragColor = blSource(vUV);\n}\n");

        public static Shader Threshold => Get("Threshold", @"
#include <bl/color>
uniform float uThreshold;
uniform float uKnee;

void main() {
    vec4 c = blSource(vUV);
    float keep = smoothstep(uThreshold, uThreshold + max(uKnee, 1e-4), blLuma(c.rgb));
    fragColor = vec4(c.rgb * keep, 1.0);
}
");

        public static Shader BlurH => Get("BlurH", Blur("vec2(uSourceTexel.x, 0.0)"));

        public static Shader BlurV => Get("BlurV", Blur("vec2(0.0, uSourceTexel.y)"));
    }
}