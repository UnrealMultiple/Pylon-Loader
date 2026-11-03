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

    /// <summary>从 .pmod v2 包里发现：程序集从内存加载，包读完即关。</summary>
    private ModEntry(string packagePath, string packageAssembly, string fallbackId)
    {
        PackagePath = packagePath;
        AssemblyPath = packageAssembly; // 包内路径（如 "MyMod.dll"），不是磁盘路径
        Id = fallbackId;
    }

    public string Id { get; private set; }

    public string Version { get; private set; } = "0.0.0";

    /// <summary>主程序集的磁盘路径（文件夹 Mod），或包内路径（.pmod Mod）。</summary>
    public string AssemblyPath { get; }

    /// <summary>非 null 时这是一个 .pmod Mod。</summary>
    public string? PackagePath { get; }

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

    /// <summary>来源（日志与去重用）：文件夹 Mod 是目录，包 Mod 是 .pmod 路径。</summary>
    public string Source => PackagePath ?? AssemblyPath;

    /// <summary>Mod 内容根：文件夹 Mod 是目录；包 Mod 是解包缓存目录（assets/lib 惰性解到这里）。</summary>
    public string Directory =>
        PackagePath is null
            ? Path.GetDirectoryName(AssemblyPath) ?? "."
            : PackageContent.EnsureExtracted(PackagePath);

    /// <summary>读清单并加载程序集；任何一步失败都记进 <see cref="Error"/>（不抛，免得拖垮其他 Mod）。</summary>
    public static ModEntry Discover(string assemblyPath, string fallbackId)
    {
        var entry = new ModEntry(assemblyPath, fallbackId);
        entry.ReadManifestFromDirectory();

        if (entry.Error is not null) return entry;

        try
        {
            // 每个 Mod 一个加载上下文：私有依赖留在它里面，宿主程序集仍共享（见 ModLoadContext）。
            var fullPath = Path.GetFullPath(assemblyPath);
            entry.LoadContext = new ModLoadContext(entry.Id, fullPath);
            entry.Assembly = entry.LoadContext.LoadFromAssemblyPath(fullPath);
            entry.FinishDiscovery();
        }
        catch (Exception e)
        {
            entry.Error = "程序集加载/发现失败：" + HostLog.Describe(e);
        }
        return entry;
    }

    /// <summary>
    /// .pmod 发现：读 TOC → 内存加载主程序集 → 立即关包。文件句柄不跨本方法存活，
    /// 程序集字节由运行时自持（LoadFromStream 会拷贝），之后包文件不占任何内存。
    /// </summary>
    public static ModEntry DiscoverPackage(string packagePath, string fallbackId)
    {
        try
        {
            ModManifest manifest;
            string? packageAssembly;
            byte[] assemblyBytes;
            byte[]? pdbBytes;
            using (var archive = PylonArchive.Open(packagePath))
            {
                packageAssembly = FindPackageAssembly(archive, fallbackId);
                if (packageAssembly is null)
                    return PackageError(packagePath, fallbackId, "包内没有主程序集（同名 dll / 唯一 dll）");

                var manifestText = archive.ReadManifestText();
                if (manifestText is null)
                    return PackageError(packagePath, fallbackId, "包内缺少 manifest.json");
                manifest = ModManifest.Parse(manifestText, fallbackId, out var manifestError);
                if (manifestError is not null)
                    return PackageError(packagePath, fallbackId, manifestError);

                assemblyBytes = archive.Read(packageAssembly);
                var pdbName = Path.ChangeExtension(packageAssembly, ".pdb");
                pdbBytes = archive.Contains(pdbName) ? archive.Read(pdbName) : null;
            } // 包句柄到此关闭：后续只持有已经读出的字节。

            var entry = new ModEntry(packagePath, packageAssembly, manifest.Id)
            {
                Version = manifest.Version,
                Enabled = manifest.Enabled,
                Dependencies = manifest.Dependencies,
            };
            entry.Id = manifest.Id;

            entry.LoadContext = new ModLoadContext(entry.Id, packageAssembly, packagePath);
            using var assemblyStream = new MemoryStream(assemblyBytes, writable: false);
            if (pdbBytes is not null)
            {
                using var pdbStream = new MemoryStream(pdbBytes, writable: false);
                entry.Assembly = entry.LoadContext.LoadFromStream(assemblyStream, pdbStream);
            }
            else
            {
                entry.Assembly = entry.LoadContext.LoadFromStream(assemblyStream);
            }
            entry.FinishDiscovery();
            return entry;
        }
        catch (Exception e)
        {
            return PackageError(packagePath, fallbackId, "pmod 加载失败：" + HostLog.Describe(e));
        }
    }

    private static ModEntry PackageError(string packagePath, string fallbackId, string error) =>
        new(packagePath, packagePath, fallbackId) { Error = error };

    private static string? FindPackageAssembly(PylonArchive archive, string fallbackId)
    {
        var dlls = archive.EntryNames
            .Where(n => !n.Contains('/') && n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var named = dlls.FirstOrDefault(n => n.Equals(fallbackId + ".dll", StringComparison.OrdinalIgnoreCase));
        if (named is not null) return named;
        return dlls.Length == 1 ? dlls[0] : null;
    }

    private void FinishDiscovery()
    {
        ModType = ModTypeDiscovery.Find(Assembly!);
        if (ModType is null) Error = "程序集里没有 IPylonMod 的实现";
    }

    private void ReadManifestFromDirectory()
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
