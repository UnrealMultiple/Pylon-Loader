using Pylon.ModLoader.Pipeline;

namespace Pylon.ModLoader.Audio;
/// <summary>
/// 音频文件定位：Sound 与 Music 共用同一套约定，只在注册期跑，不进每帧路径。
/// 探测位置分两级：先按既有约定查 Mod 根的 Assets/ 目录（前缀可省、扩展名可省、大小写兜底），
/// 全部落空再按原路径回退 Mod 根——把既有约定放第一级保证已有 Mod 的解析结果不变，
/// 第二级让资源直接放 Mod 根的布局也能命中；两级都命中时优先 Assets/，结果不随资源摆放漂移。
/// 路径带已注册扩展名（.ogg/.wav/.mp3）时按原文件名探测、不追加扩展名，作者写了扩展名就以它为准。
/// 全局查找先按首段 mod 名把路径精确归属到那个 Mod，失配才按加载序逐 Mod 回退，
/// 避免同名文件静默命中别的 Mod；裸路径的全局回退是音效侧的既有约定，音乐单参调用沿用。
/// </summary>
internal static class AudioFiles
{
    private static readonly string[] Extensions = [".ogg", ".wav", ".mp3"];

    internal static string? In(Mod? mod, string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (mod is null || string.IsNullOrEmpty(mod.Directory)) return null;
        if (Path.IsPathRooted(path)) return File.Exists(path) ? path : null;

        var relative = Normalize(path);
        if (relative.Length == 0) return null;
        if (relative.StartsWith(mod.Name + "/", StringComparison.Ordinal))
        {
            relative = relative[(mod.Name.Length + 1)..];
            if (relative.Length == 0) return null;
        }

        var root = Path.GetFullPath(mod.Directory);
        var assets = Prefixed(relative);
        if (HasRegisteredExtension(relative))
        {
            return Probe(root, assets)
                   ?? Probe(root, assets.Replace("Assets/", "assets/", StringComparison.Ordinal))
                   ?? Probe(root, LowerDirectory(assets))
                   ?? Probe(root, relative);
        }

        return Find(root, assets)
               ?? Find(root, assets.Replace("Assets/", "assets/", StringComparison.Ordinal))
               ?? Find(root, LowerDirectory(assets))
               ?? Find(root, relative);
    }

    internal static string? Anywhere(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (Path.IsPathRooted(path)) return File.Exists(path) ? path : null;

        var relative = Normalize(path);
        if (relative.Length == 0) return null;

        var slash = relative.IndexOf('/');
        if (slash > 0 && ModQuery.TryGetMod(relative[..slash], out var owner))
        {
            var owned = In(owner, relative[(slash + 1)..]);
            if (owned is not null) return owned;
        }

        var mods = ModQuery.Mods;
        for (var i = 0; i < mods.Count; i++)
        {
            var found = In(mods[i], relative);
            if (found is not null) return found;
        }
        return null;
    }

    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');

    private static string Prefixed(string relative) =>
        relative.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ? relative : "Assets/" + relative;

    private static bool HasRegisteredExtension(string relative) =>
        Path.HasExtension(relative) &&
        Array.IndexOf(Extensions, Path.GetExtension(relative).ToLowerInvariant()) >= 0;

    private static string? Probe(string root, string relative)
    {
        var full = Path.GetFullPath(Path.Combine(root, relative));
        return full.StartsWith(root, StringComparison.Ordinal) && File.Exists(full) ? full : null;
    }

    private static string LowerDirectory(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? path.ToLowerInvariant() : path[..slash].ToLowerInvariant() + path[slash..];
    }

    private static string? Find(string root, string relative)
    {
        foreach (var extension in Extensions)
        {
            var full = Path.GetFullPath(Path.Combine(root, relative + extension));
            if (!full.StartsWith(root, StringComparison.Ordinal)) continue;
            if (File.Exists(full)) return full;
        }
        return null;
    }
}
