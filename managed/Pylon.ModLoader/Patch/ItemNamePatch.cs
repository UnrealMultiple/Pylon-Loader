// 层：框架 / Patch —— Lang.GetItemName 补丁
//
// 原版对 type >= ItemID.Count 返回 LocalizedText.Empty，自定义物品名会空白；
// 这里 postfix 对已注册的 mod 类型直接返回 _itemNameCache[id]（applyName 已写入）。
using System;
using HarmonyLib;
using Pylon.ModLoader.Patch;
using Terraria;
using Terraria.Localization;

namespace Pylon.ModLoader
{
    [GamePatch(typeof(Lang), nameof(Lang.GetItemName), ParamCount = 1)]
    internal static class ItemNamePatch
    {
        [Post]
        private static void After(int __0, ref LocalizedText __result)
        {
            // 只对已注册的 mod 类型改（type >= 原版数量 且已注册）。
            if (__0 < 0) return;
            if (ModContent.Find(ContentKind.Item, __0) == null) return;
            try
            {
                var cache = Lang._itemNameCache;
                if (cache != null && __0 < cache.Length && cache[__0] != null)
                    __result = cache[__0];
            }
            catch { /* 空引用时保持原版结果 */ }
        }
    }
}