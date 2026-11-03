using System.Text.Json;

namespace Pylon.Managed.Mods;

/// <summary>
/// .pmod 包内容的惰性解包缓存。
///
/// 程序集永远走内存（见 ModLoadContext），这里只处理两件无法纯内存的事：
///   · native .so —— dlopen 需要真实文件；
///   · assets/icon 等内容文件 —— Mod API 按磁盘路径读贴图。
/// 缓存键含包大小与 mtime：包一变缓存即失效重建；缓存目录可随时整删，下次按需重建。
/// 大多数 Mod 没有 native 库、资源也少，实际落盘远小于包体。
/// </summary>
internal static class PackageContent
{
    /// <summary>缓存目录 → 包路径。PylonTexture 等按磁盘路径读文件的 API 用它做惰性解包（托管层内部用）。</summary>
    internal static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> CacheRoots = new(StringComparer.Ordinal);
    private const string ManifestName = "manifest.json";

    /// <summary>保证 manifest.json 与 icon 类小文件已在缓存里，返回缓存根目录（即 Mod 的 Directory）。</summary>
    public static string EnsureExtracted(string packagePath)
    {
        var cacheDir = CacheDirectory(packagePath);
        var manifest = Path.Combine(cacheDir, ManifestName);
        if (!File.Exists(manifest))
        {
            Directory.CreateDirectory(cacheDir);
            using var archive = PylonArchive.Open(packagePath);
            if (archive.Contains(ManifestName))
                File.WriteAllBytes(manifest, archive.Read(ManifestName));
            // 图标在多处按固定路径直读（Mod 管理页、配置页、按钮），随 manifest 一起解出。
            foreach (var icon in new[] { "icon.png", "assets/icon.png" })
            {
                if (!archive.Contains(icon)) continue;
                var target = Path.Combine(cacheDir, icon.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, archive.Read(icon));
            }
        }
        return cacheDir;
    }

    /// <summary>Mod 读贴图等按路径访问的内容文件：惰性解到缓存，返回真实磁盘路径。</summary>
    public static string EnsureContentFile(string packagePath, string relativePath)
    {
        relativePath = relativePath.Replace('\\', '/').TrimStart('/');
        var cacheDir = EnsureExtracted(packagePath);
        var target = Path.GetFullPath(Path.Combine(cacheDir, relativePath));
        if (!target.StartsWith(Path.GetFullPath(cacheDir) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new IOException($"非法的内容路径：{relativePath}");
        if (File.Exists(target)) return target;

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        using var archive = PylonArchive.Open(packagePath);
        if (!archive.Contains(relativePath)) throw new FileNotFoundException("包内没有该文件：" + relativePath);
        File.WriteAllBytes(target, archive.Read(relativePath));
        return target;
    }

    /// <summary>native .so：从包内 lib/（或根）解出，返回磁盘路径；包内没有返回 null。</summary>
    public static string? EnsureNativeLibrary(string packagePath, string unmanagedDllName)
    {
        var cacheDir = EnsureExtracted(packagePath);
        foreach (var candidate in new[] { "lib/lib" + unmanagedDllName + ".so", "lib/" + unmanagedDllName + ".so", "lib" + unmanagedDllName + ".so", unmanagedDllName + ".so" })
        {
            var inPackage = CandidateInPackage(packagePath, candidate, cacheDir);
            if (inPackage is not null) return inPackage;
        }
        return null;
    }

    private static string? CandidateInPackage(string packagePath, string candidate, string cacheDir)
    {
        try
        {
            using var archive = PylonArchive.Open(packagePath);
            if (!archive.Contains(candidate)) return null;

            var target = Path.Combine(cacheDir, candidate.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!File.Exists(target)) File.WriteAllBytes(target, archive.Read(candidate));
            return target;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>缓存目录：&lt;包目录&gt;/.pylon/cache/&lt;包名&gt;-&lt;大小&gt;-&lt;mtime&gt;/。包一变即新目录，旧的可整删。</summary>
    private static string CacheDirectory(string packagePath)
    {
        var info = new FileInfo(packagePath);
        var key = $"{Path.GetFileNameWithoutExtension(packagePath)}-{info.Length}-{info.LastWriteTimeUtc.Ticks}";
        var packageDir = Path.GetDirectoryName(Path.GetFullPath(packagePath)) ?? ".";
        var dir = Path.Combine(packageDir, ".pylon", "cache", Sanitize(key));
        CacheRoots.TryAdd(dir, Path.GetFullPath(packagePath));
        return dir;
    }

    /// <summary>删除某个包的全部缓存（包被删除时调用）。</summary>
    public static void Invalidate(string packagePath)
    {
        var info = new FileInfo(packagePath);
        var key = $"{Path.GetFileNameWithoutExtension(packagePath)}-{info.Length}-{info.LastWriteTimeUtc.Ticks}";
        var dir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(packagePath)) ?? ".", ".pylon", "cache", Sanitize(key));
        if (Directory.Exists(dir))
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (IOException) { /* 缓存删不掉不致命：键里带 mtime，不会命中旧内容 */ }
        }
    }

    private static string Sanitize(string raw)
    {
        var chars = raw.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_').ToArray();
        return new string(chars);
    }
}
