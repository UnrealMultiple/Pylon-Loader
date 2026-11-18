using Pylon.ModLoader.Contracts;

namespace Pylon.ModLoader.Content;
/// <summary>一个职业在一位玩家身上的当帧加成：伤害/击退修正、暴击、攻速、破防。</summary>
public class DamageClassData
{
    public StatModifier damage = StatModifier.Default;
    public float critChance;
    public float attackSpeed = 1f;
    public float armorPen;
    public StatModifier knockback = StatModifier.Default;

    internal void Reset()
    {
        damage = StatModifier.Default;
        critChance = 0f;
        attackSpeed = 1f;
        armorPen = 0f;
        knockback = StatModifier.Default;
    }

    internal void CopyFrom(DamageClassData other)
    {
        damage = other.damage;
        critChance = other.critChance;
        attackSpeed = other.attackSpeed;
        armorPen = other.armorPen;
        knockback = other.knockback;
    }
}
