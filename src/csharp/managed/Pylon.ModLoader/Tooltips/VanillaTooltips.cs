// 层：Mod 框架 / Tooltips —— 组出带 tModLoader 行名的 TooltipLine（等价手机版 GetLinesInfo）
// 只在 TooltipLoader 画 mod tooltip 物品时跑，原版物品仍由游戏画。
// 每行带一个 Role，颜色在画的时候按角色取：name / setBonus / research / shared / materials / price。
using System;
using System.Collections.Generic;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.Localization;
using Terraria.UI;

namespace Pylon.ModLoader.Tooltips
{
    /// <summary>行构造器：把物品的 tooltip 行组出来（原版 GetLinesInfo 的等价物）。</summary>
    internal static class VanillaTooltips
    {
        /// <summary>Build 的产出：行 + 套装颜色 + 前缀行插入位置（-1 = 没有前缀行）。</summary>
        internal sealed class BuildResult
        {
            public readonly List<TooltipLine> Lines = new();
            public Color SetBonusColor = Color.White;
            public int PrefixEnd = -1;
        }

        private static string TextValue(string key, params object[] args)
        {
            var text = Language.GetTextValue(key);
            for (var i = 0; i < args.Length; i++) text = text.Replace("{" + i + "}", Convert.ToString(args[i]));
            return text;
        }

        /// <summary>语言键的原文（Lang.tip / Lang.inter 是 LocalizedText 数组，取 .Value）。</summary>
        private static string Tip(int index) => Lang.tip[index].Value;

        /// <summary>四舍五入到偶数（银行家舍入，照原版）。</summary>
        private static int RoundEven(float x)
        {
            var floor = (int)Math.Floor(x);
            if (x - floor == 0.5f) return floor % 2 == 0 ? floor : floor + 1;
            return (int)Math.Round(x);
        }

