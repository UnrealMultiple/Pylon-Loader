using Terraria;
using Terraria.Localization;

namespace Pylon.ModLoader;

/// <summary>
/// 伤害职业：决定继承谁的属性加成（GetModifierInheritance）、算作哪个职业（GetEffectInheritance）、
/// 收哪些词缀（GetPrefixInheritance）、暴击与提示行的形状；SetDefaultStats 里写这个职业必有的加成。
/// 原版实例是 <see cref="Default"/>…<see cref="Throwing"/>，Mod 职业派生后自动注册。
/// </summary>
public abstract class DamageClass : ModType
{
    public static DamageClass Default { get; private set; } = new DefaultDamageClass();

    public static DamageClass Generic { get; private set; } = new GenericDamageClass();

    public static DamageClass Melee { get; private set; } = new MeleeDamageClass();

    public static DamageClass MeleeNoSpeed { get; private set; } = new MeleeNoSpeedDamageClass();

    public static DamageClass Ranged { get; private set; } = new RangedDamageClass();

    public static DamageClass Magic { get; private set; } = new MagicDamageClass();

    public static DamageClass Summon { get; private set; } = new SummonDamageClass();

    public static DamageClass SummonMeleeSpeed { get; private set; } = new SummonMeleeSpeedDamageClass();

    public static DamageClass MagicSummonHybrid { get; private set; } = new MagicSummonHybridDamageClass();

    public static DamageClass Throwing { get; private set; } = new ThrowingDamageClass();

    static DamageClass()
    {
        DamageClass[] vanilla = { Default, Generic, Melee, MeleeNoSpeed, Ranged, Magic, Summon, SummonMeleeSpeed, MagicSummonHybrid, Throwing };
        for (int i = 0; i < vanilla.Length; i++) vanilla[i].Type = i;
    }

    public string LocalizationCategory => "DamageClasses";

    public virtual LocalizedText DisplayName => new LocalizedText(
        $"Mods.{Mod?.Name}.{LocalizationCategory}.{Name}.DisplayName", PrettifiedName());

    public virtual StatInheritanceData GetModifierInheritance(DamageClass damageClass) =>
        damageClass == Generic ? StatInheritanceData.Full : StatInheritanceData.None;

    public virtual bool GetEffectInheritance(DamageClass damageClass) => false;

    public virtual bool GetPrefixInheritance(DamageClass damageClass) => GetEffectInheritance(damageClass);

    public virtual void SetDefaultStats(Player player) { }

    public virtual bool UseStandardCritCalcs => true;

    public virtual bool ShowStatTooltipLine(Player player, string lineName) => true;

    public bool CountsAsClass<T>() where T : DamageClass => CountsAsClass(DamageClassLoader.InstanceOf<T>());

    public bool CountsAsClass(DamageClass damageClass) => DamageClassLoader.CountsAs(this, damageClass);

    public bool GetsPrefixesFor<T>() where T : DamageClass => GetsPrefixesFor(DamageClassLoader.InstanceOf<T>());

    public bool GetsPrefixesFor(DamageClass damageClass) => this == damageClass || GetPrefixInheritance(damageClass);

    string PrettifiedName() => System.Text.RegularExpressions.Regex.Replace(Name, "([A-Z])", " $1").Trim();
}
