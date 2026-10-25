using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pylon.ModLoader.Effects
{
    /// <summary>画在哪一层：World = 世界相机之后（界面盖在上面）、Screen = 帧末（盖一切）。</summary>
    public enum ScreenEffectLayer
    {
        World = 0,
        Screen = 1,
    }

    /// <summary>一步效果：shader + 位置/参数 + uniforms/textures，加进 ScreenFrame 提交。</summary>
    public sealed class ScreenEffect
    {
        /// <summary>要跑的着色器。</summary>
        public ScreenShader Shader { get; set; }

        /// <summary>画在世界的下面还是盖一切。</summary>
        public ScreenEffectLayer Layer { get; set; } = ScreenEffectLayer.World;

        /// <summary>画到离屏目标（ScreenFrame.Target 的 id）；0 = 直接画屏幕。</summary>
        public int Dest { get; set; }

        /// <summary>shader 的 uSource；null = 不绑。可用 ScreenTexture.Screen / History / 目标纹理。</summary>
        public ScreenTexture Source { get; set; }

        /// <summary>盒子 x0, y0, x1, y1（viewport 分数，默认整屏）。</summary>
        public float[] Box { get; set; }

        /// <summary>效果中心（viewport 分数，默认 0.5, 0.5）。</summary>
        public Vector2 Center { get; set; } = new(0.5f, 0.5f);

        /// <summary>半径（高度的分数）。</summary>
        public float Radius { get; set; }

        /// <summary>强度（uIntensity）。</summary>
        public float Intensity { get; set; } = 1f;

        /// <summary>进度 0..1（uProgress）。</summary>
        public float Progress { get; set; }

        /// <summary>效果活了多久（uAge，秒）。</summary>
        public float Age { get; set; }

        /// <summary>主色（uColor）。</summary>
        public Color Color { get; set; } = Color.white;

        private readonly List<UniformEntry> _uniforms = new();
        private readonly List<TextureEntry> _textures = new();

        private sealed class UniformEntry
        {
            public string Name;
            public int Components;
            public float[] Values;
        }

        private sealed class TextureEntry
        {
            public string Name;
            public ScreenTexture Texture;
        }

        public ScreenEffect(ScreenShader shader)
        {
            Shader = shader ?? throw new ArgumentNullException(nameof(shader));
        }

        /// <summary>
        /// 设一个 uniform。1-4 个 float = vec1..vec4；超过 4 个且是 4 的倍数 = vec4 数组
        /// （如 uniform vec4 uAlgo[8]）。
        /// </summary>
        public ScreenEffect Uniform(string name, params float[] values)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("uniform 名不能为空", nameof(name));
            if (values == null || values.Length == 0) throw new ArgumentException("uniform 值不能为空", nameof(values));
            var components = values.Length <= 4 ? values.Length : 4;
            if (values.Length > 4 && values.Length % 4 != 0)
                throw new ArgumentException($"uniform '{name}'：数组长度必须是 4 的倍数（{values.Length}）", nameof(values));
            _uniforms.Add(new UniformEntry { Name = name, Components = components, Values = values });
            return this;
        }

        public ScreenEffect Uniform(string name, Vector2 value) => Uniform(name, value.x, value.y);
        public ScreenEffect Uniform(string name, Vector3 value) => Uniform(name, value.x, value.y, value.z);
        public ScreenEffect Uniform(string name, Vector4 value) => Uniform(name, value.x, value.y, value.z, value.w);
        public ScreenEffect Uniform(string name, Color value) => Uniform(name, value.r, value.g, value.b, value.a);

        /// <summary>给 shader 绑一个 sampler2D（名字对应 shader 里的 uniform sampler2D）。</summary>
        public ScreenEffect Texture(string name, ScreenTexture texture)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("绑定名不能为空", nameof(name));
            if (texture == null) throw new ArgumentNullException(nameof(texture));
            _textures.Add(new TextureEntry { Name = name, Texture = texture });
            return this;
        }

        internal void Submit(int handle)
        {
            if (handle <= 0) return;
            foreach (var u in _uniforms)
            {
                ScreenEffectsNative.SubmitAddUniform(handle, u.Name, u.Components, u.Values, u.Values.Length / u.Components);
            }
            foreach (var t in _textures)
            {
                ScreenEffectsNative.SubmitAddTexture(handle, t.Name, t.Texture.Id);
            }
        }
    }
}