        /// <summary>组全部 tooltip 行；oldKB = 画前扣掉手套/蘑菇修正的击退（前缀行拿"没修正的"对比）。</summary>
        public static BuildResult Build(Item item, float oldKB)
        {
            var result = new BuildResult();
            var lines = result.Lines;
            var me = Main.LocalPlayer;

            Add(lines, TooltipNames.ItemName, item.HoverName, "name");

            // 收藏，和装备之间共享的格子（loadout）。
            var isLoadoutSlot = false;
            var showShareHint = false;
            if (item.tooltipContext >= 0 && item.tooltipContext < ItemSlot.canLoadoutShareAt.Length &&
                ItemSlot.canLoadoutShareAt[item.tooltipContext])
            {
                var from = new Il2CppSystem.Nullable<int>();
                ItemSlot.GetSharedLoadoutItem(item.tooltipContext, item.tooltipSlot, out from);
                if (from.HasValue)
                {
                    Add(lines, TooltipNames.SharedFrom, TextValue("UI.ItemLoadoutSharedFrom", from.Value + 1));
                }
                else if (item.favorited) Add(lines, TooltipNames.Shared, TextValue("UI.ItemLoadoutShared"));
                else showShareHint = true;
                isLoadoutSlot = true;
            }
            else if (item.favorited)
            {
                Add(lines, TooltipNames.Favorite, Tip(56));
                Add(lines, TooltipNames.FavoriteDesc, Tip(57));
            }

            if (item.social && !item.vanity && !item.hasVanityEffects) Add(lines, TooltipNames.NoSocial, Tip(61));

            // 下面这些行都依赖玩家状态；主菜单/加载中悬停（LocalPlayer 空）只给名字和描述。
            if (me == null)
            {
                AddTooltipDescription(item, lines);
                return result;
            }

            // 武器行：伤害类型照原版分支（mod 伤害类扩展等管线定形再补）
            if (item.damage > 0 && (!item.notAmmo || item.useStyle != 0) &&
                (item.type < 71 || item.type > 74 || me.HasItem(905)))
            {
                // 伤害
                var damage = (int)(item.damage * ItemID.Sets.ToolTipDamageMultiplier[item.type]);
                if (ItemID.Sets.RapidAttackBonusDamage[item.type])
                    damage = me.ApplyRapidAttackBonus(damage, item.type, false);
                var damageText = ((int)(me.GetWeaponDamageMultiplier(item) * damage + 5e-6)).ToString();
                if (item.melee) damageText += Tip(2);
                else if (item.ranged) damageText += Tip(3);
                else if (item.magic) damageText += Tip(4);
                else if (item.summon) damageText += Tip(53);
                else damageText += Tip(55);
                Add(lines, TooltipNames.Damage, damageText);

                // 暴击
                var selectedCrit = me.inventory[me.selectedItem].crit;
                int? crit = null;
                if (item.melee) crit = me.meleeCrit - selectedCrit + item.GetVisualCritChance(me);
                else if (item.ranged) crit = me.rangedCrit - selectedCrit + item.GetVisualCritChance(me);
                else if (item.magic) crit = me.magicCrit - selectedCrit + item.GetVisualCritChance(me);
                if (crit != null) Add(lines, TooltipNames.CritChance, crit + Tip(5));

                // 速度（召唤武器只有鞭子才有）
                var speedLine = !item.summon || (item.shoot >= 0 && ProjectileID.Sets.IsAWhip[item.shoot]);
                if (item.useStyle != 0 && speedLine)
                {
                    var a = item.useAnimation;
                    var speedTip = a <= 8 ? 6 : a <= 20 ? 7 : a <= 25 ? 8 : a <= 30 ? 9
                                 : a <= 35 ? 10 : a <= 45 ? 11 : a <= 55 ? 12 : 13;
                    Add(lines, TooltipNames.Speed, Tip(speedTip));
                }

                // 击退
                var kb = item.knockBack;
                if (item.summon) kb += me.minionKB;
                if ((me.magicQuiver && item.useAmmo == AmmoID.Arrow) || item.useAmmo == AmmoID.Stake)
                    kb = (int)(kb * 1.1f);
                if (me.inventory[me.selectedItem].type == 3106 && item.type == 3106)
                    kb += kb * (1 - me.stealth);
                var kbTip = kb == 0 ? 14 : kb <= 1.5f ? 15 : kb <= 3 ? 16 : kb <= 4 ? 17
                            : kb <= 6 ? 18 : kb <= 7 ? 19 : kb <= 9 ? 20 : kb <= 11 ? 21 : 22;
                Add(lines, TooltipNames.Knockback, Tip(kbTip));
            }

            // 钓鱼
            if (item.fishingPole > 0)
            {
                Add(lines, TooltipNames.FishingPower, TextValue("GameUI.PrecentFishingPower", item.fishingPole));
                Add(lines, TooltipNames.NeedsBait, TextValue("GameUI.BaitRequired"));
            }
            if (item.bait > 0) Add(lines, TooltipNames.BaitPower, TextValue("GameUI.BaitPower", item.bait));

            // 装备
            if (!isLoadoutSlot && (item.headSlot > 0 || item.bodySlot > 0 || item.legSlot > 0 || item.accessory ||
                Main.projHook[item.shoot] || item.mountType != -1 ||
                (item.buffType > 0 && (Main.lightPet[item.buffType] || Main.vanityPet[item.buffType]))))
            {
                Add(lines, TooltipNames.Equipable,
                    (item.type == 854 || item.type == 3035) && Main.npcShop > 0 ? Tip(60) : Tip(23));
            }

            if (item.tileWand > 0) Add(lines, TooltipNames.WandConsumes, Tip(52) + Lang.GetItemNameValue(item.tileWand));
            if (item.questItem) Add(lines, TooltipNames.Quest, Lang.inter[65].Value);
            if (item.vanity) Add(lines, TooltipNames.Vanity, Tip(24));
            if (item.defense > 0) Add(lines, TooltipNames.Defense, item.defense + Tip(25));
            if (item.pick > 0) Add(lines, TooltipNames.PickPower, item.pick + Tip(26));
            if (item.axe > 0) Add(lines, TooltipNames.AxePower, item.axe * 5 + Tip(27));
            if (item.hammer > 0) Add(lines, TooltipNames.HammerPower, item.hammer + Tip(28));
            if (item.tileBoost != 0) Add(lines, TooltipNames.TileBoost, (item.tileBoost > 0 ? "+" : "") + item.tileBoost + Tip(54));

            // 回复 / 耗魔
            if (item.healLife > 0)
                Add(lines, TooltipNames.HealLife, item.type == 3001
                    ? TextValue("CommonItemTooltip.RestoresLifeRange", item.healLife, 120)
                    : TextValue("CommonItemTooltip.RestoresLife", item.healLife));
            if (item.healMana > 0) Add(lines, TooltipNames.HealMana, TextValue("CommonItemTooltip.RestoresMana", item.healMana));
            if (item.mana > 0 && ((item.type != 127 && item.type != 4347 && item.type != 4348 && item.type != 514) || !me.spaceGun))
                Add(lines, TooltipNames.UseMana, TextValue("CommonItemTooltip.UsesMana", (int)(item.mana * me.manaCost)));

            // 放置 / 弹药 / 消耗
            if (item.createWall > 0 || item.createTile > -1 || item.type == 849)
            {
                if (ItemID.Sets.PlaceTileOnAltUse[item.type] || item.consumable) Add(lines, TooltipNames.Placeable, Tip(33));
            }
            else if (item.ammo > 0 && !item.notAmmo) Add(lines, TooltipNames.Ammo, Tip(34));
            else if (item.consumable && !item.chlorophyteExtractinatorConsumable) Add(lines, TooltipNames.Consumable, Tip(35));

            if (item.material) Add(lines, TooltipNames.Material, Tip(36));

            // 电线相关（本版本没有 TileID.Sets.Wiring 的接线位表，先按 createTile 21/467 的箱子）
            if (item.createTile == 21 || item.createTile == 467) Add(lines, TooltipNames.Container, TextValue("CommonItemTooltip.Container"));
            if (item.createTile == 441 || item.createTile == 468)
                Add(lines, TooltipNames.WireTrigger, Lang.ToopltipProcessor((short)item.type, TextValue("CommonItemTooltip.WireTrigger")));

            // 描述（ModItem.Tooltip 写进 Item.ToolTip 的那几行；多行按 \n 拆）
            AddTooltipDescription(item, lines);

            if (Main.tenthAnniversaryWorld && item.type == 238) Add(lines, TooltipNames.WizardHatDuringAnniversary, TextValue("CommonItemTooltip.WizardHatDuringAnniversary"));
            if (Main.getGoodWorld && item.type == 1127) Add(lines, TooltipNames.BurningBlock, TextValue("CommonItemTooltip.BurningBlock"));

            // 世界种子的特殊行
            if (Terraria.GameContent.SpecialSeedFeatures.Mechdusa)
            {
                if (item.type == 556 || item.type == 557 || item.type == 544)
                {
                    lines.RemoveAt(lines.Count - 1);
                    Add(lines, TooltipNames.MechSummonDuringEverything, TextValue("CommonItemTooltip.MechSummonDuringEverything"));
                }
            }
            else if (item.type == 5334)
            {
                lines.RemoveRange(lines.Count - 2, 2);
                Add(lines, TooltipNames.MechdusaSummonNotDuringEverything, TextValue("CommonItemTooltip.MechdusaSummonNotDuringEverything"));
            }

            var dd2 = new[] { 3818, 3819, 3820, 3824, 3825, 3826, 3829, 3830, 3831, 3832, 3833, 3834 };
            if (Array.IndexOf(dd2, item.type) >= 0 && !me.downedDD2EventAnyDifficulty) Add(lines, TooltipNames.EtherianManaWarning, Lang.misc[104].Value);

            if (item.buffType > 0 && BuffID.Sets.IsWellFed[item.buffType] && Main.expertMode) Add(lines, TooltipNames.WellFedExpert, Lang.misc[40].Value);

            if (item.buffTime > 0)
            {
                Add(lines, TooltipNames.BuffTime, item.buffTime / 60 < 60
                    ? TextValue("CommonItemTooltip.SecondDuration", (int)Math.Round(item.buffTime / 60f))
                    : TextValue("CommonItemTooltip.MinuteDuration", (int)Math.Round(item.buffTime / 60f / 60f)));
            }

            if (item.expert) Add(lines, TooltipNames.Expert, TextValue("GameUI.Expert"));
            if (item.rare == -13) Add(lines, TooltipNames.Master, TextValue("GameUI.Master"));

            // 前缀行插在游戏行的后面：ModifyTooltips 里 mod 前缀的额外行也进这里
            if (item.prefix > 0)
            {
                PrefixLines(item, oldKB, lines);
                result.PrefixEnd = lines.Count;
            }

            // 套装
            if (item.wornArmor || item.type < ItemID.Count && ArmorSetBonuses.SetsContaining.Length > item.type)
            {
                SetBonus(item, result);
            }

            if (showShareHint && Terraria.Player.Settings.ShowLoadoutShareHint)
                Add(lines, TooltipNames.LoadoutShareHint, Language.GetItemInputTextValue((short)item.type, "UI.ItemLoadoutShareHint"), "shared");

            // 图鉴备注
            var notes = item.BestiaryNotes;
            if (notes != null && notes != " ")
            {
                foreach (var note in notes.Split('\n')) Add(lines, TooltipNames.BestiaryNotes, note);
            }

            return result;
        }

