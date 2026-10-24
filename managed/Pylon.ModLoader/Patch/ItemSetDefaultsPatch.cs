// 层：框架 / Patch —— Item.SetDefaults 防归零
//
// 原版 SetDefaults 会把 type >= ItemID.Count 的物品归零，所以自定义物品拦在 prefix：
// 跳过原版，ResetStats + 补 type + 跑 ModItem.SetDefaults + RebuildTooltip。
// Harmony IL 约束：补丁方法体只调静态方法（实例/虚拟方法内联易炸），逻辑抽到 Run。
using System;
using HarmonyLib;
using Pylon.ModLoader.Patch;
using Terraria;

namespace Pylon.ModLoader
{
    [GamePatch(typeof(Item), nameof(Item.SetDefaults), ParamCount = 2)]
    internal static class ItemSetDefaultsPatch
    {
        /// <summary>自定义物品的最后失败原因（给 Mod 侧日志取证）。</summary>
        public static Action<string> Error = _ => { };

        [Pre]
        internal static bool Before(Item __instance, int __0, Terraria.GameContent.Items.ItemVariant __1)
        {
            if (__0 < 0) return true;
            if (ModContent.Find(ContentKind.Item, __0) == null) return true;   // 只拦已注册
            Run(__instance, __0);
            return false;
        }

        /// <summary>实际逻辑（普通静态方法，避开补丁 IL 内联限制）。</summary>
        private static void Run(Item item, int type)
        {
            try
            {
                item.ResetStats(type);
                item.type = type;
                // 实体实例：每个生成/换类型的实体都有自己的一份 ModItem。
                var mod = ItemEntities.InstanceOf(item);
                if (mod != null)
                {
                    mod.SetDefaults(item);
                    mod.PostSetDefaults(item);
                }
                item.RebuildTooltip();
            }
            catch (Exception e)
            {
                try { Error?.Invoke($"Item.SetDefaults 自定义分支异常：{e.Message}"); } catch { }
            }
        }
    }
}