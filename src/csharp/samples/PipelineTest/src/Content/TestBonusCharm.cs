using Pylon.ModLoader;
using Terraria;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Contracts;

namespace PipelineTest.Content;

/// <summary>测试饰品：UpdateAccessory 往公共/远程/近战/魔法与测试职业上写加成。</summary>
public sealed class TestBonusCharm : ModItem
{
    public override void SetDefaults()
    {
        Item.width = 24;
        Item.height = 28;
        Item.accessory = true;
    }

    public override void UpdateAccessory(Player player, bool hideVisual)
    {
        var generic = player.GetDamage(DamageClass.Generic);
        generic.Additive += 0.25f;
        generic.Multiplicative *= 1.12f;
        generic.Base += 4;
        generic.Flat += 5;
        player.GetDamage(DamageClass.Ranged).Additive += 0.2f;
        player.GetCritChance(DamageClass.Melee) += 10;
        player.GetAttackSpeed(DamageClass.Ranged) += 0.15f;
        player.GetArmorPenetration(DamageClass.Magic) += 5;
        player.GetKnockback<TestDamageClass>().Additive += 1f;
        player.GetCritChance<TestDamageClass>() += 10;
        player.GetAttackSpeed<TestDamageClass>() += 0.5f;
    }
}
