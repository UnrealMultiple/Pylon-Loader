using Terraria;

namespace Pylon.ModLoader;

/// <summary>玩家受伤的来源侧分派；实体实例与全局筛选沿用各内容管线的绑定。</summary>
internal static class PlayerDamageSources
{
    private static int _npcCount = -1, _projectileCount = -1;
    private static bool _npcActive, _projectileActive;

    internal static bool AnyActive()
    {
        var npcs = ModContent.GetContent<ModNpc>();
        var projectiles = ModContent.GetContent<ModProjectile>();
        if (_npcCount != npcs.Count)
        {
            _npcCount = npcs.Count;
            _npcActive = false;
            foreach (var npc in npcs)
                _npcActive |= npc.HookEnabled(HookId.CanHitPlayer) || npc.HookEnabled(HookId.ModifyHitPlayer) || npc.HookEnabled(HookId.OnHitPlayer);
        }
        if (_projectileCount != projectiles.Count)
        {
            _projectileCount = projectiles.Count;
            _projectileActive = false;
            foreach (var proj in projectiles)
                _projectileActive |= proj.Plan.Has(HookId.CanHitPlayer) || proj.Plan.Has(HookId.CanHitPvp) ||
                                     proj.Plan.Has(HookId.ModifyHitPlayer) || proj.Plan.Has(HookId.OnHitPlayer);
        }
        return _npcActive || _projectileActive ||
               GlobalNpcRegistry.AnyWith(HookId.CanHitPlayer, HookId.ModifyHitPlayer, HookId.OnHitPlayer) ||
               GlobalProjectileRegistry.AnyWith(HookId.CanHitPlayer, HookId.CanHitPvp, HookId.ModifyHitPlayer, HookId.OnHitPlayer);
    }

    internal static bool CanHit(NPC npc, Player target, ref int cooldown)
    {
        foreach (var global in GlobalNpcRegistry.For(npc, HookId.CanHitPlayer))
        {
            try { if (!global.CanHitPlayer(npc, target, ref cooldown)) return false; }
            catch (Exception e) { Fail(global, HookId.CanHitPlayer, e); }
        }
        var own = NpcPipeline.Instance.InstanceOf(npc);
        if (own is null || !own.HookEnabled(HookId.CanHitPlayer)) return true;
        try { return own.CanHitPlayer(npc, target, ref cooldown); }
        catch (Exception e) { Fail(own, HookId.CanHitPlayer, e); return true; }
    }

    internal static bool CanHit(Projectile projectile, Player target, bool pvp)
    {
        var hook = pvp ? HookId.CanHitPvp : HookId.CanHitPlayer;
        foreach (var global in GlobalProjectileRegistry.For(projectile, hook))
        {
            try
            {
                if (!(pvp ? global.CanHitPvp(projectile, target) : global.CanHitPlayer(projectile, target))) return false;
            }
            catch (Exception e) { Fail(global, hook, e); }
        }
        var own = ProjectilePipeline.Instance.InstanceOf(projectile);
        if (own is null || !own.Plan.Has(hook)) return true;
        try { return pvp ? own.CanHitPvp(projectile, target) : own.CanHitPlayer(projectile, target); }
        catch (Exception e) { Fail(own, hook, e); return true; }
    }

    internal static void Modify(NPC npc, Player target, ref HurtModifiers modifiers)
    {
        var own = NpcPipeline.Instance.InstanceOf(npc);
        if (own is not null && own.HookEnabled(HookId.ModifyHitPlayer))
        {
            var previous = modifiers;
            try { own.ModifyHitPlayer(npc, target, ref modifiers); Require(modifiers); }
            catch (Exception e) { modifiers ??= previous; Fail(own, HookId.ModifyHitPlayer, e); }
        }
        foreach (var global in GlobalNpcRegistry.For(npc, HookId.ModifyHitPlayer))
        {
            var previous = modifiers;
            try { global.ModifyHitPlayer(npc, target, ref modifiers); Require(modifiers); }
            catch (Exception e) { modifiers ??= previous; Fail(global, HookId.ModifyHitPlayer, e); }
        }
    }

    internal static void Modify(Projectile projectile, Player target, ref HurtModifiers modifiers)
    {
        var own = ProjectilePipeline.Instance.InstanceOf(projectile);
        if (own is not null && own.Plan.Has(HookId.ModifyHitPlayer))
        {
            var previous = modifiers;
            try { own.ModifyHitPlayer(projectile, target, ref modifiers); Require(modifiers); }
            catch (Exception e) { modifiers ??= previous; Fail(own, HookId.ModifyHitPlayer, e); }
        }
        foreach (var global in GlobalProjectileRegistry.For(projectile, HookId.ModifyHitPlayer))
        {
            var previous = modifiers;
            try { global.ModifyHitPlayer(projectile, target, ref modifiers); Require(modifiers); }
            catch (Exception e) { modifiers ??= previous; Fail(global, HookId.ModifyHitPlayer, e); }
        }
    }

    internal static void After(NPC npc, Player target, HurtInfo info)
    {
        var own = NpcPipeline.Instance.InstanceOf(npc);
        if (own is not null && own.HookEnabled(HookId.OnHitPlayer))
        {
            try { own.OnHitPlayer(npc, target, info); }
            catch (Exception e) { Fail(own, HookId.OnHitPlayer, e); }
        }
        foreach (var global in GlobalNpcRegistry.For(npc, HookId.OnHitPlayer))
        {
            try { global.OnHitPlayer(npc, target, info); }
            catch (Exception e) { Fail(global, HookId.OnHitPlayer, e); }
        }
    }

    internal static void After(Projectile projectile, Player target, HurtInfo info)
    {
        var own = ProjectilePipeline.Instance.InstanceOf(projectile);
        if (own is not null && own.Plan.Has(HookId.OnHitPlayer))
        {
            try { own.OnHitPlayer(projectile, target, info); }
            catch (Exception e) { Fail(own, HookId.OnHitPlayer, e); }
        }
        foreach (var global in GlobalProjectileRegistry.For(projectile, HookId.OnHitPlayer))
        {
            try { global.OnHitPlayer(projectile, target, info); }
            catch (Exception e) { Fail(global, HookId.OnHitPlayer, e); }
        }
    }

    private static void Require(HurtModifiers? modifiers)
    {
        if (modifiers is null) throw new InvalidOperationException("受伤修改器不能替换为 null");
    }

    private static void Fail(ModType author, HookId hook, Exception e) =>
        ContentErrors.Report($"[playerdamage] {author.GetType().Name}.{hook} 异常:{Safe.Describe(e)}");
}
