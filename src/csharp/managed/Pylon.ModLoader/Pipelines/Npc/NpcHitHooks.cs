using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;

namespace Pylon.ModLoader;

/// <summary>近战目标上下文只覆盖一次原版攻击，命中修饰算完后逆算原版入参以免重复扣防。</summary>
internal interface INpcStrikeSource
{
    bool ClampReturnToLife { get; }
    bool Matches(NPC target);
    void Modify(NPC target, ref HitModifiers modifiers);
    void After(NPC target, HitInfo hit, int damageDone);
}

internal static class NpcHitHooks
{
    [ThreadStatic] private static INpcStrikeSource? _source;
    [ThreadStatic] private static INpcStrikeSource? _executingSource;
    internal static INpcStrikeSource? ExecutingSource => _executingSource;

    internal readonly struct SourceScope : IDisposable
    {
        private readonly INpcStrikeSource? _outer;
        internal SourceScope(INpcStrikeSource source)
        {
            _outer = _source;
            _source = source;
        }
        public void Dispose() => _source = _outer;
    }

    internal static SourceScope BeginSource(INpcStrikeSource source) => new(source);
    private sealed class Attack(Player player, Item item, NPC target, bool? allowed)
    {
        internal readonly Player Player = player;
        internal readonly Item Item = item;
        internal readonly NPC Target = target;
        internal readonly bool? Allowed = allowed;
        internal bool Struck, RestoreFriendly, Friendly;
    }

    [ThreadStatic] private static Attack? _attack;
    [ThreadStatic] private static int _hideText, _critText;
    private static bool _installed;
    private static readonly HookId[] ItemHooks = [HookId.CanHitNPC, HookId.CanMeleeAttackCollideWithNPC, HookId.ModifyHitNPC, HookId.OnHitNPC];
    private static readonly HookId[] NpcHooks = [HookId.CanBeHitByItem, HookId.CanCollideWithPlayerMeleeAttack, HookId.ModifyIncomingHit, HookId.ModifyHitByItem, HookId.OnHitByItem];
    private static readonly HookId[] PlayerHooks = [HookId.CanHitNPC, HookId.CanHitNPCWithItem, HookId.CanMeleeAttackCollideWithNPC, HookId.ModifyHitNPC, HookId.ModifyHitNPCWithItem, HookId.OnHitNPC, HookId.OnHitNPCWithItem];
    private static readonly HookId[] PlayerModifyHooks = [HookId.ModifyHitNPC, HookId.ModifyHitNPCWithItem];
    private static readonly HookId[] PlayerAfterHooks = [HookId.OnHitNPC, HookId.OnHitNPCWithItem];
    private static PlayerPipeline Players => PlayerPipeline.Instance;
    private static ModItem? ItemOf(Item item) => ItemPipeline.Instance.InstanceOf(item);
    private static ModNpc? NpcOf(NPC npc) => NpcPipeline.Instance.InstanceOf(npc);
    private static bool Same(NPC a, NPC b) => a.Pointer == b.Pointer;

    internal static void Install()
    {
        if (_installed) return;
        _installed = true;
        On.Terraria.Player.ProcessHitAgainstNPC += Process;
        On.Terraria.Player.CanNPCBeHitByPlayerOrPlayerProjectile += CanBeHit;
        On.Terraria.NPC.StrikeNPC += Strike;
        On.Terraria.CombatText.NewText_Rectangle_Color_int_bool_bool += Text;
    }

    private static bool Wants(Item item, NPC npc)
    {
        if (GlobalItemRegistry.AnyWith(ItemHooks) || GlobalNpcRegistry.AnyWith(NpcHooks)) return true;
        var own = ItemOf(item);
        foreach (var hook in ItemHooks) if (own is not null && own.Plan.Has(hook)) return true;
        var target = NpcOf(npc);
        foreach (var hook in NpcHooks) if (target is not null && target.HookEnabled(hook)) return true;
        foreach (var hook in PlayerHooks) if (Players.ModsActive(hook)) return true;
        return false;
    }

