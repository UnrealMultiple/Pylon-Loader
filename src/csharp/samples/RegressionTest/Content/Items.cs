using System;
using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader;
using Terraria;
using Terraria.ID;

namespace RegressionTest.Content
{
    /// <summary>
    /// 数据物品：SaveData/LoadData（P1-1 作者数据）+ 可滚 mod 前缀 + 聊天标签 [i:] 用。
    /// 单例 ModItem：SaveData 的值全部由 Item 参数与计数派生（不存实例字段）。
    /// </summary>
    public sealed class DataItem : ModItem
    {
        public static int StampCounter;

        public override string DisplayName => "回归数据物品";

        public override void SetDefaults(Item item)
        {
            item.width = 20;
            item.height = 20;
            item.maxStack = 1;
            item.rare = 2;
            item.value = 5000;
        }

        public override void SaveData(Item item, TagCompound tag)
        {
            var stamp = ++StampCounter;
            tag.Set("stamp", stamp);
            tag.Set("note", "D" + item.stack + "x" + item.width);
            RegressionTestMod.Log?.Info($"[rt] DataItem.SaveData stamp={stamp} note=D{item.stack}x{item.width} prefix={item.prefix}");
        }

        public override void LoadData(Item item, TagCompound tag)
        {
            var stamp = tag.GetInt("stamp");
            var note = tag.GetString("note");
            RegressionTestMod.Log?.Info($"[rt] DataItem.LoadData stamp={stamp} note={note} prefix={item.prefix}");
        }
    }

    /// <summary>火焰物品：有 _Flame 贴图（P2-2）。手持要 flame=true + holdStyle（否则站立时物品不上手，火焰块不跑）。</summary>
    public sealed class FlameItem : ModItem
    {
        public override string DisplayName => "回归火焰物品";

        public override void SetDefaults(Item item)
        {
            item.width = 20;
            item.height = 20;
            item.maxStack = 1;
            item.melee = true;
            item.damage = 5;
            item.useTime = 20;
            item.useAnimation = 20;
            item.useStyle = (byte)ItemUseStyleID.Swing;
            item.holdStyle = (byte)ItemHoldStyleID.HoldFront; // 像火把一样端在手里
            item.autoReuse = false;
            item.flame = true;
        }
    }

    /// <summary>无火焰物品：没有 _Flame 贴图（P2-2 无贴图者无白块）。</summary>
    public sealed class NoFlameItem : ModItem
    {
        public override string DisplayName => "回归无火焰物品";

        public override void SetDefaults(Item item)
        {
            item.width = 20;
            item.height = 20;
            item.maxStack = 1;
            item.melee = true;
            item.damage = 5;
            item.useTime = 20;
            item.useAnimation = 20;
            item.useStyle = (byte)ItemUseStyleID.Swing;
            item.autoReuse = false;
        }
    }

    /// <summary>本 mod 的测试前缀（可滚武器，+10% 伤害）。</summary>
    public sealed class RegPrefix : ModPrefix
    {
        public override string DisplayName => "试炼之印";

        public override int Category => CategoryAnyWeapon;

        public override int RollChance(Item item) => 3;

        public override void SetStats(ref float damageMult, ref float knockbackMult, ref float useTimeMult,
                                      ref float scaleMult, ref float shootSpeedMult, ref float manaMult,
                                      ref int critBonus, ref int tagDamage, ref int armorPenetration)
        {
            damageMult *= 1.1f;
        }
    }
}
