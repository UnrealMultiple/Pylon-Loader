using Terraria;

namespace Pylon.ModLoader;

/// <summary>
/// 伤害职业管线：安装期把 Mod 职业加进职业表（引擎入口随第一个 mod 职业激活）；
/// 武器数值入口（伤害/暴击/击退/体积）上派发 ModItem/GlobalItem/ModPlayer 的修改钩。
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
        On.Terraria.Player.GetAdjustedItemScale += ItemScale;

        Log.Info("[damageclass] 已挂（武器伤害/暴击/击退/体积派发 + 类别引擎随注册激活）");
        return true;
    }

    public void Refresh() { }

    internal void Build()
    {
        var count = 0;
        foreach (var cls in ModContent.GetContent<DamageClass>())
        {
            try
            {
                cls.Type = DamageClassLoader.Add(cls);
                _ = cls.DisplayName;
                cls.SetStaticDefaults();
                count++;
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[damageclass] {cls.Name} 安装异常：{Safe.Describe(e)}");
            }
        }

        if (count > 0) Log.Info($"[damageclass] 安装：{count} 个类别");
    }

    private static int WeaponDamage(On.Terraria.Player.orig_GetWeaponDamage orig, Player self, Item sItem)
    {
        var value = orig(self, sItem);

        if (sItem is null || sItem.type <= 0 || sItem.stack <= 0) return value;

        var baseDamage = sItem.damage;
        var start = baseDamage > 0 ? new StatModifier((float)value / baseDamage, 1f) : StatModifier.Default;
        var damage = start;

        DispatchModifyWeaponDamage(sItem, self, ref damage);

        if (damage.Equals(start)) return value;

        var at = Math.Max(baseDamage, 0);
        var result = value + ((double)damage.ApplyTo(at) - start.ApplyTo(at));
        if (!double.IsFinite(result)) return value;

        var nearest = (int)Math.Round(result);
        return Math.Max(0, Math.Abs(result - nearest) < 1e-3 ? nearest : (int)result);
    }

    private static void DispatchModifyWeaponDamage(Item item, Player player, ref StatModifier damage)
    {
        if (ModContent.IsCustom(ContentKind.Item, item.type))
        {
            var mod = ItemPipeline.Instance.InstanceOf(item);
            if (mod is not null && mod.Plan.Has(HookId.ModifyWeaponDamage))
            {
                try
                {
                    mod.ModifyWeaponDamage(player, ref damage);
                }
                catch (Exception e)
                {
                    Fail(mod, HookId.ModifyWeaponDamage, e);
                }
            }
        }

        foreach (var global in GlobalItemRegistry.For(item, HookId.ModifyWeaponDamage))
        {
            try
            {
                global.ModifyWeaponDamage(item, player, ref damage);
            }
            catch (Exception e)
            {
                Fail(global, HookId.ModifyWeaponDamage, e);
            }
        }

        var mods = PlayerPipeline.Instance.ModsOf(player);
        for (var i = 0; i < mods.Length; i++)
        {
            var mod = mods[i];
            if (!mod.HookEnabled(HookId.ModifyWeaponDamage)) continue;

            try
            {
                mod.ModifyWeaponDamage(item, ref damage);
            }
            catch (Exception e)
            {
                Fail(mod, HookId.ModifyWeaponDamage, e);
            }
        }
    }

    private static int WeaponCrit(On.Terraria.Player.orig_GetWeaponCrit orig, Player self, Item sItem)
    {
        var value = orig(self, sItem);

        if (sItem is null || sItem.type <= 0 || sItem.stack <= 0) return value;

        var crit = (float)value;
        DispatchModifyWeaponCrit(sItem, self, ref crit);

        if (crit == value || !float.IsFinite(crit)) return value;
        return (int)(crit + 5E-06f);
    }

    private static void DispatchModifyWeaponCrit(Item item, Player player, ref float crit)
    {
        if (ModContent.IsCustom(ContentKind.Item, item.type))
        {
            var mod = ItemPipeline.Instance.InstanceOf(item);
            if (mod is not null && mod.Plan.Has(HookId.ModifyWeaponCrit))
            {
                try
                {
                    mod.ModifyWeaponCrit(player, ref crit);
                }
                catch (Exception e)
                {
                    Fail(mod, HookId.ModifyWeaponCrit, e);
                }
            }
        }

        foreach (var global in GlobalItemRegistry.For(item, HookId.ModifyWeaponCrit))
        {
            try
            {
                global.ModifyWeaponCrit(item, player, ref crit);
            }
            catch (Exception e)
            {
                Fail(global, HookId.ModifyWeaponCrit, e);
            }
        }

        var mods = PlayerPipeline.Instance.ModsOf(player);
        for (var i = 0; i < mods.Length; i++)
        {
            var mod = mods[i];
            if (!mod.HookEnabled(HookId.ModifyWeaponCrit)) continue;

            try
            {
                mod.ModifyWeaponCrit(item, ref crit);
            }
            catch (Exception e)
            {
                Fail(mod, HookId.ModifyWeaponCrit, e);
            }
        }
    }

    private static float WeaponKnockback(On.Terraria.Player.orig_GetWeaponKnockback orig, Player self, Item sItem, float KnockBack)
    {
        var value = orig(self, sItem, KnockBack);

        if (sItem is null || sItem.type <= 0 || sItem.stack <= 0) return value;

        var start = KnockBack != 0 ? new StatModifier(value / KnockBack, 1f) : StatModifier.Default;
        var knockback = start;

        DispatchModifyWeaponKnockback(sItem, self, ref knockback);

        if (knockback.Equals(start)) return value;

        var result = value + (knockback.ApplyTo(KnockBack) - start.ApplyTo(KnockBack));
        return float.IsFinite(result) ? Math.Max(0f, result) : value;
    }

    private static void DispatchModifyWeaponKnockback(Item item, Player player, ref StatModifier knockback)
    {
        if (ModContent.IsCustom(ContentKind.Item, item.type))
        {
            var mod = ItemPipeline.Instance.InstanceOf(item);
            if (mod is not null && mod.Plan.Has(HookId.ModifyWeaponKnockback))
            {
                try
                {
                    mod.ModifyWeaponKnockback(player, ref knockback);
                }
                catch (Exception e)
                {
                    Fail(mod, HookId.ModifyWeaponKnockback, e);
                }
            }
        }

        foreach (var global in GlobalItemRegistry.For(item, HookId.ModifyWeaponKnockback))
        {
            try
            {
                global.ModifyWeaponKnockback(item, player, ref knockback);
            }
            catch (Exception e)
            {
                Fail(global, HookId.ModifyWeaponKnockback, e);
            }
        }

        var mods = PlayerPipeline.Instance.ModsOf(player);
        for (var i = 0; i < mods.Length; i++)
        {
            var mod = mods[i];
            if (!mod.HookEnabled(HookId.ModifyWeaponKnockback)) continue;

            try
            {
                mod.ModifyWeaponKnockback(item, ref knockback);
            }
            catch (Exception e)
            {
                Fail(mod, HookId.ModifyWeaponKnockback, e);
            }
        }
    }

    private static float ItemScale(On.Terraria.Player.orig_GetAdjustedItemScale orig, Player self, Item item)
    {
        var value = orig(self, item);

        if (item is null || item.type <= 0) return value;

        var scale = value;
        DispatchModifyItemScale(item, self, ref scale);

        return float.IsFinite(scale) ? Math.Max(0f, scale) : value;
    }

    private static void DispatchModifyItemScale(Item item, Player player, ref float scale)
    {
        if (ModContent.IsCustom(ContentKind.Item, item.type))
        {
            var mod = ItemPipeline.Instance.InstanceOf(item);
            if (mod is not null && mod.Plan.Has(HookId.ModifyItemScale))
            {
                try
                {
                    mod.ModifyItemScale(player, ref scale);
                }
                catch (Exception e)
                {
                    Fail(mod, HookId.ModifyItemScale, e);
                }
            }
        }

        foreach (var global in GlobalItemRegistry.For(item, HookId.ModifyItemScale))
        {
            try
            {
                global.ModifyItemScale(item, player, ref scale);
            }
            catch (Exception e)
            {
                Fail(global, HookId.ModifyItemScale, e);
            }
        }

        var mods = PlayerPipeline.Instance.ModsOf(player);
        for (var i = 0; i < mods.Length; i++)
        {
            var mod = mods[i];
            if (!mod.HookEnabled(HookId.ModifyItemScale)) continue;

            try
            {
                mod.ModifyItemScale(item, ref scale);
            }
            catch (Exception e)
            {
                Fail(mod, HookId.ModifyItemScale, e);
            }
        }
    }

    private static void Fail(ModItem mod, HookId hook, Exception e) =>
        ContentErrors.Report($"[damageclass] {mod.FullKey}.{hook} 异常：{Safe.Describe(e)}");

    private static void Fail(GlobalItem global, HookId hook, Exception e) =>
        ContentErrors.Report($"[damageclass] {global.FullKey}.{hook} 异常：{Safe.Describe(e)}");

    private static void Fail(ModPlayer mod, HookId hook, Exception e) =>
        ContentErrors.Report($"[damageclass] {mod.Mod?.Name}/{mod.GetType().Name}.{hook} 异常：{Safe.Describe(e)}");
}