    private static void Process(On.Terraria.Player.orig_ProcessHitAgainstNPC orig, Player self, Item item,
                                Rectangle rect, int damage, float knockback, int index)
    {
        if ((uint)index >= (uint)Main.npc.Length || Main.npc[index] is not { } target || !Wants(item, target))
        {
            orig(self, item, rect, damage, knockback, index);
            return;
        }
        var allowed = CanHit(item, self, target);
        if (allowed == false) return;
        var collide = Collision(item, self, target, rect);
        if (collide == false) return;
        if (collide == true)
        {
            var hitbox = target.Hitbox;
            var left = Math.Min(rect.X, hitbox.X); var top = Math.Min(rect.Y, hitbox.Y);
            rect = new Rectangle(left, top, Math.Max(rect.X + rect.Width, hitbox.X + hitbox.Width) - left,
                                 Math.Max(rect.Y + rect.Height, hitbox.Y + hitbox.Height) - top);
        }
        var outer = _attack;
        var current = new Attack(self, item, target, allowed);
        _attack = current;
        try { orig(self, item, rect, damage, knockback, index); }
        finally
        {
            if (current.RestoreFriendly) target.friendly = current.Friendly;
            _attack = outer;
        }
    }

    private static bool CanBeHit(On.Terraria.Player.orig_CanNPCBeHitByPlayerOrPlayerProjectile orig,
                                 Player self, NPC npc, Projectile projectile)
    {
        var attack = _attack;
        if (attack is null || projectile is not null || attack.Allowed != true ||
            attack.Player.Pointer != self.Pointer || !Same(attack.Target, npc)) return orig(self, npc, projectile!);
        if (npc.friendly && !attack.RestoreFriendly)
        {
            attack.Friendly = true;
            attack.RestoreFriendly = true;
            npc.friendly = false;
        }
        return true;
    }

    private static bool? PlayerGate(Player player, Item item, NPC npc, Rectangle rect, bool collision)
    {
        if (!collision && Players.ModsActive(HookId.CanHitNPC))
        {
            var allowed = true;
            foreach (var mod in Players.ModsOf(player))
            {
                if (!mod.HookEnabled(HookId.CanHitNPC)) continue;
                try { allowed &= mod.CanHitNPC(player, npc); }
                catch (Exception e) { Fail(mod, HookId.CanHitNPC, e); }
            }
            if (!allowed) return false;
        }
        var hook = collision ? HookId.CanMeleeAttackCollideWithNPC : HookId.CanHitNPCWithItem;
        bool? result = null;
        if (!Players.ModsActive(hook)) return result;
        foreach (var mod in Players.ModsOf(player))
        {
            if (!mod.HookEnabled(hook)) continue;
            try
            {
                var next = collision ? mod.CanMeleeAttackCollideWithNPC(player, item, rect, npc) : mod.CanHitNPCWithItem(player, item, npc);
                if (next == false) return false;
                result ??= next;
            }
            catch (Exception e) { Fail(mod, hook, e); }
        }
        return result;
    }

    private static bool? ItemGate(Item item, Player player, NPC npc, Rectangle rect, bool collision)
    {
        var hook = collision ? HookId.CanMeleeAttackCollideWithNPC : HookId.CanHitNPC;
        bool? result = null;
        foreach (var global in GlobalItemRegistry.For(item, hook))
        {
            try
            {
                var next = collision ? global.CanMeleeAttackCollideWithNPC(item, rect, player, npc) : global.CanHitNPC(item, player, npc);
                if (next == false) return false;
                result ??= next;
            }
            catch (Exception e) { Fail(global, hook, e); }
        }
        var own = ItemOf(item);
        if (own is not null && own.Plan.Has(hook))
        {
            try
            {
                var next = collision ? own.CanMeleeAttackCollideWithNPC(item, rect, player, npc) : own.CanHitNPC(item, player, npc);
                if (next == false) return false;
                result ??= next;
            }
            catch (Exception e) { Fail(own, hook, e); }
        }
        return result;
    }

