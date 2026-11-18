using Pylon.NaviteAPI.Effects;
using UnityEngine;

namespace Pylon.ModLoader.Effects;

/// <summary>
/// 一步效果：shader + 位置/参数 + uniforms/textures，加进 ScreenFrame 提交。
/// </summary>
public sealed class ScreenEffect(ScreenShader shader)
{
    public ScreenShader Shader { get; set; } = shader ?? throw new ArgumentNullException(nameof(shader));

    public ScreenEffectLayer Layer { get; set; } = ScreenEffectLayer.World;

    public int Dest { get; set; }

    public ScreenTexture? Source { get; set; }

    public float[]? Box { get; set; }

    public bool Mesh { get; set; }

    public ScreenMeshBlend MeshBlend { get; set; } = ScreenMeshBlend.Additive;

    public float[]? MeshVertices { get; set; }

    public Vector2 Center { get; set; } = new(0.5f, 0.5f);

    public float Radius { get; set; }

    public float Intensity { get; set; } = 1f;

    public float Progress { get; set; }

    public float Age { get; set; }

    public Color Color { get; set; } = Color.white;

    private readonly List<UniformEntry> _uniforms = [];
    private readonly List<TextureEntry> _textures = [];

    private sealed class UniformEntry(string name, int components, float[] values)
    {
        public string Name { get; } = name;
        public int Components { get; } = components;
        public float[] Values { get; } = values;
    }

    private sealed class TextureEntry(string name, ScreenTexture texture)
    {
        public string Name { get; } = name;
        public ScreenTexture Texture { get; } = texture;
    }

    public ScreenEffect Uniform(string name, params float[] values)
    {
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("uniform 名不能为空", nameof(name));
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length == 0) throw new ArgumentException("uniform 值不能为空", nameof(values));
        var components = values.Length <= 4 ? values.Length : 4;
        if (values.Length > 4 && values.Length % 4 != 0)
            throw new ArgumentException($"uniform '{name}'：数组长度必须是 4 的倍数（{values.Length}）", nameof(values));
        _uniforms.Add(new UniformEntry(name, components, values));
        return this;
    }

    public ScreenEffect Uniform(string name, Vector2 value) => Uniform(name, value.x, value.y);

    public ScreenEffect Uniform(string name, Vector3 value) => Uniform(name, value.x, value.y, value.z);

    public ScreenEffect Uniform(string name, Vector4 value) => Uniform(name, value.x, value.y, value.z, value.w);

    public ScreenEffect Uniform(string name, Color value) => Uniform(name, value.r, value.g, value.b, value.a);

    public ScreenEffect Texture(string name, ScreenTexture texture)
    {
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("绑定名不能为空", nameof(name));
        ArgumentNullException.ThrowIfNull(texture);
        _textures.Add(new TextureEntry(name, texture));
        return this;
    }

    internal int UniformBytes
    {
        get
        {
            var bytes = 0;
            foreach (var uniform in _uniforms) bytes += uniform.Values.Length * sizeof(float);
            return bytes;
        }
    }

    internal int UniformCount => _uniforms.Count;
    internal int TextureCount => _textures.Count;

    internal void Submit(int handle)
    {
        if (handle <= 0) return;
        foreach (var u in _uniforms)
            ScreenEffectsNative.SubmitAddUniform(handle, u.Name, u.Components, u.Values, u.Values.Length / u.Components);
        foreach (var t in _textures)
            ScreenEffectsNative.SubmitAddTexture(handle, t.Name, t.Texture.Id);
    }
}
