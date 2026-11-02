using System;
using System.Collections.Generic;
using System.IO;
using Terraria;

namespace Pylon.ModLoader;

/// <summary>Mod 音乐：注册 Assets/Music 下的曲子拿槽位，给 ModSceneEffect/ModBiome/ModNPC 的 Music 用。</summary>
public static class MusicLoader
{
    /// <summary>一条 Mod 曲子：槽位、文件、Android 播放器 id 与淡入淡出状态。</summary>
    internal sealed class Track(int slot, string file)
    {
        internal int Slot { get; } = slot;
        internal string File { get; } = file;
        internal int MusicId { get; set; }   // AndroidAudio 的音乐 id（>0 才有）
        internal float Fade { get; set; }
        internal float Sent { get; set; } = -1f;
    }

    internal static readonly List<Track> Tracks = [];

    private static readonly Dictionary<string, int> Slots = new(StringComparer.Ordinal);
    private static readonly Dictionary<int, Dictionary<int, int>> MusicBoxes = [];
    private static readonly Dictionary<int, int> MusicBoxItems = [];
    private static readonly string[] Extensions = ["", ".ogg", ".wav", ".mp3"];
    private static int _base = -1;

    /// <summary>原版最后一个音乐槽之后（MusicID.Count）。</summary>
    internal static int Base
    {
        get
        {
            if (_base >= 0) return _base;
            try { _base = Terraria.ID.MusicID.Count; }
            catch { _base = 105; }
            if (_base <= 0) _base = 105;
            return _base;
        }
    }

    public static int MusicCount => Base + Tracks.Count;

    public static int GetMusicSlot(string path) => FindSlot(null, path);

    public static int GetMusicSlot(Mod mod, string path) => FindSlot(mod, path);

    public static bool MusicExists(string path) => Resolve(null, path) is not null;

    public static bool MusicExists(Mod mod, string path) => Resolve(mod, path) is not null;

    public static void AddMusicBox(Mod mod, int musicSlot, int itemType, int tileType, int tileFrameY = 0)
    {
        if (musicSlot <= 0 || tileType < 754)
            throw new ArgumentOutOfRangeException(nameof(musicSlot), "音乐盒要 Mod 曲子的槽位与 Mod tile");

        if (!MusicBoxes.TryGetValue(tileType, out var boxes))
            MusicBoxes[tileType] = boxes = [];
        boxes[tileFrameY] = musicSlot;
        if (itemType > 0) MusicBoxItems[itemType] = musicSlot;
    }

    public static bool IsMusicPlaying(int slot) =>
        TrackOf(slot) is { MusicId: > 0 } track && AndroidAudio.MusicState(track.MusicId) == 2;

    /// <summary>把一条 Mod 曲强制回到开头并从 0 淡入。用于 Boss 再次召唤时重置 BGM。</summary>
    public static void RestartMusic(int slot) => ModMusic.Restart(slot);

    /// <summary>Boss 死亡：停掉当前 Mod 曲（粉饰淡出到 0），避免死亡后 BGM 还在响。</summary>
    public static void StopMusic() => ModMusic.Stop();

    private static int FindSlot(Mod? mod, string path)
    {
        var file = Resolve(mod, path);
        if (file is null)
        {
            Safe.Once("music:" + path, $"MusicLoader: 找不到 '{path}'（Assets/Music 下的 .ogg/.wav/.mp3）", true);
            return 0;
        }
        if (Slots.TryGetValue(file, out var slot)) return slot;

        slot = Base + Tracks.Count;
        var track = new Track(slot, file) { MusicId = AndroidAudio.RegisterMusic(file) };
        Tracks.Add(track);
        Slots[file] = slot;
        AndroidAudio.PrimeMusic(track.MusicId);   // 提前 prepare，召唤时 start 即时
        ModMusic.EnsureHooks();
        return slot;
    }

    internal static Track? TrackOf(int slot)
    {
        var index = slot - Base;
        return index >= 0 && index < Tracks.Count ? Tracks[index] : null;
    }

    internal static int MusicBoxSlot(int tileType, int frameY) =>
        MusicBoxes.TryGetValue(tileType, out var boxes) &&
        boxes.TryGetValue(frameY - frameY % 36, out var slot)
            ? slot
            : 0;

    internal static int MusicBoxItemSlot(int itemType) =>
        MusicBoxItems.TryGetValue(itemType, out var slot) ? slot : 0;

    private static string? Resolve(Mod? mod, string path)
    {
        if (string.IsNullOrEmpty(path)) return null;

        var relative = path.Replace('\\', '/').TrimStart('/');
        if (!relative.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            relative = "Assets/" + relative;

        if (mod is not null)
            return FindIn(mod.Directory, relative) ??
                   FindIn(mod.Directory, relative.Replace("Assets/", "assets/")) ??
                   FindIn(mod.Directory, LowerDirectory(relative));

        foreach (var candidate in ModLoader.Mods)
        {
            var found = FindIn(candidate.Directory, relative) ??
                        FindIn(candidate.Directory, relative.Replace("Assets/", "assets/")) ??
                        FindIn(candidate.Directory, LowerDirectory(relative));
            if (found is not null) return found;
        }
        return null;
    }

    private static string LowerDirectory(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0
            ? path.ToLowerInvariant()
            : path[..slash].ToLowerInvariant() + path[slash..];
    }

    private static string? FindIn(string root, string relative)
    {
        if (string.IsNullOrEmpty(root)) return null;
        var fullRoot = Path.GetFullPath(root);
        foreach (var extension in Extensions)
        {
            var full = Path.GetFullPath(Path.Combine(fullRoot, relative + extension));
            // 拼出来的路径必须还在 Mod 目录里：.. 不能越界读到别人的文件。
            if (!full.StartsWith(fullRoot, StringComparison.Ordinal)) continue;
            if (File.Exists(full)) return full;
        }
        return null;
    }
}
