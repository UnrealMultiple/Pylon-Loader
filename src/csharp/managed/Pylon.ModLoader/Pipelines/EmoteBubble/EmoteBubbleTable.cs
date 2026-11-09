using Terraria;
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

    /// <summary>这个分类里、当前解锁的 mod 表情（按注册序）。</summary>
    internal static void AppendCategory(int categoryId, Il2CppSystem.Collections.Generic.List<int> into)
    {
        EnsureLocalized();
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

    /// <summary>
    /// mod 表情也要进 <see cref="EmoteID.Search"/>。
    /// 菜单里点表情时游戏走 <c>GUIEmotesWindow.EmoteOver</c> → <c>EmoteID.Search.GetName(编号)</c>
    /// 拼聊天文本；<c>IdDictionary</c> 内部是字典索引器而不是 TryGetValue，mod 编号查不到就抛
    /// <c>KeyNotFoundException</c>。异常从钩子/游戏代码里穿过 il2cpp 边界没人接，
    /// 进程直接 SIGABRT（after "uncaught exception of type Il2CppExceptionWrapper"）。
    /// </summary>
    private static void RegisterName(ModEmoteBubble emote)
    {
        try
        {
            var search = EmoteID.Search;
            if (search is not null && !search.ContainsName(emote.Name)) search.Add(emote.Name, emote.Type);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[emotebubble] {emote.Name} 登记 EmoteID.Search 失败：{Safe.Describe(e)}");
        }

        EnsureLocalized();
    }

    private static bool _localized;

    /// <summary>
    /// 语言表要等异步加载完才能写（加载期间写入会让游戏在未就绪的语言状态上取值并中断加载），
    /// 而发号发生在注册期；所以名字挪到第一次用到时补登记。
    /// 键是 <c>"EmojiName." + 表情名</c>：聊天与悬停提示都按这个键取文本，缺了就显示原始键名。
    /// </summary>
    internal static void EnsureLocalized()
    {
        if (_localized || !Main._isAsyncLoadComplete) return;
        _localized = true;

        for (var type = Base; type < Base + _next; type++)
        {
            if (Of(type) is not { } emote) continue;

            try
            {
                var key = "EmojiName." + emote.Name;
                var text = string.IsNullOrEmpty(emote.DisplayName) ? emote.Name : emote.DisplayName;
                LocalizationKeys.Register(key, new Terraria.Localization.LocalizedText(key, text));
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[emotebubble] {emote.Name} 登记语言键失败：{Safe.Describe(e)}");
            }
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
            // 幂等只能看这张表自己：注册管线在建实例时就已经把 Registered 置 true 了。
            var key = emote.Mod.Name + "/" + emote.Name;
            if (ByKey.ContainsKey(key)) continue;

            if (_next == _bubbles.Length) Array.Resize(ref _bubbles, _bubbles.Length * 2);

            emote.Type = Base + _next;
            emote.Registered = true;
            _bubbles[_next++] = emote;

            ByKey[key] = emote;
            ByName[emote.Name] = ByName.ContainsKey(emote.Name) ? null : emote;

            RegisterName(emote);
        }
    }
}