    private static bool? TargetGate(NPC npc, Player player, Item item, Rectangle rect, bool collision)
    {
        var hook = collision ? HookId.CanCollideWithPlayerMeleeAttack : HookId.CanBeHitByItem;
        bool? result = null;
        foreach (var global in GlobalNpcRegistry.For(npc, hook))
        {
            try
            {
                var next = collision ? global.CanCollideWithPlayerMeleeAttack(npc, player, item, rect) : global.CanBeHitByItem(npc, player, item);
                if (next == false) return false;
                result ??= next;
            }
            catch (Exception e) { Fail(global, hook, e); }
        }
        var own = NpcOf(npc);
        if (own is not null && own.HookEnabled(hook))
        {
            try
            {
                var next = collision ? own.CanCollideWithPlayerMeleeAttack(npc, player, item, rect) : own.CanBeHitByItem(npc, player, item);
                if (next == false) return false;
                result ??= next;
            }
            catch (Exception e) { Fail(own, hook, e); }
        }
        return result;
    }

    private static bool? CanHit(Item item, Player player, NPC npc) => CombinedGate(item, player, npc, default, false);
    private static bool? Collision(Item item, Player player, NPC npc, Rectangle rect) => CombinedGate(item, player, npc, rect, true);
    private static bool? CombinedGate(Item item, Player player, NPC npc, Rectangle rect, bool collision)
    {
        var result = PlayerGate(player, item, npc, rect, collision);
        if (result == false) return false;
        var own = ItemGate(item, player, npc, rect, collision);
        result ??= own;
        if (result == false) return false;
        var target = TargetGate(npc, player, item, rect, collision);
        return result ?? target;
    }

    private static int Strike(On.Terraria.NPC.orig_StrikeNPC orig, NPC self, int damage, float knockback,
                              int direction, bool crit, bool fromNet, int owner)
    {
        if (fromNet) return orig(self, damage, knockback, direction, crit, fromNet, owner);
        var attack = _attack;
        var itemHit = attack is not null && !attack.Struck && Same(attack.Target, self);
        var source = !itemHit && _source?.Matches(self) == true ? _source : null;
        if (!itemHit && source is null && !(NpcOf(self)?.HookEnabled(HookId.ModifyIncomingHit) == true) &&
            !GlobalNpcRegistry.AnyWith(HookId.ModifyIncomingHit)) return orig(self, damage, knockback, direction, crit, false, owner);
        if (itemHit)
        {
            attack!.Struck = true;
            if (attack.RestoreFriendly) { self.friendly = attack.Friendly; attack.RestoreFriendly = false; }
        }
        // 修饰中主动造成另一击时不继承外层武器，嵌套原版入口结束后恢复原上下文。
        var outer = _attack;
        var outerSource = _source;
        var outerExecuting = _executingSource;
        _attack = null;
        _source = null;
        _executingSource = source;
        try { return ApplyStrike(orig, self, damage, knockback, direction, crit, owner, itemHit ? attack : null, source); }
        finally { _attack = outer; _source = outerSource; _executingSource = outerExecuting; }
    }

