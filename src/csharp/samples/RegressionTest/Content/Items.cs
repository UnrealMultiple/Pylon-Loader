using System;
using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader;
using Terraria;
using Terraria.ID;

namespace RegressionTest.Content
{
    /// <summary>
    /// 数据物品：SaveData/LoadData（P1-1 作者数据）+ 可滚 mod 前缀 + 聊天标签 [i:] 用。
    /// 单例 ModItem：SaveData 的值全部由 <see cref="ModItem.Item"/> 与计数派生（不存实例字段）。
    /// </summary>
    public sealed class DataItem : ModItem
    {
        public static int StampCounter;

        public override string DisplayName => "回归数据物品";

        public override void SetDefaults()
        {
            Item.width = 20;
            Item.height = 20;
            Item.maxStack = 1;
            Item.rare = 2;
            Item.value = 5000;
        }

        public override void SaveData(TagCompound tag)
        {
            var stamp = ++StampCounter;
            tag.Set("stamp", stamp);
            tag.Set("note", "D" + Item.stack + "x" + Item.width);
            RegressionTestMod.Log?.Info($"[rt] DataItem.SaveData stamp={stamp} note=D{Item.stack}x{Item.width} prefix={Item.prefix}");
        }

        public override void LoadData(TagCompound tag)
        {
            var stamp = tag.GetInt("stamp");
            var note = tag.GetString("note");
            RegressionTestMod.Log?.Info($"[rt] DataItem.LoadData stamp={stamp} note={note} prefix={Item.prefix}");
        }
    }

    /// <summary>火焰物品：有 _Flame 贴图（P2-2）。手持要 flame=true + holdStyle（否则站立时物品不上手，火焰块不跑）。</summary>
    public sealed class FlameItem : ModItem
    {
        public override string DisplayName => "回归火焰物品";

        public override void SetDefaults()
        {
            Item.width = 20;
            Item.height = 20;
            Item.maxStack = 1;
            Item.melee = true;
            Item.damage = 5;
            Item.useTime = 20;
            Item.useAnimation = 20;
            Item.useStyle = (byte)ItemUseStyleID.Swing;
            Item.holdStyle = (byte)ItemHoldStyleID.HoldFront; // 像火把一样端在手里
            Item.autoReuse = false;
            Item.flame = true;
        }
    }

    /// <summary>无火焰物品：没有 _Flame 贴图（P2-2 无贴图者无白块）。</summary>
    public sealed class NoFlameItem : ModItem
    {
        public override string DisplayName => "回归无火焰物品";

        public override void SetDefaults()
        {
            Item.width = 20;
            Item.height = 20;
            Item.maxStack = 1;
            Item.melee = true;
            Item.damage = 5;
            Item.useTime = 20;
            Item.useAnimation = 20;
            Item.useStyle = (byte)ItemUseStyleID.Swing;
            Item.autoReuse = false;
        }
    }

    /// <summary>本 mod 的测试前缀（可滚武器，+10% 伤害）。</summary>
    public sealed class RegPrefix : ModPrefix
    {
        public override string DisplayName => "试炼之印";

        public override int Category => CategoryAnyWeapon;

        public override float RollChance(Item item) => 3f;

        public override void SetStats(ref float damageMult, ref float knockbackMult, ref float useTimeMult,
                                      ref float scaleMult, ref float shootSpeedMult, ref float manaMult,
                                      ref int critBonus, ref int tagDamage, ref int armorPenetration)
        {
            damageMult *= 1.1f;
        }
    }
}
