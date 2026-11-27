using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Pylon.NaviteAPI.ImGui;
using CheatMenu;

namespace CheatMenu.Infra;

/// <summary>
/// MT2 图标库：把物品/NPC/弹幕/Buff 的 PNG 图标画进 ImGui。
/// 图标以普通文件随 Mod 分发（Mod 目录 <c>assets/&lt;子目录&gt;/&lt;前缀&gt;_&lt;id&gt;.png</c>），
/// 运行时按需读盘解码上传 GL 纹理，LRU 限容；纹理池空（返回 0）时下一帧自动重试。
/// </summary>
public static class IconStore
{
    public enum Kind { Item, Npc, Projectile, Buff }

    private const string AssetDirName = "assets";
    private const int MaxTextures = 320;
    private const int MaxUploadsPerFrame = 24;

    private sealed class Entry
    {
        public ulong Texture;
        public int Width, Height;
        public bool Failed;
        public bool Busy;
        public long LastUsed;
    }

    private static readonly Dictionary<long, Entry> Cache = new();
    private static readonly Dictionary<Kind, int> UploadsThisFrame = new();
    private static int _frame;

    // ---------------- 公共入口 ----------------

    /// <summary>画一个图标（保持纵横比，缩放到 size 高度内）。返回是否画了图；false 时调用方可以画占位。</summary>
    public static bool Draw(Kind kind, int id, Vector2 size, Vector4 tint = default)
    {
        if (!TryGet(kind, id, out var texture, out var w, out var h)) return false;

        // 纵横比适配：高度铺满，宽度按比例
        var scale = h > 0 ? size.Y / h : 1f;
        if (w * scale > size.X) scale = size.X / w;
        var drawSize = new Vector2(w * scale, h * scale);

        // 居中
        var cursor = ImGui.GetCursorPos();
        var avail = ImGui.GetContentRegionAvail();
        var padX = MathF.Max((avail.X - drawSize.X) * 0.5f, 0f);
        var padY = MathF.Max((avail.Y - drawSize.Y) * 0.5f, 0f);
        ImGui.SetCursorPos(new Vector2(cursor.X + padX, cursor.Y + padY));

        // NaviteAPI 的 ImGui.Image 只收 4 参（无 tint）：着色交给 DrawList.AddImage 的 col
        var pos = ImGui.GetCursorScreenPos();
        var col = tint == default
            ? 0xFFFFFFFFu
            : ImGui.ColorConvertFloat4ToU32(new Vector4(tint.X, tint.Y, tint.Z, tint.W));
        ImGui.GetWindowDrawList().AddImage(texture, pos, pos + drawSize,
            new Vector2(0f, 0f), new Vector2(1f, 1f), col);

        ImGui.SetCursorPos(cursor);
        ImGui.Dummy(size);
        return true;
    }

    public static bool TryGet(Kind kind, int id, out ulong texture, out int width, out int height)
    {
        texture = 0;
        width = 0;
        height = 0;
        if (id <= 0) return false;

        AdvanceFrame();

        var key = Key(kind, id);
        if (Cache.TryGetValue(key, out var entry))
        {
            entry.LastUsed = _frame;
            texture = entry.Texture;
            width = entry.Width;
            height = entry.Height;
            return entry.Texture != 0;
        }

        if (UploadsThisFrame.GetValueOrDefault(kind) >= MaxUploadsPerFrame) return false;
        UploadsThisFrame[kind] = UploadsThisFrame.GetValueOrDefault(kind) + 1;

        entry = Load(kind, id);
        if (entry.Busy)
        {
            // 解码成功但纹理池空：不缓存不记失败，下一帧重试。
            return false;
        }
        entry.LastUsed = _frame;
        Cache[key] = entry;
        Evict();

        texture = entry.Texture;
        width = entry.Width;
        height = entry.Height;
        return entry.Texture != 0;
    }

    public static void OnModUnload()
    {
        foreach (var entry in Cache.Values)
        {
            if (entry.Texture != 0) ImGui.DestroyTexture(entry.Texture);
        }
        Cache.Clear();
        Scratch.Clear();
        _available.Clear();
        _availableBuilt = false;
    }

    // ---------------- 条目可用性（网格预扫描用，只列文件名不解码） ----------------

    private static readonly Dictionary<Kind, HashSet<int>> _available = new();
    private static bool _availableBuilt;

    /// <summary>该分类下 assets 对应子目录里实际存在的 id 集合（一次性列举，之后查缓存）。</summary>
    public static IReadOnlyCollection<int> AvailableIds(Kind kind)
    {
        BuildAvailable();
        return _available.TryGetValue(kind, out var set) ? set : Array.Empty<int>();
    }

    public static bool HasEntry(Kind kind, int id)
    {
        BuildAvailable();
        return _available.TryGetValue(kind, out var set) && set.Contains(id);
    }