        /// <summary>前缀改了什么，跟没前缀的同型物品比。</summary>
        private static void PrefixLines(Item item, float oldKB, List<TooltipLine> lines)
        {
            var me = Main.LocalPlayer;
            Item baseItem = Main.tooltipPrefixComparisonItem;
            if (baseItem == null || baseItem.type != item.type)
            {
                baseItem = new Item();
                baseItem.SetDefaults(item.type, null);
            }

            void ModLine(string name, string value, bool bad)
            {
                var line = Add(lines, name, value);
                line.IsModifier = true;
                line.IsModifierBad = bad;
            }

            string Signed(int n, string suffix) => (n > 0 ? "+" + n : n.ToString()) + suffix;
            int Percent(float now, float was) => RoundEven((now - was) / was * 100);

            if (baseItem.damage != item.damage)
            {
                var n = Percent(item.damage, baseItem.damage);
                ModLine(TooltipNames.PrefixDamage, Signed(n, Tip(39)), n < 0);
            }
            if (baseItem.useAnimation != item.useAnimation)
            {
                var n = -Percent(item.useAnimation, baseItem.useAnimation);
                ModLine(TooltipNames.PrefixSpeed, Signed(n, Tip(40)), n < 0);
            }
            if (baseItem.crit != item.crit)
            {
                var n = item.crit - baseItem.crit;
                ModLine(TooltipNames.PrefixCritChance, Signed(n, Tip(41)), n < 0);
            }
            if (baseItem.mana != item.mana)
            {
                var n = Percent(item.mana, baseItem.mana);
                ModLine(TooltipNames.PrefixUseMana, Signed(n, Tip(42)), n > 0);
            }
            if (baseItem.scale != item.scale)
            {
                var n = Percent(item.scale, baseItem.scale);
                ModLine(TooltipNames.PrefixSize, Signed(n, Tip(43)), n < 0);
            }
            if (baseItem.shootSpeed != item.shootSpeed)
            {
                var n = Percent(item.shootSpeed, baseItem.shootSpeed);
                ModLine(TooltipNames.PrefixShootSpeed, Signed(n, Tip(44)), n < 0);
            }
            if (baseItem.knockBack != oldKB)
            {
                var n = Percent(oldKB, baseItem.knockBack);
                ModLine(TooltipNames.PrefixKnockback, Signed(n, Tip(45)), n < 0);
            }
            if (baseItem.armorPenetration != item.armorPenetration)
            {
                var n = item.armorPenetration - baseItem.armorPenetration;
                ModLine(TooltipNames.PrefixArmorPenetration, string.Format(TextValue("CommonItemTooltip.PrefixArmorPenetration"), n), n < 0);
            }
            if (baseItem.bonusTagDamage != item.bonusTagDamage)
            {
                var n = item.bonusTagDamage - baseItem.bonusTagDamage;
                ModLine(TooltipNames.PrefixTagDamage, string.Format(TextValue("CommonItemTooltip.PrefixTagDamage"), n), n < 0);
            }

            // 饰品的前缀是固定值
            var p = item.prefix;
            if (p >= 62 && p <= 65) ModLine(TooltipNames.PrefixAccDefense, "+" + (p - 61) + Tip(25), false);
            else if (p == 66) ModLine(TooltipNames.PrefixAccMaxMana, "+20 " + Tip(31), false);
            else if (p == 67 || p == 68) ModLine(TooltipNames.PrefixAccCritChance, "+" + (p == 67 ? 2 : 4) + Tip(5), false);
            else if (p >= 69 && p <= 72) ModLine(TooltipNames.PrefixAccDamage, "+" + (p - 68) + Tip(39), false);
            else if (p >= 73 && p <= 76) ModLine(TooltipNames.PrefixAccMoveSpeed, "+" + (p - 72) + Tip(46), false);
            else if (p >= 77 && p <= 80) ModLine(TooltipNames.PrefixAccMeleeSpeed, "+" + (p - 76) + Tip(47), false);
        }

