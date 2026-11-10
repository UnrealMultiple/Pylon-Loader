using Terraria;
using Terraria.DataStructures;

namespace Pylon.ModLoader;

/// <summary>把受伤修饰换算回原版入参；实际扣血和原版冷却仍由游戏执行。</summary>
internal static class PlayerHurtHooks
{
    private static bool _installed;
    private static readonly HookId[] Hooks =
    [
        HookId.ImmuneTo, HookId.FreeDodge, HookId.ConsumableDodge, HookId.ModifyHurt,
        HookId.OnHurt, HookId.PostHurt, HookId.CanBeHitByNPC, HookId.CanBeHitByProjectile,
        HookId.ModifyHitByNPC, HookId.ModifyHitByProjectile, HookId.OnHitByNPC,
        HookId.OnHitByProjectile, HookId.CanHitPvpWithProj
    ];

    internal static void Install()
    {
        if (_installed) return;
        _installed = true;
        On.Terraria.Player.Hurt += Hurt;
    }

    private static bool Active()
    {
        foreach (var hook in Hooks)
            if (PlayerPipeline.Instance.ModsActive(hook)) return true;
        return PlayerDamageSources.AnyActive() || ItemPvpHooks.AnyActive();
    }

    private static void Fail(ModPlayer mod, HookId hook, Exception e) =>
        ContentErrors.Report($"[playerhurt] {mod.GetType().Name}.{hook} 异常:{Safe.Describe(e)}");

    private static double Hurt(On.Terraria.Player.orig_Hurt orig, Player self, PlayerDeathReason damageSource,
                               int damage, int hitDirection, bool pvp, bool quiet, bool crit,
                               int cooldownCounter, bool dodgeable)
    {
        if (!Active()) return orig(self, damageSource, damage, hitDirection, pvp, quiet, crit, cooldownCounter, dodgeable);

        HurtModifiers modifiers;
        HurtInfo pending;
        ModPlayer[] mods;
        NPC? npc = null;
        Projectile? projectile = null;
        Player? weaponOwner = null;
        Item? weapon = null;
        var slot = cooldownCounter;
        int defense;
        float perPoint;
        try
        {
            mods = PlayerPipeline.Instance.ModsOf(self);
            var immune = false;
            foreach (var mod in mods)
            {
                if (!mod.HookEnabled(HookId.ImmuneTo)) continue;
                try { immune |= mod.ImmuneTo(self, damageSource, cooldownCounter, dodgeable); }
                catch (Exception e) { Fail(mod, HookId.ImmuneTo, e); }
            }
            if (immune) return 0;

            if (damageSource is not null)
            {
                var n = damageSource._sourceNPCIndex;
                var p = damageSource._sourceProjectileLocalIndex;
                if ((uint)n < (uint)Main.npc.Length) npc = Main.npc[n];
                if ((uint)p < (uint)Main.projectile.Length) projectile = Main.projectile[p];
            }
            var allowed = true;
            if (npc is not null)
            {
                if (!PlayerDamageSources.CanHit(npc, self, ref slot)) return 0;
                foreach (var mod in mods)
                {
                    if (!mod.HookEnabled(HookId.CanBeHitByNPC)) continue;
                    try { allowed &= mod.CanBeHitByNPC(self, npc, ref slot); }
                    catch (Exception e) { Fail(mod, HookId.CanBeHitByNPC, e); }
                }
            }
            if (projectile is not null && !PlayerDamageSources.CanHit(projectile, self, pvp)) return 0;
            if (projectile is not null && !pvp)
            {
                foreach (var mod in mods)
                {
                    if (!mod.HookEnabled(HookId.CanBeHitByProjectile)) continue;
                    try { allowed &= mod.CanBeHitByProjectile(self, projectile); }
                    catch (Exception e) { Fail(mod, HookId.CanBeHitByProjectile, e); }
                }
            }
            if (projectile is not null && pvp && (uint)projectile.owner < (uint)Main.player.Length)
            {
                var attacker = Main.player[projectile.owner];
                if (attacker is not null && PlayerPipeline.Instance.ModsActive(HookId.CanHitPvpWithProj))
                {
                    foreach (var mod in PlayerPipeline.Instance.ModsOf(attacker))
                    {
                        if (!mod.HookEnabled(HookId.CanHitPvpWithProj)) continue;
                        try { allowed &= mod.CanHitPvpWithProj(attacker, projectile, self); }
                        catch (Exception e) { Fail(mod, HookId.CanHitPvpWithProj, e); }
                    }
                }
            }
            if (!allowed) return 0;

            modifiers = new HurtModifiers(damageSource, damage, hitDirection, pvp, slot, dodgeable, quiet, crit);
            // 远端重放的伤害已经完成来源修饰，服务器转发不能再次乘同一份武器加成。
            if (pvp && (Main.netMode == 0 || self.whoAmI == Main.myPlayer) &&
                ItemPvpHooks.Source(damageSource, self.whoAmI, out var itemAttacker, out var sourceItem))
            {
                weaponOwner = itemAttacker;
                weapon = sourceItem;
                ItemPvpHooks.Modify(sourceItem, itemAttacker, self, ref modifiers);
            }
            if (npc is not null)
            {
                PlayerDamageSources.Modify(npc, self, ref modifiers);
                Modify(mods, self, npc, null, HookId.ModifyHitByNPC, ref modifiers);
            }
            if (projectile is not null)
            {
                PlayerDamageSources.Modify(projectile, self, ref modifiers);
                Modify(mods, self, null, projectile, HookId.ModifyHitByProjectile, ref modifiers);
            }
            Modify(mods, self, null, null, HookId.ModifyHurt, ref modifiers);
            if (modifiers.Cancelled) return 0;
            defense = self.statDefense;
            perPoint = defense > 0 ? (float)((100000 - Main.CalculateDamagePlayersTake(100000, defense)) / defense) : 0f;
            pending = modifiers.ToHurtInfo(modifiers.damage, defense, perPoint, 0f, self.noKnockback);
            if (pending.Dodgeable && self.whoAmI == Main.myPlayer)
            {
                if (Dodge(mods, self, pending, false)) return 0;
                if (Dodge(mods, self, pending, true)) return 0;
            }
        }
        catch (Exception e)
        {
            ContentErrors.Report("[playerhurt] 受伤准备异常:" + Safe.Describe(e));
            return orig(self, damageSource, damage, hitDirection, pvp, quiet, crit, cooldownCounter, dodgeable);
        }

        // 原版还会减一次防御；防御后修饰必须先逆算，不能把最终伤害直接作为入参。
        var rawValue = modifiers.ChangesAfterDefense
            ? Math.Ceiling(pending.Damage + defense * perPoint - 1e-6)
            : Math.Round(modifiers.SourceDamage.ApplyTo(modifiers.damage) * modifiers.IncomingDamageMultiplier);
        var raw = double.IsFinite(rawValue) ? (int)Math.Clamp(rawValue, 1d, int.MaxValue) : damage;
        var source = modifiers.DamageSource as PlayerDeathReason ?? damageSource;
        var done = orig(self, source!, raw, pending.HitDirection, modifiers.PvP, modifiers.quiet,
                        modifiers.crit, modifiers.CooldownCounter, modifiers.Dodgeable);
        if (!(done > 0)) return done;
        pending.Damage = (int)Math.Min(done, int.MaxValue);
        if (weapon is not null && weaponOwner is not null) ItemPvpHooks.After(weapon, weaponOwner, self, pending);
        if (npc is not null)
        {
            PlayerDamageSources.After(npc, self, pending);
            After(mods, self, npc, null, pending, HookId.OnHitByNPC);
        }
        if (projectile is not null)
        {
            PlayerDamageSources.After(projectile, self, pending);
            After(mods, self, null, projectile, pending, HookId.OnHitByProjectile);
        }
        After(mods, self, null, null, pending, HookId.OnHurt);
        if (!self.dead && self.statLife > 0) After(mods, self, null, null, pending, HookId.PostHurt);
        return done;
    }

