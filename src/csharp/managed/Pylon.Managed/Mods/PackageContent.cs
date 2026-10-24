using System.Text.Json;
using Pylon.ModLoader;
using Pylon.ModLoader.Textures;

namespace Pylon.Managed.Mods;

/// <summary>
/// .pmod 包内容的全量解包缓存。
///
/// 程序集永远走内存（见 ModLoadContext）；native .so、assets 等内容文件解到缓存目录，
/// Mod 按磁盘路径/目录枚举访问。缓存键含包大小与 mtime：包一变缓存即失效重建；
/// 缓存目录可随时整删，下次整包重建。
/// </summary>
internal static class PackageContent
{
    /// <summary>缓存目录 → 包路径。PylonTexture 等按磁盘路径读文件的 API 用它做惰性解包（托管层内部用）。</summary>
    internal static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> CacheRoots = new(StringComparer.Ordinal);
    private const string ManifestName = "manifest.json";
    private const string CompleteMarker = ".complete";

    /// <summary>
    /// 把包内容全量解到缓存，返回缓存根目录（即 Mod 的 Directory）。
    /// 全量而非按需：Mod 会按目录枚举 assets、直读任意路径，懒解只覆盖 PylonTexture 一条路，
    /// 其余访问会拿到空目录/缺文件（实测"资源解不出来/不完整"的根因）。
    /// </summary>
    public static string EnsureExtracted(string packagePath)
    {
        var cacheDir = CacheDirectory(packagePath);
        if (File.Exists(Path.Combine(cacheDir, CompleteMarker)))
            return cacheDir;

        Directory.CreateDirectory(cacheDir);
        using var archive = PylonArchive.Open(packagePath);
        foreach (var entryPath in archive.EntryNames)
        {
            var target = Path.Combine(cacheDir, entryPath.Replace('/', Path.DirectorySeparatorChar));
            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            File.WriteAllBytes(target, archive.Read(entryPath));
        }
        // 标记最后写：解包中途被杀时下次进来整包重解，不会留下半成品。
        File.WriteAllText(Path.Combine(cacheDir, CompleteMarker), packagePath);
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
