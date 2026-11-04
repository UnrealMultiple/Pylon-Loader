using Terraria;

namespace Pylon.ModLoader;

/// <summary>
/// 伤害类别管线：把物品声明的类别挂到武器的伤害 / 暴击 / 击退三个入口上。
/// 玩家侧的类别加成（饰品、buff 汇总）本版不做，见 docs/pipeline/content-damageclass.md。
/// </summary>
internal sealed class DamageClassPipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static DamageClassPipeline Instance { get; } = new();

    public string Name => "damageclass";

    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        On.Terraria.Player.GetWeaponDamage += WeaponDamage;
        On.Terraria.Player.GetWeaponCrit += WeaponCrit;
        On.Terraria.Player.GetWeaponKnockback += WeaponKnockback;

        Log.Info("[damageclass] 已挂（伤害 / 暴击 / 击退三个乘区）");
        return true;
    }

    public void Refresh() => DamageClassTable.Rebuild();

    /// <summary>安装期一次：收表 + 静态默认值。</summary>
    internal void Build()
    {
        DamageClassTable.Rebuild();

        foreach (var cls in ModContent.GetContent<ModDamageClass>()) cls.SetStaticDefaults();

        if (DamageClassTable.Count > 0) Log.Info($"[damageclass] 安装：{DamageClassTable.Count} 件武器声明了类别");
    }

    // ===================== 乘区 =====================

    private static int WeaponDamage(On.Terraria.Player.orig_GetWeaponDamage orig, Player self, Item sItem)
    {
        var damage = orig(self, sItem);

        if (sItem is null || DamageClassTable.Of(sItem.type) is not { } cls) return damage;

        try
        {
            var multiplier = cls.DamageMultiplier;
            if (multiplier == 1f) return damage;

            var scaled = (int)(damage * multiplier);
            return scaled < 1 ? (damage > 0 ? 1 : 0) : scaled;
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[damageclass] {cls.Name}.DamageMultiplier 异常：{Safe.Describe(e)}");
            return damage;
        }
    }

    private static int WeaponCrit(On.Terraria.Player.orig_GetWeaponCrit orig, Player self, Item sItem)
    {
        var crit = orig(self, sItem);

        if (sItem is null || DamageClassTable.Of(sItem.type) is not { } cls) return crit;

        try
        {
            return crit + cls.CritBonus;
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[damageclass] {cls.Name}.CritBonus 异常：{Safe.Describe(e)}");
            return crit;
        }
    }

    private static float WeaponKnockback(On.Terraria.Player.orig_GetWeaponKnockback orig, Player self, Item sItem, float KnockBack)
    {
        var knockback = orig(self, sItem, KnockBack);

        if (sItem is null || DamageClassTable.Of(sItem.type) is not { } cls) return knockback;

        try
        {
            return knockback * cls.KnockbackMultiplier;
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[damageclass] {cls.Name}.KnockbackMultiplier 异常：{Safe.Describe(e)}");
            return knockback;
        }
    }
}
