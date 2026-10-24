// 层：框架 / Patch —— Lang.GetTooltip 补丁
//
// Item.RebuildTooltip() 用 Lang.GetTooltip(type) 填 Item.ToolTip（TooltipLoader 读的就是它）；
// 原版对 mod 类型查不到缓存，这里对已注册的类型直接返回 applyTooltip 写入的那份。
using System;
using HarmonyLib;
using Pylon.ModLoader.Patch;
using Terraria;
using Terraria.UI;

namespace Pylon.ModLoader
{
    [GamePatch(typeof(Lang), nameof(Lang.GetTooltip), ParamCount = 1)]
    internal static class ItemTooltipPatch
    {
        [Post]
        private static void After(int __0, ref ItemTooltip __result)
        {
            if (__0 < 0) return;
            if (ModContent.Find(ContentKind.Item, __0) == null) return;
            try
            {
                var cache = Lang._itemTooltipCache;
                if (cache != null && __0 < cache.Length && cache[__0] != null)
                    __result = cache[__0];
            }
            catch { /* 空引用时保持原版结果 */ }
        }
    }
}