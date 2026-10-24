using Pylon.NaviteAPI.Effects;

namespace Pylon.ModLoader.Effects;

public static partial class ScreenGraphics
{
    /// <summary>一个屏幕效果片元着色器。同名字同源码复用同一 id。</summary>
    public class Shader
    {
        private static readonly HashSet<Shader> FileShaders = [];

        private string _source;

        public string Name { get; }

        public bool Mesh { get; }

        public string? Path { get; private set; }

        public int Id { get; private set; }

        public ShaderStatus Status => (ShaderStatus)ScreenEffectStatus.State(Id);

        public string Error => ScreenEffectStatus.Error(Id);

        protected internal Shader(string name, string source)
            : this(name, source, false) { }

        protected internal Shader(string name, string source, bool mesh)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("着色器名不能为空", nameof(name));
            if (string.IsNullOrEmpty(source) || !source.Contains("main"))
                throw new ArgumentException($"{name}：GLSL 源码里没有 main()", nameof(source));
            Name = name;
            Mesh = mesh;
            _source = source;
            Id = mesh ? ScreenEffectsNative.ShaderRegisterMesh(Name, _source)
                      : ScreenEffectsNative.ShaderRegister(Name, _source);
        }

        /// <summary>从源码注册；名字建议带 Mod 前缀（如 <c>"MyMod/effects/Rift"</c>）。</summary>
        public static Shader FromSource(string source, string name = "shader") => new(name, source);

        /// <summary>不读屏幕的硬件几何着色器：顶点由 C# 直接提交（拖尾/三角形/环带等批量图元用）。</summary>
        public static Shader FromSourceMesh(string name, string source) => new(name, source, true);

        public static Shader FromFile(Mod mod, string path) => FromFileImpl(mod, path, false);

        public static Shader FromFileMesh(Mod mod, string path) => FromFileImpl(mod, path, true);

        private static Shader FromFileImpl(Mod mod, string path, bool mesh)
        {
            ArgumentNullException.ThrowIfNull(mod);
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("着色器路径为空", nameof(path));
            var file = HasExtension(path) ? path : path + ".frag";
            var full = Textures.PylonTexture.Resolve(mod, file);
            var source = File.ReadAllText(full);
            var shader = new Shader(FileKey(mod, file), source, mesh) { Path = full };
            lock (FileShaders) FileShaders.Add(shader);
            return shader;
        }

        public bool Reload()
        {
            var path = Path;
            if (path is null || !File.Exists(path)) return false;
            var source = File.ReadAllText(path);
            if (source == _source) return false;
            _source = source;
            Id = Mesh ? ScreenEffectsNative.ShaderRegisterMesh(Name, _source)
                      : ScreenEffectsNative.ShaderRegister(Name, _source);
            return true;
        }

        /// <summary>所有从文件加载的 shader（热重载用）。</summary>
        public static IReadOnlyList<Shader> Files
        {
            get
            {
                lock (FileShaders) return FileShaders.ToArray();
            }
        }

        internal static string FileKey(Mod mod, string path) =>
            mod.Name + "/" + path.Replace('\\', '/');

        private static bool HasExtension(string path) =>
            path.EndsWith(".frag", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".glsl", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".fs", StringComparison.OrdinalIgnoreCase);

        public override string ToString() => Name;
    }
}