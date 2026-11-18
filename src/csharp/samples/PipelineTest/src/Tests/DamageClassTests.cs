using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Pylon.ModLoader;
using Terraria;
using Terraria.ID;

namespace PipelineTest.Tests;

/// <summary>
/// 伤害职业：Ready 验证注册表、继承/算作缓存、物品职业解析；
/// World 验证职业加成（SetDefaultStats + UpdateAccessory）、折算、汇总值与提示行。
/// </summary>
internal static class DamageClassTests
{
    private const int Slot = 3;

    private static Item? _savedArmor;
    private static float _knockback0, _damageGap0;
    private static int _critGap0;

    private static Content.TestDamageClass Class => ModContent.GetInstance<Content.TestDamageClass>();

    private static Item Make(int type)
    {
        var item = new Item();
        item.SetDefaults(type, null);
        return item;
    }

    public static void Register()
    {
        PT.Group("damageclass", Phase.Ready, RunReady);
        PT.Group("damageclass-w", Phase.World, RunWorld);
    }

    private static void RunReady()
    {
        PT.Case("DMGCLS-R1", c =>
        {
            DamageClass[] vanilla = { DamageClass.Default, DamageClass.Generic, DamageClass.Melee, DamageClass.MeleeNoSpeed, DamageClass.Ranged,
                DamageClass.Magic, DamageClass.Summon, DamageClass.SummonMeleeSpeed, DamageClass.MagicSummonHybrid, DamageClass.Throwing };
            var ordered = true;
            for (var i = 0; i < vanilla.Length; i++)
                ordered &= vanilla[i].Type == i && DamageClassLoader.GetDamageClass(i) == vanilla[i];
            c.True(ordered, "原版职业 Type 0-9 且注册表可反查");

            var cls = Class;
            c.NotNull(cls, "mod 职业实例");
            c.True(cls.Type == 10 && DamageClassLoader.DamageClassCount == 11 && DamageClassLoader.GetDamageClass(10) == cls,
                $"mod 职业注册在 10（type={cls.Type} count={DamageClassLoader.DamageClassCount}）");
            c.Eq(Content.TestDamageClass.StaticDefaults, 1, "SetStaticDefaults 派一次");
            c.Eq(cls.DisplayName?.Value, "Test Damage Class", "名字回退按类名拆词");
        });

        PT.Case("DMGCLS-R2", c =>
        {
            var sword = Make(ModContent.ItemType<Content.TestClassSword>());
            c.True(sword.DamageType == Class && sword.melee && sword.magic && !sword.ranged && !sword.summon,
                $"SetDefaults 里的 DamageType 生效且写了职业旗（melee={sword.melee} magic={sword.magic}）");

            c.True(sword.CountsAsClass(DamageClass.Melee) && sword.CountsAsClass<Content.TestDamageClass>() &&
                   sword.CountsAsClass(DamageClass.Magic) && !sword.CountsAsClass(DamageClass.Ranged),
                "CountsAs 跟 GetEffectInheritance");
            c.True(DamageClass.MeleeNoSpeed.CountsAsClass(DamageClass.Melee) && !DamageClass.Melee.CountsAsClass(DamageClass.MeleeNoSpeed),
                "原版 CountsAs 方向");
            c.True(DamageClass.Throwing.GetsPrefixesFor(DamageClass.Ranged) && DamageClass.Summon.GetsPrefixesFor(DamageClass.Magic) &&
                   !DamageClass.Ranged.GetsPrefixesFor(DamageClass.Melee),
                "GetsPrefixesFor");
        });

        PT.Case("DMGCLS-R3", c =>
        {
            var expected = new (int type, DamageClass cls)[]
            {
                (ItemID.CopperShortsword, DamageClass.Melee), (ItemID.WoodenBow, DamageClass.Ranged),
                (ItemID.WandofSparking, DamageClass.Magic), (ItemID.SlimeStaff, DamageClass.Summon),
                (ItemID.BlandWhip, DamageClass.SummonMeleeSpeed), (ItemID.DirtBlock, DamageClass.Default),
            };
            var wrong = "";
            foreach (var (type, cls) in expected)
            {
                var got = Make(type).DamageType;
                if (got != cls) wrong += $"{type}:{got.Name} ";
            }
            c.Eq(wrong, "", "原版物品的职业从旗标解析");

            var a = Make(ItemID.WoodenSword);
            var b = Make(ItemID.WoodenSword);
            a.DamageType = DamageClass.Ranged;
            c.True(a.DamageType == DamageClass.Ranged && a.ranged && !a.melee && b.DamageType == DamageClass.Melee,
                "SetDefaults 之外改 DamageType 只影响那一件");

            var proj = Main.projectile[999];
            var melee = proj.melee;
            var ranged = proj.ranged;
            var magic = proj.magic;
            proj.DamageType = Class;
            var took = proj.DamageType == Class && proj.melee && proj.magic && !proj.ranged;
            proj.DamageType = DamageClass.Default;
            proj.melee = melee;
            proj.ranged = ranged;
            proj.magic = magic;
            c.True(took, "弹幕也能挂职业");
        });
    }

