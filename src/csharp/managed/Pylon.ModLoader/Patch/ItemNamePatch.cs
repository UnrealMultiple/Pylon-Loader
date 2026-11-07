// 原版对 type >= ItemID.Count 返回空名字：自定义物品名会空白，物品介绍也就整块不显示。
// 这里同时接管两个入口——
//   Lang.GetItemName(type)      -> LocalizedText（缓存里那份，供按文本取的代码用）
//   Lang.GetItemNameValue(type) -> string（游戏界面真正显示的入口）
// 名字本体存在 ModItem.DisplayName 上，不依赖语言库查找（D-012：LocalizedText 的内联值在本构建读不出）。
using Terraria;
using Terraria.Localization;

namespace Pylon.ModLoader;

/// <summary>Lang.GetItemName / GetItemNameValue 的 mod 类型结果替换。</summary>
internal static class ItemNamePatch
{
    private static bool _installed;

    /// <summary>注册期装一次（幂等）。</summary>
    internal static void Install(IModLog logger)
    {
        if (_installed) return;
        _installed = true;

        On.Terraria.Lang.GetItemName += GetItemName;
        On.Terraria.Lang.GetItemNameValue += GetItemNameValue;

        logger.Info("[lang] 已接管 GetItemName / GetItemNameValue（mod 物品名字与介绍）");
    }

    /// <summary>mod 物品的显示名（找不到就 null）。</summary>
    private static string? DisplayNameOf(int type)
    {
        if (type < 0) return null;
        if (ModContent.Find(ContentKind.Item, type) is not ModItem item) return null;

        var name = item.DisplayName;
        return string.IsNullOrEmpty(name) ? null : name;
    }

    /// <summary>原版之后：已注册的 mod 类型换成缓存里那份（拿不到就保持原版结果）。</summary>
    private static LocalizedText GetItemName(On.Terraria.Lang.orig_GetItemName orig, int type)
    {
        var result = orig(type);
        if (type < 0 || ModContent.Find(ContentKind.Item, type) is null) return result;

        try
        {
            var cache = Lang._itemNameCache;
            if (cache is { } nameCache && type < nameCache.Length && nameCache[type] is not null)
                result = nameCache[type];
        }
        catch { /* 空引用时保持原版结果 */ }

        return result;
    }

    /// <summary>名字的真正显示入口：缓存里的 LocalizedText 在本构建读出来是空串，这里直接给字符串。</summary>
    private static string GetItemNameValue(On.Terraria.Lang.orig_GetItemNameValue orig, int type)
    {
        var name = DisplayNameOf(type);
        return name is not null ? name : orig(type);
    }
}
