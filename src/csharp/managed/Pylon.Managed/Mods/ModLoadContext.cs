using System.Reflection;
using System.Runtime.Loader;
using Pylon.Managed.Hosting;
using Pylon.Managed.Interop;

namespace Pylon.Managed.Mods;

/// <summary>一个 Mod 一个加载上下文；私有依赖留在它里面，宿主程序集仍共享。</summary>
internal sealed class ModLoadContext(string modId, string modDllPath)
    : AssemblyLoadContext($"pylon-mod:{modId}", isCollectible: false) // 不可回收：补丁与 interop 的 native 侧都被钉住
{
    private readonly string _modId = modId;
    private readonly string _directory = Path.GetDirectoryName(modDllPath) ?? ".";

    private readonly AssemblyDependencyResolver? _resolver =
        File.Exists(Path.ChangeExtension(modDllPath, ".deps.json"))
            ? new AssemblyDependencyResolver(modDllPath)
            : null;

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var name = assemblyName.Name;
        if (name is null) return null;
        if (HostAssemblies.Contains(name)) return null; // 宿主优先：Mod 不许遮蔽

        var path = _resolver?.ResolveAssemblyToPath(assemblyName) ?? Probe(name + ".dll");
        if (path is null)
        {
            HostLog.Write($"[{_modId}] 依赖未找到：{name}（找过 {_directory} 与 lib/）");
            return null; // 交回默认上下文报错，运行时的报错信息更全
        }

        HostLog.Write($"[{_modId}] 依赖解析：{name} ← {path}");
        return LoadFromAssemblyPath(path);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        if (HostNatives.TryResolve(unmanagedDllName, out var host)) return host;

        var path = _resolver?.ResolveUnmanagedDllToPath(unmanagedDllName)
                   ?? Probe("lib" + unmanagedDllName + ".so")
                   ?? Probe(unmanagedDllName + ".so");
        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }

    /// <summary>Mod 目录 → Mod/lib/。</summary>
    private string? Probe(string fileName)
    {
        var direct = Path.Combine(_directory, fileName);
        if (File.Exists(direct)) return direct;

        var inLib = Path.Combine(_directory, "lib", fileName);
        return File.Exists(inLib) ? inLib : null;
    }
}
