using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Pylon.ModLoader.Effects
{
    /// <summary>着色器编译状态（与内核 ShaderState 一致）。</summary>
    public enum ScreenShaderStatus
    {
        Pending = 0,
        Ready = 1,
        Failed = -1,
    }

    /// <summary>一个屏幕效果片元着色器。同名字同源码复用同一 id。</summary>
    public sealed class ScreenShader
    {
        /// <summary>从文件创建的着色器（Reload 可用）；避免 GC 收集。</summary>
        private static readonly HashSet<ScreenShader> FileShaders = new();

        private string _source;

        /// <summary>着色器名（也是日志里的名字）。</summary>
        public string Name { get; }

        /// <summary>来源文件绝对路径；FromSource 创建时为 null。</summary>
        public string Path { get; internal set; }

        /// <summary>内核注册 id（提交效果时用）。</summary>
        public int Id { get; private set; }

        /// <summary>'pending'（还没编译）/ 'ready' / 'failed'。</summary>
        public ScreenShaderStatus Status
        {
            get
            {
                int state;
                ScreenEffectsNative.ShaderStatus(Id, out state, null, 0);
                return (ScreenShaderStatus)state;
            }
        }

        /// <summary>编译错误（带 Mod 文件行号），失败时才有内容。</summary>
        public string Error
        {
            get
            {
                var sb = new StringBuilder(2048);
                ScreenEffectsNative.ShaderStatus(Id, out _, sb, sb.Capacity);
                return sb.ToString();
            }
        }

        /// <summary>GLSL 源码构造（name 只用于日志/去重）。</summary>
        public ScreenShader(string name, string source)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("着色器名不能为空", nameof(name));
            if (string.IsNullOrEmpty(source) || !source.Contains("main"))
                throw new ArgumentException($"{name}：GLSL 源码里没有 main()", nameof(source));
            Name = name;
            Path = null;
            _source = source;
            Id = ScreenEffectsNative.ShaderRegister(Name, _source);
        }

        /// <summary>直接给源码。</summary>
        public static ScreenShader FromSource(string name, string source) => new(name, source);

        /// <summary>从 Mod 资产读文件（相对路径按 Mod 目录解析，同 PylonTexture）。</summary>
        public static ScreenShader FromFile(Mod owner, string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("着色器路径为空", nameof(path));
            var file = path.EndsWith(".frag", StringComparison.OrdinalIgnoreCase) ||
                       path.EndsWith(".glsl", StringComparison.OrdinalIgnoreCase) ||
                       path.EndsWith(".fs", StringComparison.OrdinalIgnoreCase)
                ? path
                : path + ".frag";
            var full = Pylon.ModLoader.Textures.PylonTexture.Resolve(owner, file);
            var source = File.ReadAllText(full);
            var shader = new ScreenShader(full, source) { Path = full };
            FileShaders.Add(shader);
            return shader;
        }

        /// <summary>重读文件，变了就重新注册（效果下一帧切到新程序）。true = 变了。</summary>
        public bool Reload()
        {
            if (Path == null) return false;
            var source = File.ReadAllText(Path);
            if (source == _source) return false;
            _source = source;
            Id = ScreenEffectsNative.ShaderRegister(Name, _source);
            return true;
        }
    }
}
