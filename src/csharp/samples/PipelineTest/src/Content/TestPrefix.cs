using Pylon.ModLoader;
using Terraria;

namespace PipelineTest.Content;

/// <summary>测试前缀：伤害 ×1.5、暴击 +10、价值 ×2。</summary>
public sealed class TestPrefix : ModPrefix
{
    public static bool Applied;

    public override string DisplayName => "测试前缀";

    public override int Category => CategoryAnyWeapon;

    public override void SetStats(ref float damageMult, ref float knockbackMult, ref float useTimeMult,
        ref float scaleMult, ref float shootSpeedMult, ref float manaMult,
        ref int critBonus, ref int tagDamage, ref int armorPenetration)
    {
        damageMult = 1.5f;
        critBonus += 10;
    }

    public override void ModifyValue(ref float valueMult) => valueMult *= 2f;

    public override void Apply(Item item) => Applied = true;
}
