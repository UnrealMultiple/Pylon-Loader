using System.Reflection;
using System.Runtime.Loader;
using Pylon.Managed.Hosting;
using Pylon.Managed.Interop;

namespace Pylon.Managed.Mods;

/// <summary>
/// 一个 Mod 一个加载上下文；私有依赖留在它里面，宿主程序集仍共享。
/// 文件夹 Mod 按磁盘路径解析；.pmod Mod 的主程序集与托管依赖全部走内存（包读完即关），
/// 只有 native .so 与 assets 惰性落到 <see cref="PackageContent"/> 的解包缓存。
/// </summary>
internal sealed class ModLoadContext : AssemblyLoadContext
{
    private readonly string _modId;
    private readonly string? _packagePath;

    // 文件夹 Mod 用的两个字段。
    private readonly string _directory;
    private readonly AssemblyDependencyResolver? _resolver;

    // .pmod Mod：主程序集的包内路径（同目录解析托管依赖）。
    private readonly string? _packageAssemblyPath;

    /// <summary>文件夹 Mod。</summary>
    public ModLoadContext(string modId, string modDllPath)
        : base($"pylon-mod:{modId}", isCollectible: false) // 不可回收：补丁与 interop 的 native 侧都被钉住
    {
        _modId = modId;
        _directory = Path.GetDirectoryName(modDllPath) ?? ".";
        _resolver = File.Exists(Path.ChangeExtension(modDllPath, ".deps.json"))
            ? new AssemblyDependencyResolver(modDllPath)
            : null;
    }

    /// <summary>.pmod Mod：托管依赖从包里读，native 依赖走解包缓存。</summary>
    public ModLoadContext(string modId, string packageAssemblyPath, string packagePath)
        : base($"pylon-mod:{modId}", isCollectible: false)
    {
        _modId = modId;
        _packagePath = packagePath;
        _packageAssemblyPath = packageAssemblyPath;
        _directory = PackageContent.EnsureExtracted(packagePath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var name = assemblyName.Name;
        if (name is null) return null;
        if (HostAssemblies.Contains(name)) return null; // 宿主优先：Mod 不许遮蔽

        // 包 Mod：托管依赖全部内存加载（含 lib/ 与主程序集同目录的 dll）。
        if (_packagePath is not null && _packageAssemblyPath is not null)
        {
            var packageDir = Path.GetDirectoryName(_packageAssemblyPath)?.Replace('\\', '/');
            var candidates = new[]
            {
                name + ".dll",
                string.IsNullOrEmpty(packageDir) ? null : packageDir + "/" + name + ".dll",
                "lib/" + name + ".dll",
            };
            foreach (var candidate in candidates)
            {
                if (candidate is null) continue;
                try
                {
                    using var archive = PylonArchive.Open(_packagePath);
                    if (!archive.Contains(candidate)) continue;
                    using var stream = archive.OpenRead(candidate);
                    HostLog.Write($"[{_modId}] 依赖解析：{name} ← {_packagePath}!{candidate}（内存）");
                    return LoadFromStream(stream);
                }
                catch (InvalidDataException)
                {
                    return null; // 包坏了：交回默认上下文报错，运行时的报错信息更全
                }
            }
            HostLog.Write($"[{_modId}] 依赖未找到：{name}（在包内找过主程序集同目录与 lib/）");
            return null;
        }

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

        // 包 Mod 的 .so 无法内存 dlopen：惰性解到缓存目录后按路径加载。
        if (_packagePath is not null)
        {
            var soPath = PackageContent.EnsureNativeLibrary(_packagePath, unmanagedDllName);
            if (soPath is not null)
            {
                HostLog.Write($"[{_modId}] native 依赖：{unmanagedDllName} ← {soPath}（缓存）");
                return LoadUnmanagedDllFromPath(soPath);
            }
            return IntPtr.Zero;
        }

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
