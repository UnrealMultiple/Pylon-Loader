using System.IO;
using System.Text;

namespace Pylon.ModLoader.Effects;

/// <summary>一个屏幕效果片元着色器。同名字同源码复用同一 id。</summary>
public sealed class ScreenShader
{
    // 文件着色器要能被 Reload，不能被 GC 回收。
    private static readonly HashSet<ScreenShader> FileShaders = [];

    private string _source;

    /// <summary>着色器名（也是日志里的名字）。</summary>
    public string Name { get; }

    /// <summary>true = 硬件几何路径的片元着色器（配 mesh 顶点着色器，不读屏幕）。</summary>
    public bool Mesh { get; }

    /// <summary>来源文件绝对路径；FromSource 创建时为 null。</summary>
    public string? Path { get; internal set; }

    /// <summary>内核注册 id（提交效果时用）。</summary>
    public int Id { get; private set; }

    /// <summary>'pending'（还没编译）/ 'ready' / 'failed'。</summary>
    public ScreenShaderStatus Status
    {
        get
        {
            ScreenEffectsNative.ShaderStatus(Id, out var state, null, 0);
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
        : this(name, source, false) { }

    /// <summary>GLSL 源码构造；mesh=true 时走硬件几何路径（不读屏幕、不做屏幕拷贝）。</summary>
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

    /// <summary>直接给源码。</summary>
    public static ScreenShader FromSource(string name, string source) => new(name, source);

    /// <summary>直接注册成硬件几何着色器。</summary>
    public static ScreenShader FromSourceMesh(string name, string source) => new(name, source, true);

    /// <summary>从 Mod 资产读文件（相对路径按 Mod 目录解析，同 PylonTexture）。</summary>
    public static ScreenShader FromFile(Mod owner, string path) => FromFileImpl(owner, path, false);

    /// <summary>从 Mod 资产读文件，注册成硬件几何着色器（mesh 顶点着色器；不读屏幕）。</summary>
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

    /// <summary>重读文件，变了就重新注册（效果下一帧切到新程序）。true = 变了。</summary>
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
