using Pylon.ModLoader;

namespace PipelineTest.Content;

/// <summary>测试伤害类别：2 倍伤害、+15 暴击、1.5 倍击退——数值刻意非常规。</summary>
public sealed class TestDamageClass : ModDamageClass
{
    public override float DamageMultiplier => 2f;
    public override int CritBonus => 15;
    public override float KnockbackMultiplier => 1.5f;
}
