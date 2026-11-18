using Pylon.ModLoader;
using Terraria;

namespace PipelineTest.Content;

/// <summary>测试伤害职业：算作近战和魔法、继承全属性、必带 +4 暴击与 +10 破防、藏 Speed 行。</summary>
public sealed class TestDamageClass : DamageClass
{
    public static int StaticDefaults;

    public override StatInheritanceData GetModifierInheritance(DamageClass damageClass) =>
        damageClass == Generic ? StatInheritanceData.Full : StatInheritanceData.None;

    public override bool GetEffectInheritance(DamageClass damageClass) => damageClass == Melee || damageClass == Magic;

    public override void SetStaticDefaults() => StaticDefaults++;

    public override void SetDefaultStats(Player player)
    {
        player.GetCritChance<TestDamageClass>() += 4;
        player.GetArmorPenetration<TestDamageClass>() += 10;
    }

    public override bool ShowStatTooltipLine(Player player, string lineName) => lineName != "Speed";
}
