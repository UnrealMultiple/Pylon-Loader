// 层：框架 / Patch —— Lang.GetItemName 补丁
//
// 原版对 type >= ItemID.Count 返回 LocalizedText.Empty，自定义物品名会空白；
// 这里在原版之后对已注册的 mod 类型直接返回 _itemNameCache[id]（applyName 已写入）。
using System;
using Terraria;
using Terraria.Localization;

namespace Pylon.ModLoader
{
    /// <summary>Lang.GetItemName 的 mod 类型结果替换。</summary>
    internal static class ItemNamePatch
    {
        private static bool _installed;

        /// <summary>注册期装一次（幂等）。</summary>
        internal static void Install(ModLogger logger)
        {
            if (_installed) return;
            _installed = true;

            On.Terraria.Lang.GetItemName += GetItemName;
        }

        /// <summary>原版之后：已注册的 mod 类型换成缓存里那份（拿不到就保持原版结果）。</summary>
        private static LocalizedText GetItemName(On.Terraria.Lang.orig_GetItemName orig, int type)
        {
            // 只对已注册的 mod 类型改（type >= 原版数量 且已注册）。
            var result = orig(type);
            if (type < 0) return result;
            if (ModContent.Find(ContentKind.Item, type) == null) return result;
            try
            {
                var cache = Lang._itemNameCache;
                if (cache != null && type < cache.Length && cache[type] != null)
                    result = cache[type];
            }
            catch { /* 空引用时保持原版结果 */ }
            return result;
        }
    }
}
