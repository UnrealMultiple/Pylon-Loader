namespace Pylon.ModLoader;

/// <summary>
/// 自定义伤害类别。这一版是"乘法标记"：挂在物品上，武器的伤害/暴击/击退按它的乘区走。
/// 玩家侧的加成（饰品、buff 汇总）本版不做，见 docs/pipeline/content-damageclass.md。
/// </summary>
public abstract class ModDamageClass : ModType
{
    /// <summary>武器伤害乘区（1 = 不变）。</summary>
    public virtual float DamageMultiplier => 1f;

    /// <summary>武器暴击加成（0 = 不变，单位是百分点）。</summary>
    public virtual int CritBonus => 0;

    /// <summary>武器击退乘区（1 = 不变）。</summary>
    public virtual float KnockbackMultiplier => 1f;

    /// <summary>只放共享数值，不要碰游戏对象。</summary>
    public override void SetStaticDefaults() { }
}
