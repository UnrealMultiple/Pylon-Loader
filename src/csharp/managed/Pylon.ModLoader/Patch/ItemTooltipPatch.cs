// 层：框架 / Patch —— Lang.GetTooltip 补丁
//
// Item.RebuildTooltip() 用 Lang.GetTooltip(type) 填 Item.ToolTip（TooltipLoader 读的就是它）；
// 原版对 mod 类型查不到缓存，这里对已注册的类型直接返回 applyTooltip 写入的那份。
using System;
using Terraria;
using Terraria.UI;

namespace Pylon.ModLoader
{
    /// <summary>Lang.GetTooltip 的 mod 类型结果替换。</summary>
    internal static class ItemTooltipPatch
    {
        private static bool _installed;

        /// <summary>注册期装一次（幂等）。</summary>
        internal static void Install(ModLogger logger)
        {
            if (_installed) return;
            _installed = true;

            On.Terraria.Lang.GetTooltip += GetTooltip;
        }

        /// <summary>原版之后：已注册的 mod 类型换成缓存里那份（拿不到就保持原版结果）。</summary>
        private static ItemTooltip GetTooltip(On.Terraria.Lang.orig_GetTooltip orig, int type)
        {
            var result = orig(type);
            if (type < 0) return result;
            if (ModContent.Find(ContentKind.Item, type) == null) return result;
            try
            {
                var cache = Lang._itemTooltipCache;
                if (cache != null && type < cache.Length && cache[type] != null)
                    result = cache[type];
            }
            catch { /* 空引用时保持原版结果 */ }
            return result;
        }
    }
}
