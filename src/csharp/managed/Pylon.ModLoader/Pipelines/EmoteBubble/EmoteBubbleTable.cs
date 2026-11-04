using Terraria.GameContent.UI;

namespace Pylon.ModLoader;

/// <summary>
/// 表情气泡侧表：编号 = <see cref="EmoteID.Count"/>（151，编译期常量）+ 注册序。
/// 表只有"编号 → 模板"，没有实例缓存：泡泡本体是游戏对象，模板按编号现取。
/// </summary>
internal static class EmoteBubbleTable
{
    /// <summary>原版表情数（151）：mod 表情编号的起点。interop 那边是静态只读，不是 const。</summary>
    internal static readonly int Base = EmoteID.Count;

    private static readonly Dictionary<string, ModEmoteBubble> ByKey = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, ModEmoteBubble?> ByName = new(StringComparer.Ordinal);

    private static ModEmoteBubble?[] _bubbles = new ModEmoteBubble?[8];
    private static int _next;

    /// <summary>已注册的 mod 表情数量。</summary>
    internal static int Count => _next;

    /// <summary>含原版在内的表长。</summary>
    internal static int Total => Base + _next;

    internal static ModEmoteBubble? Of(int type)
    {
        var index = type - Base;
        return (uint)index < (uint)_bubbles.Length ? _bubbles[index] : null;
    }

    /// <summary>按名字取编号：先 <c>Mod名/类名</c>，再裸类名（重名当没有）。</summary>
    /// <summary>这个分类里、当前解锁的 mod 表情（按注册序）。</summary>
    internal static void AppendCategory(int categoryId, Il2CppSystem.Collections.Generic.List<int> into)
    {
        for (var type = Base; type < Base + _next; type++)
        {
            if (Of(type) is not { } emote) continue;

            var categories = emote.Categories;
            var match = false;
            for (var index = 0; index < categories.Count; index++)
            {
                if (categories[index] != categoryId) continue;
                match = true;
                break;
            }

            if (!match) continue;

            bool unlocked;
            try
            {
                unlocked = emote.IsUnlocked();
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[emotebubble] {emote.Name}.IsUnlocked 异常：{Safe.Describe(e)}");
                continue;
            }

            if (unlocked) into.Add(type);
        }
    }

    internal static int Find(string? name)
    {
        if (string.IsNullOrEmpty(name)) return 0;

        if (name.IndexOf('/') >= 0) return ByKey.TryGetValue(name, out var exact) ? exact.Type : 0;
        return ByName.TryGetValue(name, out var loose) && loose is not null ? loose.Type : 0;
    }

    /// <summary>发号；幂等，每次 Mod 注册后跑一次。</summary>
    internal static void Assign()
    {
        foreach (var emote in ModContent.GetContent<ModEmoteBubble>())
        {
            if (emote.Registered) continue;

            if (_next == _bubbles.Length) Array.Resize(ref _bubbles, _bubbles.Length * 2);

            emote.Type = Base + _next;
            emote.Registered = true;
            _bubbles[_next++] = emote;

            ByKey[emote.Mod.Name + "/" + emote.Name] = emote;
            ByName[emote.Name] = ByName.ContainsKey(emote.Name) ? null : emote;
        }
    }
}