    private static void Modify(ModPlayer[] mods, Player player, NPC? npc, Projectile? projectile,
                               HookId hook, ref HurtModifiers modifiers)
    {
        foreach (var mod in mods)
        {
            if (!mod.HookEnabled(hook)) continue;
            var previous = modifiers;
            try
            {
                switch (hook)
                {
                    case HookId.ModifyHitByNPC: mod.ModifyHitByNPC(player, npc!, ref modifiers); break;
                    case HookId.ModifyHitByProjectile: mod.ModifyHitByProjectile(player, projectile!, ref modifiers); break;
                    default: mod.ModifyHurt(player, ref modifiers); break;
                }
                if (modifiers is null) throw new InvalidOperationException("受伤修改器不能替换为 null");
            }
            catch (Exception e)
            {
                modifiers ??= previous;
                Fail(mod, hook, e);
            }
        }
    }

    private static bool Dodge(ModPlayer[] mods, Player player, HurtInfo info, bool consumable)
    {
        var hook = consumable ? HookId.ConsumableDodge : HookId.FreeDodge;
        var dodged = false;
        foreach (var mod in mods)
        {
            if (!mod.HookEnabled(hook)) continue;
            try { dodged |= consumable ? mod.ConsumableDodge(player, info) : mod.FreeDodge(player, info); }
            catch (Exception e) { Fail(mod, hook, e); }
        }
        return dodged;
    }

    private static void After(ModPlayer[] mods, Player player, NPC? npc, Projectile? projectile, HurtInfo info, HookId hook)
    {
        foreach (var mod in mods)
        {
            if (!mod.HookEnabled(hook)) continue;
            try
            {
                switch (hook)
                {
                    case HookId.OnHitByNPC: mod.OnHitByNPC(player, npc!, info); break;
                    case HookId.OnHitByProjectile: mod.OnHitByProjectile(player, projectile!, info); break;
                    case HookId.OnHurt: mod.OnHurt(player, info); break;
                    case HookId.PostHurt: mod.PostHurt(player, info); break;
                }
            }
            catch (Exception e) { Fail(mod, hook, e); }
        }
    }
}
