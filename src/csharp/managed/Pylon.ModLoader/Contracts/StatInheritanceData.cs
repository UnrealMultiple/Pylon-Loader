namespace Pylon.ModLoader;

/// <summary>一个伤害职业从另一个职业的加成里继承多少（tModLoader 的 StatInheritanceData）：1 = 全继承、0 = 不继承，按百分比算。</summary>
public sealed class StatInheritanceData
{
    /// <summary>五个继承系数，缺省全 0（什么都不继承；JS 的对象参数在 C# 里就是可选参数）。</summary>
    public StatInheritanceData(float damageInheritance = 0, float critChanceInheritance = 0,
        float attackSpeedInheritance = 0, float armorPenInheritance = 0, float knockbackInheritance = 0)
    {
        DamageInheritance = damageInheritance;
        CritChanceInheritance = critChanceInheritance;
        AttackSpeedInheritance = attackSpeedInheritance;
        ArmorPenInheritance = armorPenInheritance;
        KnockbackInheritance = knockbackInheritance;
    }

    /// <summary>伤害加成的继承比例（1 = 全继承）。</summary>
    public float DamageInheritance;

    /// <summary>暴击率加成的继承比例（1 = 全继承）。</summary>
    public float CritChanceInheritance;

    /// <summary>攻速加成的继承比例（1 = 全继承）。</summary>
    public float AttackSpeedInheritance;

    /// <summary>护甲穿透加成的继承比例（1 = 全继承）。</summary>
    public float ArmorPenInheritance;

    /// <summary>击退加成的继承比例（1 = 全继承）。</summary>
    public float KnockbackInheritance;

    /// <summary>全继承（1、1、1、1、1）；每次读返回新实例（参考共用会被 Object.assign 改坏）。</summary>
    public static StatInheritanceData Full => new(1, 1, 1, 1, 1);

    /// <summary>什么都不继承（全 0）；每次读都返回新实例。</summary>
    public static StatInheritanceData None => new();

    /// <summary>五个系数全是 0（什么都不继承）。</summary>
    public bool IsNone =>
        DamageInheritance == 0 && CritChanceInheritance == 0 && AttackSpeedInheritance == 0 &&
        ArmorPenInheritance == 0 && KnockbackInheritance == 0;
}