    private static int ApplyStrike(On.Terraria.NPC.orig_StrikeNPC orig, NPC npc, int damage, float knockback,
                                   int direction, bool crit, int owner, Attack? attack, INpcStrikeSource? source)
    {
        HitInfo hit;
        try
        {
            var modifiers = new HitModifiers(damage, knockback, direction, crit, npc.defense);
            TargetModify(npc, attack, HookId.ModifyIncomingHit, ref modifiers);
            if (attack is not null) ModifyItem(attack, ref modifiers);
            else source?.Modify(npc, ref modifiers);
            hit = modifiers.ToHitInfo();
        }
        catch (Exception e)
        {
            ContentErrors.Report("[npchit] 命中准备异常:" + Safe.Describe(e));
            return orig(npc, damage, knockback, direction, crit, false, owner);
        }
        var plan = Plan(npc, hit);
        var health = (uint)npc.realLife < (uint)Main.npc.Length ? Main.npc[npc.realLife] ?? npc : npc;
        var life = health.life;
        var immortal = npc.immortal;
        if (hit.HideCombatText) _hideText++;
        if (plan.TextCrit) _critText++;
        int done;
        try { done = orig(npc, plan.Raw, plan.Knockback, hit.HitDirection, plan.Crit, false, owner); }
        finally
        {
            if (hit.HideCombatText) _hideText--;
            if (plan.TextCrit) _critText--;
        }
        if (done <= 0) return done;
        if (!hit.InstantKill) hit.Damage = done;
        var actual = immortal ? 0 : Math.Min(Math.Max(life, 0), done);
        if (attack is not null)
        {
            AfterItem(attack, hit, actual);
            return actual;
        }
        if (source is not null)
        {
            source.After(npc, hit, actual);
            return source.ClampReturnToLife ? actual : done;
        }
        // 没有显式来源时保留原版返回值，不按当前生命截断。
        return done;
    }

    private static void TargetModify(NPC npc, Attack? attack, HookId hook, ref HitModifiers modifiers)
    {
        var own = NpcOf(npc);
        if (own is not null && own.HookEnabled(hook))
        {
            var previous = modifiers;
            try
            {
                if (hook == HookId.ModifyIncomingHit) own.ModifyIncomingHit(npc, ref modifiers);
                else own.ModifyHitByItem(npc, attack!.Player, attack.Item, ref modifiers);
                if (modifiers is null) throw new InvalidOperationException("命中修改器不能为 null");
            }
            catch (Exception e) { modifiers ??= previous; Fail(own, hook, e); }
        }
        foreach (var global in GlobalNpcRegistry.For(npc, hook))
        {
            var previous = modifiers;
            try
            {
                if (hook == HookId.ModifyIncomingHit) global.ModifyIncomingHit(npc, ref modifiers);
                else global.ModifyHitByItem(npc, attack!.Player, attack.Item, ref modifiers);
                if (modifiers is null) throw new InvalidOperationException("命中修改器不能为 null");
            }
            catch (Exception e) { modifiers ??= previous; Fail(global, hook, e); }
        }
    }

    private static void ModifyItem(Attack attack, ref HitModifiers modifiers)
    {
        var own = ItemOf(attack.Item);
        if (own is not null && own.Plan.Has(HookId.ModifyHitNPC))
        {
            var previous = modifiers;
            try { own.ModifyHitNPC(attack.Item, attack.Player, attack.Target, ref modifiers); Validate(modifiers); }
            catch (Exception e) { modifiers ??= previous; Fail(own, HookId.ModifyHitNPC, e); }
        }
        foreach (var global in GlobalItemRegistry.For(attack.Item, HookId.ModifyHitNPC))
        {
            var previous = modifiers;
            try { global.ModifyHitNPC(attack.Item, attack.Player, attack.Target, ref modifiers); Validate(modifiers); }
            catch (Exception e) { modifiers ??= previous; Fail(global, HookId.ModifyHitNPC, e); }
        }
        TargetModify(attack.Target, attack, HookId.ModifyHitByItem, ref modifiers);
        foreach (var hook in PlayerModifyHooks)
        {
            if (!Players.ModsActive(hook)) continue;
            foreach (var mod in Players.ModsOf(attack.Player))
            {
                if (!mod.HookEnabled(hook)) continue;
                var previous = modifiers;
                try
                {
                    if (hook == HookId.ModifyHitNPC) mod.ModifyHitNPC(attack.Player, attack.Target, ref modifiers);
                    else mod.ModifyHitNPCWithItem(attack.Player, attack.Item, attack.Target, ref modifiers);
                    Validate(modifiers);
                }
                catch (Exception e) { modifiers ??= previous; Fail(mod, hook, e); }
            }
        }
    }

    private static void Validate(HitModifiers modifiers)
    {
        if (modifiers is null) throw new InvalidOperationException("命中修改器不能为 null");
    }

