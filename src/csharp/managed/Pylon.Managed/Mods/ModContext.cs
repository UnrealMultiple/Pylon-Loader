using Pylon.Mod;

namespace Pylon.Managed.Mods;

/// <summary>宿主给 Mod 的上下文实现。</summary>
internal sealed class ModContext(string id, string version, string directory) : IModContext
{
    public string Id { get; } = id;

    public string Version { get; } = version;

    public string Directory { get; } = directory;

    /// <summary>日志前缀带上 Mod id：多 Mod 时一眼看出是谁在说话。</summary>
    public void Log(string message) => HostLog.Write($"[{Id}] {message}");

    public void RegisterImGuiTab(IModImGuiTab tab) => ImGuiTabRegistry.Register(tab);
}
