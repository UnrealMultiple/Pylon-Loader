using Terraria;

namespace Pylon.ModLoader;

/// <summary>接触攻击只在匹配目标的首次受击中使用来源上下文，嵌套攻击不继承外层来源。</summary>
internal static class NpcContactHooks
{
    private static bool _installed;
    private static readonly HookId[] SourceHooks = [HookId.CanHitNPC, HookId.ModifyHitNPC, HookId.OnHitNPC];

    internal static void Install()
    {
        if (_installed) return;
        _installed = true;
        On.Terraria.NPC.BeHurtByOtherNPC += Contact;
    }

    private sealed class ContactSource(NPC attacker, NPC victim) : INpcStrikeSource
    {
        private bool _struck;
        public bool ClampReturnToLife => false;
        public bool Matches(NPC target) => !_struck && target.Pointer == victim.Pointer;
        public void Modify(NPC target, ref HitModifiers modifiers)
        {
            _struck = true;
            var own = NpcPipeline.Instance.InstanceOf(attacker);
            if (own is not null && own.HookEnabled(HookId.ModifyHitNPC))
            {
                var previous = modifiers;
                try { own.ModifyHitNPC(attacker, target, ref modifiers); Validate(modifiers); }
                catch (Exception e) { modifiers ??= previous; Fail(own, HookId.ModifyHitNPC, e); }
            }
            foreach (var global in GlobalNpcRegistry.For(attacker, HookId.ModifyHitNPC))
            {
                var previous = modifiers;
                try { global.ModifyHitNPC(attacker, target, ref modifiers); Validate(modifiers); }
                catch (Exception e) { modifiers ??= previous; Fail(global, HookId.ModifyHitNPC, e); }
            }
        }

        public void After(NPC target, HitInfo hit, int damageDone)
        {
            var own = NpcPipeline.Instance.InstanceOf(attacker);
            if (own is not null && own.HookEnabled(HookId.OnHitNPC))
            {
                try { own.OnHitNPC(attacker, target, hit); }
                catch (Exception e) { Fail(own, HookId.OnHitNPC, e); }
            }
            foreach (var global in GlobalNpcRegistry.For(attacker, HookId.OnHitNPC))
            {
                try { global.OnHitNPC(attacker, target, hit); }
                catch (Exception e) { Fail(global, HookId.OnHitNPC, e); }
            }
        }
    }

    private static bool Wants(NPC attacker, NPC victim)
    {
        if (GlobalNpcRegistry.AnyWith(SourceHooks) || GlobalNpcRegistry.AnyWith(HookId.CanBeHitByNPC)) return true;
        var source = NpcPipeline.Instance.InstanceOf(attacker);
        foreach (var hook in SourceHooks) if (source?.HookEnabled(hook) == true) return true;
        return NpcPipeline.Instance.InstanceOf(victim)?.HookEnabled(HookId.CanBeHitByNPC) == true;
    }

    private static void Contact(On.Terraria.NPC.orig_BeHurtByOtherNPC orig, NPC self, int index, NPC attacker)
    {
        if (attacker is null || !Wants(attacker, self)) { orig(self, index, attacker!); return; }
        if (!CanHit(attacker, self)) return;
        using var scope = NpcHitHooks.BeginSource(new ContactSource(attacker, self));
        orig(self, index, attacker);
    }

    private static bool CanHit(NPC source, NPC target)
    {
        foreach (var global in GlobalNpcRegistry.For(source, HookId.CanHitNPC))
        {
            try { if (!global.CanHitNPC(source, target)) return false; }
            catch (Exception e) { Fail(global, HookId.CanHitNPC, e); }
        }
        foreach (var global in GlobalNpcRegistry.For(target, HookId.CanBeHitByNPC))
        {
            try { if (!global.CanBeHitByNPC(target, source)) return false; }
            catch (Exception e) { Fail(global, HookId.CanBeHitByNPC, e); }
        }
        var own = NpcPipeline.Instance.InstanceOf(source);
        if (own?.HookEnabled(HookId.CanHitNPC) == true)
        {
            try { if (!own.CanHitNPC(source, target)) return false; }
            catch (Exception e) { Fail(own, HookId.CanHitNPC, e); }
        }
        own = NpcPipeline.Instance.InstanceOf(target);
        if (own?.HookEnabled(HookId.CanBeHitByNPC) == true)
        {
            try { if (!own.CanBeHitByNPC(target, source)) return false; }
            catch (Exception e) { Fail(own, HookId.CanBeHitByNPC, e); }
        }
        return true;
    }

    private static void Validate(HitModifiers? modifiers)
    {
        if (modifiers is null) throw new InvalidOperationException("命中修改器不能为 null");
    }

    private static void Fail(ModType author, HookId hook, Exception e) =>
        ContentErrors.Report($"[npccontact] {author.GetType().Name}.{hook} 异常:{Safe.Describe(e)}");
}