        /// <summary>套装行：最近的一套，用游戏自己的 GetTooltipForWornArmor/GetTooltipForSinglePiece。</summary>
        private static void SetBonus(Item item, BuildResult result)
        {
            var me = Main.LocalPlayer;
            var context = new ArmorSetBonus.QueryContext(me);
            var query = new ArmorSetBonus.QueryResult();

            ArmorSetBonus best = null;
            foreach (var set in ArmorSetBonuses.SetsContaining[item.type])
            {
                if (best == null) best = set;
                var q = set.QueryCount(context);
                if (query.ItemsNeeded < q.ItemsNeeded ||
                    (query.ItemsNeeded == q.ItemsNeeded && query.ItemsFound < q.ItemsFound))
                {
                    query = q;
                    best = set;
                }
            }
            if (best == null) return;

            var key = TextValue(Main.ReversedUpDownArmorSetBonuses ? "Key.UP" : "Key.DOWN");
            if (item.wornArmor)
            {
                Add(result.Lines, TooltipNames.SetBonus, best.GetTooltipForWornArmor(context, query).Replace("{0}", key), "setBonus");
                result.SetBonusColor = query.Complete ? Color.LimeGreen : new Color(130, 130, 130);
            }
            else
            {
                Add(result.Lines, TooltipNames.SetBonus, best.GetTooltipForSinglePiece(item.type).Replace("{0}", key), "setBonus");
                result.SetBonusColor = new Color(130, 130, 130);
            }
        }

        /// <summary>描述行（Item.ToolTip → Tooltip0..N；局部方法，主菜单也能给）。</summary>
        private static void AddTooltipDescription(Item item, List<TooltipLine> lines)
        {
            var tooltip = item.ToolTip;
            if (tooltip == null) return;
            for (var i = 0; i < tooltip.Lines; i++)
            {
                var cursed = i == 0 && ItemID.Sets.UsesCursedByPlanteraTooltip[item.type] && !NPC.downedPlantBoss;
                Add(lines, TooltipNames.Tooltip(i), cursed ? Tip(59) : tooltip.GetLine(i));
            }
        }

        /// <summary>加一行；role 决定颜色（name/setBonus/research/shared/materials/price）。</summary>
        private static TooltipLine Add(List<TooltipLine> lines, string name, string value, string role = null)
        {
            var line = new TooltipLine("Terraria", name, value);
            if (role != null) line.Role = role;
            lines.Add(line);
            return line;
        }
    }
}
