using Il2CppMicrosoft.Xna.Framework;
using Terraria;

namespace Pylon.ModLoader;

/// <summary>在原版弹幕 PVE 窗口记录来源，受击计算统一交给 NPC 执行器。</summary>
internal static class ProjectileHitHooks
{
    private static bool _installed;
    private static readonly HookId[] SourceHooks = [HookId.ModifyHitNPC, HookId.OnHitNPC];
    private static readonly HookId[] TargetHooks = [HookId.ModifyIncomingHit, HookId.ModifyHitByProjectile, HookId.OnHitByProjectile];
    private static readonly HookId[] PlayerHooks = [HookId.ModifyHitNPC, HookId.ModifyHitNPCWithProj, HookId.OnHitNPC, HookId.OnHitNPCWithProj];
    private static readonly HookId[] PlayerModifyHooks = [HookId.ModifyHitNPC, HookId.ModifyHitNPCWithProj];
    private static readonly HookId[] PlayerAfterHooks = [HookId.OnHitNPC, HookId.OnHitNPCWithProj];
    private static PlayerPipeline Players => PlayerPipeline.Instance;
    private static ModProjectile? Own(Projectile projectile) => ProjectilePipeline.Instance.InstanceOf(projectile);
    private static ModNpc? Target(NPC npc) => NpcPipeline.Instance.InstanceOf(npc);

    internal static void Install()
    {
        if (_installed) return;
        _installed = true;
        On.Terraria.Projectile.Damage_PVE += Damage;
        On.Terraria.Player.CanNPCBeHitByPlayerOrPlayerProjectile += CanHit;
    }

    private static Player? Owner(Projectile projectile)
    {
        var owner = projectile.owner;
        var players = Main.player;
        if (projectile.npcProj || projectile.trap || (uint)owner > 255u || (uint)owner >= (uint)players.Length)
            return null;
        var player = players[owner];
        return player is { active: true } ? player : null;
    }

    private static bool WantsStrike(Projectile projectile)
    {
        if (GlobalProjectileRegistry.AnyWith(SourceHooks) || GlobalNpcRegistry.AnyWith(TargetHooks)) return true;
        var own = Own(projectile);
        foreach (var hook in SourceHooks) if (own is not null && own.Plan.Has(hook)) return true;
        foreach (var hook in PlayerHooks) if (Players.ModsActive(hook)) return true;
        // 目标在原版循环内才确定；模板位集只用来判断是否需要保留这次来源。
        foreach (var npc in ModContent.GetContent<ModNpc>())
            foreach (var hook in TargetHooks) if (npc.HookEnabled(hook)) return true;
        return false;
    }

    private sealed class Source(Projectile projectile) : INpcStrikeSource
    {
        internal nint Pointer => projectile.Pointer;
        public bool ClampReturnToLife => true;
        public bool Matches(NPC target) => true;
        public void Modify(NPC target, ref HitModifiers modifiers) => ModifyHit(projectile, target, ref modifiers);
        public void After(NPC target, HitInfo hit, int damageDone) => AfterHit(projectile, target, hit, damageDone);
    }

    private static void Damage(On.Terraria.Projectile.orig_Damage_PVE orig, Projectile self,
                               ref Rectangle hitbox, float multiplier)
    {
        if (NpcHitHooks.ExecutingSource is Source executing && executing.Pointer == self.Pointer || !WantsStrike(self))
        {
            orig(self, ref hitbox, multiplier);
            return;
        }
        using var source = NpcHitHooks.BeginSource(new Source(self));
        orig(self, ref hitbox, multiplier);
    }

    private static bool CanHit(On.Terraria.Player.orig_CanNPCBeHitByPlayerOrPlayerProjectile orig,
                               Player self, NPC npc, Projectile projectile)
    {
        if (projectile is null) return orig(self, npc, projectile!);
        var verdict = Decision(projectile, npc);
        return verdict ?? orig(self, npc, projectile);
    }

    private static bool? Decision(Projectile projectile, NPC npc)
    {
        bool? result = null;
        if (Owner(projectile) is { } owner)
        {
            if (Players.ModsActive(HookId.CanHitNPC))
            {
                var allowed = true;
                foreach (var mod in Players.ModsOf(owner))
                {
                    if (!mod.HookEnabled(HookId.CanHitNPC)) continue;
                    try { allowed &= mod.CanHitNPC(owner, npc); }
                    catch (Exception e) { Fail(mod, HookId.CanHitNPC, e); }
                }
                if (!allowed) return false;
            }
            if (Players.ModsActive(HookId.CanHitNPCWithProj))
            {
                foreach (var mod in Players.ModsOf(owner))
                {
                    if (!mod.HookEnabled(HookId.CanHitNPCWithProj)) continue;
                    try
                    {
                        var next = mod.CanHitNPCWithProj(owner, projectile, npc);
                        if (next == false) return false;
                        result ??= next;
                    }
                    catch (Exception e) { Fail(mod, HookId.CanHitNPCWithProj, e); }
                }
                if (result == false) return false;
            }
        }
        // 显式的玩家决定优先于后续来源和目标判定；没有决定才继续查询。
        if (result is null) result = SourceGate(projectile, npc);
        if (result == false) return false;
        if (result is null) result = TargetGate(npc, projectile);
        return result;
    }