    private static void AfterItem(Attack attack, HitInfo hit, int done)
    {
        var own = ItemOf(attack.Item);
        if (own is not null && own.Plan.Has(HookId.OnHitNPC))
        {
            try { own.OnHitNPC(attack.Item, attack.Player, attack.Target, hit, done); }
            catch (Exception e) { Fail(own, HookId.OnHitNPC, e); }
        }
        foreach (var global in GlobalItemRegistry.For(attack.Item, HookId.OnHitNPC))
        {
            try { global.OnHitNPC(attack.Item, attack.Player, attack.Target, hit, done); }
            catch (Exception e) { Fail(global, HookId.OnHitNPC, e); }
        }
        var npc = NpcOf(attack.Target);
        if (npc is not null && npc.HookEnabled(HookId.OnHitByItem))
        {
            try { npc.OnHitByItem(attack.Target, attack.Player, attack.Item, hit, done); }
            catch (Exception e) { Fail(npc, HookId.OnHitByItem, e); }
        }
        foreach (var global in GlobalNpcRegistry.For(attack.Target, HookId.OnHitByItem))
        {
            try { global.OnHitByItem(attack.Target, attack.Player, attack.Item, hit, done); }
            catch (Exception e) { Fail(global, HookId.OnHitByItem, e); }
        }
        foreach (var hook in PlayerAfterHooks)
        {
            if (!Players.ModsActive(hook)) continue;
            foreach (var mod in Players.ModsOf(attack.Player))
            {
                if (!mod.HookEnabled(hook)) continue;
                try
                {
                    if (hook == HookId.OnHitNPC) mod.OnHitNPC(attack.Player, attack.Target, hit, done);
                    else mod.OnHitNPCWithItem(attack.Player, attack.Item, attack.Target, hit, done);
                }
                catch (Exception e) { Fail(mod, hook, e); }
            }
        }
    }

    private readonly record struct StrikePlan(int Raw, float Knockback, bool Crit, bool TextCrit);
    private static int Integer(double value) => double.IsFinite(value) ? (int)Math.Clamp(value, 1d, int.MaxValue) : 1;
    private static StrikePlan Plan(NPC npc, HitInfo hit)
    {
        if (hit.InstantKill) return new(Integer(Math.Max(1d, npc.life) + Math.Ceiling(npc.defense / 2d) + 1d), hit.Knockback, false, false);
        var raw = Integer(Math.Ceiling(hit.Damage / (hit.Crit ? 2d : 1d) + npc.defense / 2d - 1e-9));
        if (!hit.Crit || Integer(2d * Math.Max(1d, raw - npc.defense * 0.5d)) == hit.Damage)
            return new(raw, hit.Knockback, hit.Crit, false);
        // 暴击整数倍率无法表达奇数伤害时，原版用普通伤害执行，表现仍保留暴击颜色与击退。
        var criticalKnockback = (float)Math.Clamp(hit.Knockback * 1.4d, 0d, float.MaxValue);
        return new(Integer(Math.Ceiling(hit.Damage + npc.defense / 2d - 1e-9)), criticalKnockback, false, true);
    }

    private static int Text(On.Terraria.CombatText.orig_NewText_Rectangle_Color_int_bool_bool orig,
                            Rectangle location, Color color, int amount, bool dramatic, bool dot)
    {
        if (_hideText > 0) return -1;
        if (_critText > 0)
        {
            if (color == CombatText.DamagedHostile) color = CombatText.DamagedHostileCrit;
            else if (color == CombatText.OthersDamagedHostile) color = CombatText.OthersDamagedHostileCrit;
            else if (color == CombatText.DamagedFriendly) color = CombatText.DamagedFriendlyCrit;
            dramatic = true;
        }
        return orig(location, color, amount, dramatic, dot);
    }

    private static void Fail(object author, HookId hook, Exception e) =>
        ContentErrors.Report($"[npchit] {author.GetType().Name}.{hook} 异常:{Safe.Describe(e)}");
}
