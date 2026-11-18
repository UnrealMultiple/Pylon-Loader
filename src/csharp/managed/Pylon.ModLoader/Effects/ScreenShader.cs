using Pylon.NaviteAPI.Effects;

namespace Pylon.ModLoader.Effects;

/// <summary>一个屏幕效果片元着色器。同名字同源码复用同一 id。</summary>
public sealed class ScreenShader
{
    private static readonly HashSet<ScreenShader> FileShaders = [];

    private string _source;

    public string Name { get; }

    public bool Mesh { get; }

    public string? Path { get; internal set; }

    public int Id { get; private set; }

    public ScreenShaderStatus Status => (ScreenShaderStatus)ScreenEffectStatus.State(Id);

    public string Error => ScreenEffectStatus.Error(Id);

    public ScreenShader(string name, string source)
        : this(name, source, false) { }

    public ScreenShader(string name, string source, bool mesh)
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

    public static ScreenShader FromSource(string name, string source) => new(name, source);

    public static ScreenShader FromSourceMesh(string name, string source) => new(name, source, true);

    public static ScreenShader FromFile(Mod owner, string path) => FromFileImpl(owner, path, false);

    public static ScreenShader FromFileMesh(Mod owner, string path) => FromFileImpl(owner, path, true);

    private static ScreenShader FromFileImpl(Mod owner, string path, bool mesh)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("着色器路径为空", nameof(path));
        var file = path.EndsWith(".frag", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".glsl", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".fs", StringComparison.OrdinalIgnoreCase)
            ? path
            : path + ".frag";
        var full = Textures.PylonTexture.Resolve(owner, file);
        var source = File.ReadAllText(full);
        var shader = new ScreenShader(full, source, mesh) { Path = full };
        FileShaders.Add(shader);
        return shader;
    }

    public bool Reload()
    {
        var path = Path;
        if (path is null) return false;
        var source = File.ReadAllText(path);
        if (source == _source) return false;
        _source = source;
        Id = Mesh ? ScreenEffectsNative.ShaderRegisterMesh(Name, _source)
                  : ScreenEffectsNative.ShaderRegister(Name, _source);
        return true;
    }
}
