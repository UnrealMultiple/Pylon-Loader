namespace Pylon.ModLoader;

/// <summary>NPC 挨的这一下（OnHit* 收到的结果）：Damage 是 NPC 实际掉的，SourceDamage 是防御前的原始伤害。</summary>
public class HitInfo
{
    /// <summary>这次攻击的伤害职业（DamageClass.Melee…；没有就是 null）。</summary>
    public DamageClass DamageType;

    /// <summary>防御前的原始伤害（至少 1）。</summary>
    public int SourceDamage;

    /// <summary>NPC 实际受到的伤害（至少 1）。</summary>
    public int Damage;

    /// <summary>是不是暴击。</summary>
    public bool Crit;

    /// <summary>受击方向（±1 左右；0 = 没有方向）。</summary>
    public int HitDirection;

    /// <summary>这次的击退值。</summary>
    public float Knockback;

    /// <summary>一击必杀（秒了，Damage 固定 1）。</summary>
    public bool InstantKill;

    /// <summary>不显示伤害数字。</summary>
    public bool HideCombatText;

    /// <summary>values 对象翻译成按名可选参数；SourceDamage/Damage 为 1、传小于 1 夹到 1，其余全默认。</summary>
    public HitInfo(DamageClass damageType = null!, int sourceDamage = 1, int damage = 1, bool crit = false,
        int hitDirection = 0, float knockback = 0f, bool instantKill = false, bool hideCombatText = false)
    {
        DamageType = damageType;
        SourceDamage = Math.Max(sourceDamage, 1);
        Damage = Math.Max(damage, 1);
        Crit = crit;
        HitDirection = hitDirection;
        Knockback = float.IsNaN(knockback) ? 0f : knockback;
        InstantKill = instantKill;
        HideCombatText = hideCombatText;
    }
}
