using System.IO;

namespace Pylon.ModLoader;

/// <summary>
/// 音频文件定位：作者的路径相对 Mod 根、可省略 Assets/ 前缀，扩展名可省。
/// 只在注册期跑，不进每帧路径；Sound 与 Music 共用同一套约定。
/// </summary>
internal static class AudioFiles
{
    private static readonly string[] Extensions = [".ogg", ".wav", ".mp3"];

    /// <summary>在指定 Mod 里找；找不到返回 null。</summary>
    internal static string? In(Mod? mod, string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (mod is null || string.IsNullOrEmpty(mod.Directory)) return null;
        if (Path.IsPathRooted(path)) return File.Exists(path) ? path : null;

        var relative = Relative(path);
        if (relative.Length == 0) return null;

        var root = Path.GetFullPath(mod.Directory);
        return Find(root, relative)
               ?? Find(root, relative.Replace("Assets/", "assets/", StringComparison.Ordinal))
               ?? Find(root, LowerDirectory(relative));
    }

    /// <summary>按加载顺序在所有 Mod 里找；路径可写成 "ModName/..." 指定归属。</summary>
    internal static string? Anywhere(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (Path.IsPathRooted(path)) return File.Exists(path) ? path : null;

        var relative = Relative(path);
        if (relative.Length == 0) return null;

        // 显式 "ModName/..." 先按那个 Mod 找；找不到仍回落到"任意 Mod 的同名相对路径"（老写法）。
        var slash = relative.IndexOf('/');
        if (slash > 0 && ModLoader.TryGetMod(relative[..slash], out var owner))
        {
            var owned = In(owner, relative[(slash + 1)..]);
            if (owned is not null) return owned;
        }

        var mods = ModLoader.Mods;
        for (var i = 0; i < mods.Count; i++)
        {
            var found = In(mods[i], relative);
            if (found is not null) return found;
        }
        return null;
    }

    private static string Relative(string path)
    {
        var relative = path.Replace('\\', '/').TrimStart('/');
        if (relative.Length == 0) return relative;
        return relative.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ? relative : "Assets/" + relative;
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
            // 拼出来的路径必须还在 Mod 目录里：.. 不能越界读到别人的文件。
            if (!full.StartsWith(root, StringComparison.Ordinal)) continue;
            if (File.Exists(full)) return full;
        }
        return null;
    }
}
