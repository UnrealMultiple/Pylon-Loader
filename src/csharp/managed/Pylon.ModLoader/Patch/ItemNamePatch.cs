using Terraria;
using Terraria.Localization;

namespace Pylon.ModLoader;

/// <summary>Lang.GetItemName / GetItemNameValue 的 mod 类型结果替换。</summary>
internal static class ItemNamePatch
{
    private static bool _installed;

    internal static void Install(IModLog logger)
    {
        if (_installed) return;
        _installed = true;

        On.Terraria.Lang.GetItemName += GetItemName;
        On.Terraria.Lang.GetItemNameValue += GetItemNameValue;

        logger.Info("[lang] 已接管 GetItemName / GetItemNameValue（mod 物品名字与介绍）");
    }

    private static string? DisplayNameOf(int type)
    {
        if (type < 0) return null;

        var unloaded = UnloadedItems.DisplayNameOf(type);
        if (unloaded is not null) return unloaded;

        if (ModContent.Find(ContentKind.Item, type) is not ModItem item) return null;

        var name = item.DisplayName;
        return string.IsNullOrEmpty(name) ? null : name;
    }

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
        catch { }

        return result;
    }

    private static string GetItemNameValue(On.Terraria.Lang.orig_GetItemNameValue orig, int type)
    {
        var name = DisplayNameOf(type);
        return name is not null ? name : orig(type);
    }
}