    private static void RunWorld()
    {
        PT.Later("DMGCLS-W1", 120, c =>
        {
            var player = Main.player[Main.myPlayer];
            if (player is null || !player.active)
            {
                PT.SkipCase("DMGCLS-W1", "玩家还没就绪");
                return;
            }

            var sword = Make(ModContent.ItemType<Content.TestClassSword>());
            _knockback0 = player.GetWeaponKnockback(sword, 4f);
            _critGap0 = player.meleeCrit - player.rangedCrit;
            _damageGap0 = player.rangedDamage - player.magicDamage;
            _savedArmor = player.armor[Slot];
            player.armor[Slot] = Make(ModContent.ItemType<Content.TestBonusCharm>());
        });

        PT.Later("DMGCLS-W2", 240, c =>
        {
            var player = Main.player[Main.myPlayer];
            if (player is null || !player.active)
            {
                PT.SkipCase("DMGCLS-W2", "玩家还没就绪");
                return;
            }

            var sword = Make(ModContent.ItemType<Content.TestClassSword>());
            var cls = Class;

            c.Eq(player.GetCritChance(cls), 14f, "SetDefaultStats+UpdateAccessory 的暴击（4+10）");
            c.Eq(player.GetArmorPenetration<Content.TestDamageClass>(), 10f, "SetDefaultStats 的破防");

            int critGap = player.meleeCrit - player.rangedCrit;
            float damageGap = player.rangedDamage - player.magicDamage;
            c.True(critGap - _critGap0 == 10, $"折算进原版暴击差（{_critGap0}→{critGap}）");
            c.True(MathF.Abs(damageGap - (1.12f * _damageGap0 + 0.2f)) < 1e-3f, $"折算进原版伤害差（{_damageGap0}→{damageGap}）");

            c.True(MathF.Abs(player.GetTotalAttackSpeed(DamageClass.Ranged) - 1.15f) < 1e-4f &&
                   MathF.Abs(player.GetTotalAttackSpeed(cls) - 1.5f) < 1e-4f,
                $"汇总攻速 ranged={player.GetTotalAttackSpeed(DamageClass.Ranged)} class={player.GetTotalAttackSpeed(cls)}");

            StatModifier t = player.GetTotalDamage(cls);
            float expected = ((70 + t.Base) * t.Additive * t.Multiplicative + t.Flat) / 70f;
            float multiplier = player.GetWeaponDamageMultiplier(sword);
            c.True(t.Base == 4 && t.Flat == 5 && t.Additive > 1.39f && MathF.Abs(multiplier - expected) < 1e-4f,
                $"GetWeaponDamageMultiplier 用职业汇总（base={t.Base} flat={t.Flat} got={multiplier} want={expected}）");

            int commonCrit = Math.Min(player.meleeCrit, Math.Min(player.rangedCrit, player.magicCrit));
            float totalCrit = player.GetTotalCritChance(cls);
            int weaponCrit = player.GetWeaponCrit(sword);
            c.True(MathF.Abs(totalCrit - (14 + commonCrit)) < 1e-4f &&
                   weaponCrit == (int)MathF.Floor(totalCrit + 0.5f) + 6,
                $"mod 职业的武器暴击（total={totalCrit} weapon={weaponCrit}）");

            float knockback = player.GetWeaponKnockback(sword, 4f);
            c.True(MathF.Abs(knockback - 2 * _knockback0) < 1e-3f, $"职业击退翻倍（{_knockback0}→{knockback}）");

            c.Eq(player.GetWeaponArmorPenetration(sword), sword.armorPenetration + 10 + player.armorPenetration,
                "职业破防汇总");
            c.True(MathF.Abs(player.GetTotalArmorPenetration(DamageClass.Magic) - (5 + player.armorPenetration)) < 1e-4f,
                "魔法破防吃 UpdateAccessory 的 5");

            int time = player.itemTime, timeMax = player.itemTimeMax;
            player.ApplyItemTime(sword);
            int applied = player.itemTime;
            player.itemTime = time;
            player.itemTimeMax = timeMax;
            c.Eq(applied, 20, $"职业攻速决定 itemTime（{applied}）");
        });

        PT.Later("DMGCLS-W3", 250, c =>
        {
            var cls = Class;
            var sword = Make(ModContent.ItemType<Content.TestClassSword>());
            var player2 = Main.player[Main.myPlayer];
            if (player2 is null || !player2.active)
            {
                PT.SkipCase("DMGCLS-W3", "玩家还没就绪");
                return;
            }

            var totalCrit = player2.GetTotalCritChance(cls);
            var texts = new Il2CppStringArray(64);
            var modifiers = new Il2CppStructArray<bool>(64);
            var bad = new Il2CppStructArray<bool>(64);
            int yoyo = -1, research = -1, materials = -1, count = 1, setBonus = -1, shared = -1;
            var color = new Il2CppMicrosoft.Xna.Framework.Graphics.Color(255, 255, 255, 255);
            Main.MouseText_DrawItemTooltip_GetLinesInfo(sword, ref yoyo, ref research, ref materials,
                sword.knockBack, ref count, texts, modifiers, bad, ref setBonus, ref color, ref shared);

            var lines = new List<string>();
            for (var i = 0; i < count && i < texts.Length; i++) lines.Add(texts[i] ?? "");
            int crit = (int)MathF.Floor(totalCrit + 0.5f) + sword.GetVisualCritChance(player2);
            var named = false;
            foreach (var line in lines)
                if (line.Length > 0 && char.IsDigit(line[0]) && line.EndsWith(" Test Damage Class", StringComparison.Ordinal)) named = true;
            var critLine = lines.Contains(crit + Lang.tip[5].Value);
            var speed = false;
            for (var ti = 6; ti <= 13; ti++) speed |= lines.Contains(Lang.tip[ti].Value ?? "");

            c.True(named, "Damage 行带职业名");
            c.True(critLine, $"暴击行（{crit}）");
            c.True(!speed, "Speed 行被藏");

            try
            {
                if (_savedArmor is not null) player2.armor[Slot] = _savedArmor;
            }
            catch (Exception e)
            {
                PT.Info("恢复装备失败：" + e.Message);
            }
            _savedArmor = null;
        });
    }
}
