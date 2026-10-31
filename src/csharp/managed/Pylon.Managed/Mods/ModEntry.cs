using System.Reflection;

namespace Pylon.Managed.Mods;

/// <summary>一个待加载 Mod 的全部状态：清单、加载上下文、类型与实例。</summary>
internal sealed class ModEntry
{
    private ModEntry(string assemblyPath, string fallbackId)
    {
        AssemblyPath = assemblyPath;
        Id = fallbackId;
    }

    public string Id { get; private set; }

    public string Version { get; private set; } = "0.0.0";

    /// <summary>主程序集绝对路径。</summary>
    public string AssemblyPath { get; }

    public IReadOnlyList<string> Dependencies { get; private set; } = [];

    public bool Enabled { get; set; } = true;

    public ModLoadContext? LoadContext { get; private set; }

    public Assembly? Assembly { get; private set; }

    public Type? ModType { get; private set; }

    /// <summary>宿主持有实例：Mod 的状态就存在这里。</summary>
    public object? Instance { get; set; }

    public ModContext? Context { get; set; }

    /// <summary>发现阶段的失败原因；非 null 时该 Mod 不加载。</summary>
    public string? Error { get; set; }

    public string Directory => Path.GetDirectoryName(AssemblyPath) ?? ".";

    /// <summary>读清单并加载程序集；任何一步失败都记进 <see cref="Error"/>（不抛，免得拖垮其他 Mod）。</summary>
    public static ModEntry Discover(string assemblyPath, string fallbackId)
    {
        var entry = new ModEntry(assemblyPath, fallbackId);
        entry.ReadManifest();

        if (entry.Error is not null) return entry;

        try
        {
            // 每个 Mod 一个加载上下文：私有依赖留在它里面，宿主程序集仍共享（见 ModLoadContext）。
            var fullPath = Path.GetFullPath(assemblyPath);
            entry.LoadContext = new ModLoadContext(entry.Id, fullPath);
            entry.Assembly = entry.LoadContext.LoadFromAssemblyPath(fullPath);
            entry.ModType = ModTypeDiscovery.Find(entry.Assembly);
            if (entry.ModType is null) entry.Error = "程序集里没有 IPylonMod 的实现";
        }
        catch (Exception e)
        {
            entry.Error = "程序集加载/发现失败：" + HostLog.Describe(e);
        }
        return entry;
    }

    private void ReadManifest()
    {
        var manifestPath = Path.Combine(Directory, "manifest.json");
        if (!File.Exists(manifestPath)) return;

        var manifest = ModManifest.Read(manifestPath, Id, out var error);
        if (error is not null)
        {
            Error = error;
            return;
        }

        Id = manifest.Id;
        Version = manifest.Version;
        Enabled = manifest.Enabled;
        Dependencies = manifest.Dependencies;
    }
}
