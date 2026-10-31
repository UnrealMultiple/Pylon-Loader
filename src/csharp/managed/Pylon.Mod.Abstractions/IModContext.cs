namespace Pylon.Mod;

/// <summary>宿主提供给单个 Mod 的上下文。</summary>
public interface IModContext
{
    /// <summary>Mod id（manifest 里的 id，缺省为目录名）。</summary>
    string Id { get; }

    /// <summary>Mod 版本（manifest 里的 version）。</summary>
    string Version { get; }

    /// <summary>Mod 所在目录的绝对路径。</summary>
    string Directory { get; }

    /// <summary>写一行日志；宿主会自动加上 Mod 前缀。</summary>
    void Log(string message);

    /// <summary>注册一个 ImGui 菜单页，由框架的菜单统一绘制。</summary>
    void RegisterImGuiTab(IModImGuiTab tab);
}