    private static void BuildAvailable()
    {
        if (_availableBuilt) return;
        _availableBuilt = true;

        foreach (var kind in Kinds())
        {
            var dir = DirFor(kind);
            if (dir is null || !Directory.Exists(dir)) continue;

            HashSet<int> set = null;
            foreach (var file in Directory.EnumerateFiles(dir, "*.png"))
            {
                // Item_123.png → 123
                var stem = Path.GetFileNameWithoutExtension(file);
                var underscore = stem.LastIndexOf('_');
                if (underscore <= 0) continue;
                if (!int.TryParse(stem[(underscore + 1)..], out var id) || id <= 0) continue;

                set ??= _available[kind] = new HashSet<int>();
                set.Add(id);
            }
        }
    }

    // ---------------- 内部 ----------------

    private static IEnumerable<Kind> Kinds()
    {
        yield return Kind.Item;
        yield return Kind.Npc;
        yield return Kind.Projectile;
        yield return Kind.Buff;
    }

    private static void AdvanceFrame()
    {
        if (_frame == UiFrame()) return;
        _frame = UiFrame();
        UploadsThisFrame.Clear();
    }

    private static int UiFrame()
    {
        try { return (int)ImGui.GetFrameCount(); }
        catch { return _frame; }
    }

    private static long Key(Kind kind, int id) => ((long)kind << 32) | (uint)id;

    /// <summary>淘汰到 <see cref="MaxTextures"/> 以内。</summary>
    private static void Evict()
    {
        if (Cache.Count <= MaxTextures) return;

        var cutoff = _frame - MaxTextures;
        if (cutoff > 0) RemoveOlderThan(cutoff);
        if (Cache.Count <= MaxTextures) return;

        while (Cache.Count > MaxTextures)
        {
            long oldestKey = 0;
            var oldestStamp = long.MaxValue;
            var found = false;
            foreach (var pair in Cache)
            {
                if (pair.Value.LastUsed >= oldestStamp) continue;
                oldestStamp = pair.Value.LastUsed;
                oldestKey = pair.Key;
                found = true;
            }
            if (!found) return;
            RemoveEntry(oldestKey);
        }
    }

    private static void RemoveOlderThan(long cutoff)
    {
        Scratch.Clear();
        foreach (var pair in Cache)
        {
            if (pair.Value.LastUsed < cutoff) Scratch.Add(pair.Key);
        }
        for (var i = 0; i < Scratch.Count; i++) RemoveEntry(Scratch[i]);
    }

    private static readonly List<long> Scratch = new(64);

    private static void RemoveEntry(long key)
    {
        if (!Cache.TryGetValue(key, out var entry)) return;
        if (entry.Texture != 0)
        {
            try { ImGui.DestroyTexture(entry.Texture); }
            catch { /* 纹理闸门自会兜底 */ }
        }
        Cache.Remove(key);
    }

    private static string SubDir(Kind kind) => kind switch
    {
        Kind.Item => "items",
        Kind.Npc => "npcs",
        Kind.Projectile => "projectiles",
        Kind.Buff => "buffs",
        _ => null,
    };

    private static string Prefix(Kind kind) => kind switch
    {
        Kind.Item => "Item",
        Kind.Npc => "NPC",
        Kind.Projectile => "Projectile",
        Kind.Buff => "Buff",
        _ => null,
    };

    private static string AssetRoot()
    {
        var folder = CheatMenuMod.Instance?.Folder;
        return string.IsNullOrEmpty(folder) ? null : Path.Combine(folder, AssetDirName);
    }

    /// <summary>该分类对应的磁盘目录；Mod 未就绪时返回 null。</summary>
    private static string DirFor(Kind kind)
    {
        var root = AssetRoot();
        var sub = SubDir(kind);
        return root is null || sub is null ? null : Path.Combine(root, sub);
    }

    private static string FilePath(Kind kind, int id)
    {
        var dir = DirFor(kind);
        var prefix = Prefix(kind);
        return dir is null || prefix is null ? null : Path.Combine(dir, prefix + "_" + id + ".png");
    }

    private static Entry Load(Kind kind, int id)
    {
        var entry = new Entry { Failed = true };
        var path = FilePath(kind, id);
        if (path is null || !File.Exists(path)) return entry;

        try
        {
            var rgba = ImGui.DecodeImage(File.ReadAllBytes(path), out var w, out var h);
            if (rgba is null || w <= 0 || h <= 0) return entry;

            var texture = ImGui.UploadTexture(rgba, w, h);
            if (texture == 0)
            {
                entry.Failed = false;
                entry.Busy = true;        // 纹理池空：下一帧重试
                return entry;
            }
            entry.Failed = false;
            entry.Texture = texture;
            entry.Width = w;
            entry.Height = h;
            return entry;
        }
        catch
        {
            return entry;
        }
    }
}