    private static bool? SourceGate(Projectile projectile, NPC npc)
    {
        bool? result = null;
        foreach (var global in GlobalProjectileRegistry.For(projectile, HookId.CanHitNPC))
        {
            try
            {
                var next = global.CanHitNPC(projectile, npc);
                if (next == false) return false;
                result ??= next;
            }
            catch (Exception e) { Fail(global, HookId.CanHitNPC, e); }
        }
        var own = Own(projectile);
        if (own is not null && own.Plan.Has(HookId.CanHitNPC))
        {
            try
            {
                var next = own.CanHitNPC(projectile, npc);
                if (next == false) return false;
                result ??= next;
            }
            catch (Exception e) { Fail(own, HookId.CanHitNPC, e); }
        }
        return result;
    }

    private static bool? TargetGate(NPC npc, Projectile projectile)
    {
        bool? result = null;
        foreach (var global in GlobalNpcRegistry.For(npc, HookId.CanBeHitByProjectile))
        {
            try
            {
                var next = global.CanBeHitByProjectile(npc, projectile);
                if (next == false) return false;
                result ??= next;
            }
            catch (Exception e) { Fail(global, HookId.CanBeHitByProjectile, e); }
        }
        var own = Target(npc);
        if (own is not null && own.HookEnabled(HookId.CanBeHitByProjectile))
        {
            try
            {
                var next = own.CanBeHitByProjectile(npc, projectile);
                if (next == false) return false;
                result ??= next;
            }
            catch (Exception e) { Fail(own, HookId.CanBeHitByProjectile, e); }
        }
        return result;
    }

    private static void ModifyHit(Projectile projectile, NPC npc, ref HitModifiers modifiers)
    {
        var own = Own(projectile);
        if (own is not null && own.Plan.Has(HookId.ModifyHitNPC))
        {
            var previous = modifiers;
            try { own.ModifyHitNPC(projectile, npc, ref modifiers); Require(modifiers); }
            catch (Exception e) { modifiers ??= previous; Fail(own, HookId.ModifyHitNPC, e); }
        }
        foreach (var global in GlobalProjectileRegistry.For(projectile, HookId.ModifyHitNPC))
        {
            var previous = modifiers;
            try { global.ModifyHitNPC(projectile, npc, ref modifiers); Require(modifiers); }
            catch (Exception e) { modifiers ??= previous; Fail(global, HookId.ModifyHitNPC, e); }
        }
        var target = Target(npc);
        if (target is not null && target.HookEnabled(HookId.ModifyHitByProjectile))
        {
            var previous = modifiers;
            try { target.ModifyHitByProjectile(npc, projectile, ref modifiers); Require(modifiers); }
            catch (Exception e) { modifiers ??= previous; Fail(target, HookId.ModifyHitByProjectile, e); }
        }
        foreach (var global in GlobalNpcRegistry.For(npc, HookId.ModifyHitByProjectile))
        {
            var previous = modifiers;
            try { global.ModifyHitByProjectile(npc, projectile, ref modifiers); Require(modifiers); }
            catch (Exception e) { modifiers ??= previous; Fail(global, HookId.ModifyHitByProjectile, e); }
        }
        if (Owner(projectile) is not { } owner) return;
        foreach (var hook in PlayerModifyHooks)
        {
            if (!Players.ModsActive(hook)) continue;
            foreach (var mod in Players.ModsOf(owner))
            {
                if (!mod.HookEnabled(hook)) continue;
                var previous = modifiers;
                try
                {
                    if (hook == HookId.ModifyHitNPC) mod.ModifyHitNPC(owner, npc, ref modifiers);
                    else mod.ModifyHitNPCWithProj(owner, projectile, npc, ref modifiers);
                    Require(modifiers);
                }
                catch (Exception e) { modifiers ??= previous; Fail(mod, hook, e); }
            }
        }
    }

    private static void AfterHit(Projectile projectile, NPC npc, HitInfo hit, int done)
    {
        var own = Own(projectile);
        if (own is not null && own.Plan.Has(HookId.OnHitNPC))
        {
            try { own.OnHitNPC(projectile, npc, hit, done); }
            catch (Exception e) { Fail(own, HookId.OnHitNPC, e); }
        }
        foreach (var global in GlobalProjectileRegistry.For(projectile, HookId.OnHitNPC))
        {
            try { global.OnHitNPC(projectile, npc, hit, done); }
            catch (Exception e) { Fail(global, HookId.OnHitNPC, e); }
        }
        var target = Target(npc);
        if (target is not null && target.HookEnabled(HookId.OnHitByProjectile))
        {
            try { target.OnHitByProjectile(npc, projectile, hit, done); }
            catch (Exception e) { Fail(target, HookId.OnHitByProjectile, e); }
        }
        foreach (var global in GlobalNpcRegistry.For(npc, HookId.OnHitByProjectile))
        {
            try { global.OnHitByProjectile(npc, projectile, hit, done); }
            catch (Exception e) { Fail(global, HookId.OnHitByProjectile, e); }
        }
        if (Owner(projectile) is not { } owner) return;
        foreach (var hook in PlayerAfterHooks)
        {
            if (!Players.ModsActive(hook)) continue;
            foreach (var mod in Players.ModsOf(owner))
            {
                if (!mod.HookEnabled(hook)) continue;
                try
                {
                    if (hook == HookId.OnHitNPC) mod.OnHitNPC(owner, npc, hit, done);
                    else mod.OnHitNPCWithProj(owner, projectile, npc, hit, done);
                }
                catch (Exception e) { Fail(mod, hook, e); }
            }
        }
    }

    private static void Require(HitModifiers? modifiers)
    {
        if (modifiers is null) throw new InvalidOperationException("命中修改器不能替换为 null");
    }

    private static void Fail(object author, HookId hook, Exception e) =>
        ContentErrors.Report($"[projectilehit] {author.GetType().Name}.{hook} 异常:{Safe.Describe(e)}");
}
