namespace Pylon.ModLoader.Contracts;
/// <summary>
/// 五个属性（伤害/暴击/攻速/破防/击退）从别的职业继承的比例：1 = 全继承、0 = 不继承；
/// <see cref="Full"/> 全继承、<see cref="None"/> 全不继承。
/// </summary>
public struct StatInheritanceData
{
    public static readonly StatInheritanceData Full = new(1f, 1f, 1f, 1f, 1f);

    public static readonly StatInheritanceData None = new(0f, 0f, 0f, 0f, 0f);

    public float damageInheritance;
    public float critChanceInheritance;
    public float attackSpeedInheritance;
    public float armorPenInheritance;
    public float knockbackInheritance;

    public StatInheritanceData(float damageInheritance = 0f, float critChanceInheritance = 0f,
                               float attackSpeedInheritance = 0f, float armorPenInheritance = 0f,
                               float knockbackInheritance = 0f)
    {
        this.damageInheritance = damageInheritance;
        this.critChanceInheritance = critChanceInheritance;
        this.attackSpeedInheritance = attackSpeedInheritance;
        this.armorPenInheritance = armorPenInheritance;
        this.knockbackInheritance = knockbackInheritance;
    }

    internal bool IsNone => damageInheritance == 0 && critChanceInheritance == 0 && attackSpeedInheritance == 0 &&
                            armorPenInheritance == 0 && knockbackInheritance == 0;
}
