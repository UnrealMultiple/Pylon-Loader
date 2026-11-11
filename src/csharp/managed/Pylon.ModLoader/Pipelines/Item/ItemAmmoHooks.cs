using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Terraria;

namespace Pylon.ModLoader;

/// <summary>选择、发射参数与实际消耗共享一次选弹上下文；最后一发通知前保留弹药身份。</summary>
internal static class ItemAmmoHooks
{
    private sealed class AmmoScope(Player player, Item weapon, bool dontConsume)
    {
        internal readonly Player Player = player;
        internal readonly Item Weapon = weapon;
        internal readonly bool DontConsume = dontConsume;
        internal Item? Item;
        internal int Stack;
        internal bool Consumable, Blocked, Clear;
    }

    [ThreadStatic] private static AmmoScope? _ammo;
    private static readonly Dictionary<int, Item?> DefaultItems = [];
    private static bool _installed;
    private static int _templateCount = -1;
    private static bool _ammoSelection, _ammoSide;

    internal static void Install()
    {
        if (_installed) return;
        _installed = true;
        On.Terraria.Player.PickAmmo_IterateRange += Iterate;
        On.Terraria.Player.HasAmmo += HasAmmo;
        On.Terraria.Player.PickAmmo += Pick;
        On.Terraria.Player.PickAmmo_PickAmmoItem += PickItem;
        On.Terraria.Item.TurnToAir += TurnToAir;
    }

    private static ModItem? Own(Item? item) => item is null ? null : ItemPipeline.Instance.InstanceOf(item);
    private static bool Has(ModItem? item, HookId hook) => item is not null && item.Plan.Has(hook);
    private static bool Same(Item? left, Item? right) => left is not null && right is not null && left.Pointer == right.Pointer;

    private static void Refresh()
    {
        var items = ModContent.GetContent<ModItem>();
        if (_templateCount == items.Count) return;
        _templateCount = items.Count;
        _ammoSelection = false;
        _ammoSide = false;
        foreach (var item in items)
        {
            _ammoSelection |= item.Plan.Has(HookId.CanBeChosenAsAmmo);
            _ammoSide |= item.Plan.Has(HookId.CanBeChosenAsAmmo) || item.Plan.Has(HookId.CanBeConsumedAsAmmo) ||
                         item.Plan.Has(HookId.OnConsumedAsAmmo) || item.Plan.Has(HookId.PickAmmo);
        }
    }

    private static bool WantsSelection(Item weapon)
    {
        Refresh();
        return _ammoSelection || GlobalItemRegistry.AnyWith(HookId.CanChooseAmmo, HookId.CanBeChosenAsAmmo) ||
               Has(Own(weapon), HookId.CanChooseAmmo);
    }

    private static bool WantsAvailability(Item weapon) => WantsSelection(weapon) ||
        GlobalItemRegistry.AnyWith(HookId.NeedsAmmo) || Has(Own(weapon), HookId.NeedsAmmo);

    private static bool WantsAmmo(Item weapon)
    {
        Refresh();
        var own = Own(weapon);
        return _ammoSide || WantsAvailability(weapon) ||
               GlobalItemRegistry.AnyWith(HookId.CanConsumeAmmo, HookId.CanBeConsumedAsAmmo,
                   HookId.OnConsumeAmmo, HookId.OnConsumedAsAmmo, HookId.PickAmmo) ||
               Has(own, HookId.CanConsumeAmmo) || Has(own, HookId.OnConsumeAmmo) ||
               PlayerPipeline.Instance.ModsActive(HookId.CanConsumeAmmo) ||
               PlayerPipeline.Instance.ModsActive(HookId.OnConsumeAmmo);
    }

    private static bool NeedsAmmo(Item weapon, Player player)
    {
        var own = Own(weapon);
        if (Has(own, HookId.NeedsAmmo))
        {
            try { if (!own!.NeedsAmmo(weapon, player)) return false; }
            catch (Exception e) { Report(own!, HookId.NeedsAmmo, e); }
        }
        foreach (var global in GlobalItemRegistry.For(weapon, HookId.NeedsAmmo))
        {
            try { if (!global.NeedsAmmo(weapon, player)) return false; }
            catch (Exception e) { Report(global, HookId.NeedsAmmo, e); }
        }
        return true;
    }

    private static bool CanChoose(Item weapon, Item ammo, Player player)
    {
        bool? verdict = null;
        foreach (var global in GlobalItemRegistry.For(weapon, HookId.CanChooseAmmo))
        {
            try
            {
                var value = global.CanChooseAmmo(weapon, ammo, player);
                if (value == false) return false;
                verdict ??= value;
            }
            catch (Exception e) { Report(global, HookId.CanChooseAmmo, e); }
        }
        foreach (var global in GlobalItemRegistry.For(ammo, HookId.CanBeChosenAsAmmo))
        {
            try
            {
                var value = global.CanBeChosenAsAmmo(ammo, weapon, player);
                if (value == false) return false;
                verdict ??= value;
            }
            catch (Exception e) { Report(global, HookId.CanBeChosenAsAmmo, e); }
        }
        var weaponMod = Own(weapon);
        if (Has(weaponMod, HookId.CanChooseAmmo))
        {
            try
            {
                var value = weaponMod!.CanChooseAmmo(weapon, ammo, player);
                if (value == false) return false;
                verdict ??= value;
            }
            catch (Exception e) { Report(weaponMod!, HookId.CanChooseAmmo, e); }
        }
        var ammoMod = Own(ammo);
        if (Has(ammoMod, HookId.CanBeChosenAsAmmo))
        {
            try
            {
                var value = ammoMod!.CanBeChosenAsAmmo(ammo, weapon, player);
                if (value == false) return false;
                verdict ??= value;
            }
            catch (Exception e) { Report(ammoMod!, HookId.CanBeChosenAsAmmo, e); }
        }
        return verdict ?? ammo.ammo == weapon.useAmmo;
    }

    private static Item Iterate(On.Terraria.Player.orig_PickAmmo_IterateRange orig, Player self, Item weapon,
                                Il2CppReferenceArray<Item> items, Il2CppStructArray<int> order, bool allowCycling)
    {
        if (!WantsSelection(weapon)) return orig(self, weapon, items, order, allowCycling);
        try
        {
            var cycling = allowCycling && (int)self.ammoCyclingMode != 0 && weapon.type != 5134 && weapon.type != 779;
            List<Item>? eligible = null;
            for (var i = 0; i < order.Length; i++)
            {
                var index = order[i];
                if ((uint)index >= (uint)items.Length) continue;
                var candidate = items[index];
                if (candidate is null || candidate.type <= 0 || candidate.stack <= 0 || !CanChoose(weapon, candidate, self)) continue;
                if (!cycling) return candidate;
                (eligible ??= []).Add(candidate);
            }
            // 转 long 再取绝对值，避免 cyclingOffset 为 int.MinValue 时溢出。
            return eligible is { Count: > 0 } ? eligible[(int)(Math.Abs((long)self.ammoCyclingOffset) % eligible.Count)] : null!;
        }
        catch (Exception e)
        {
            ContentErrors.Report("[ammo] 选择异常:" + Safe.Describe(e));
            return orig(self, weapon, items, order, allowCycling);
        }
    }

    private static bool HasAmmo(On.Terraria.Player.orig_HasAmmo orig, Player self, Item weapon, bool canUse)
    {
        if (!WantsAvailability(weapon)) return orig(self, weapon, canUse);
        if (weapon.useAmmo <= 0) return canUse;
        try { return self.PickAmmo_PickAmmoItem(weapon) is not null || !NeedsAmmo(weapon, self); }
        catch (Exception e)
        {
            ContentErrors.Report("[ammo] 可用性异常:" + Safe.Describe(e));
            return orig(self, weapon, canUse);
        }
    }

    private static Item? DefaultAmmo(Item weapon)
    {
        var type = weapon.useAmmo;
        if (type <= 0) return null;
        if (DefaultItems.TryGetValue(type, out var cached)) return cached;
        var item = new Item();
        item.SetDefaults(type, null!);
        if (item.ammo != type) return DefaultItems[type] = null;
        item.consumable = false;
        return DefaultItems[type] = item;
    }

    private static void Pick(On.Terraria.Player.orig_PickAmmo orig, Player self, Item weapon, ref int type,
                             ref float speed, ref bool canShoot, ref int damage, ref float knockback,
                             out int usedAmmoItemId, bool dontConsume)
    {
        if (!WantsAmmo(weapon))
        {
            orig(self, weapon, ref type, ref speed, ref canShoot, ref damage, ref knockback, out usedAmmoItemId, dontConsume);
            return;
        }
        var outer = _ammo;
        var scope = new AmmoScope(self, weapon, dontConsume);
        _ammo = scope;
        try
        {
            orig(self, weapon, ref type, ref speed, ref canShoot, ref damage, ref knockback, out usedAmmoItemId, dontConsume);
            Finish(scope, ref type, ref speed, canShoot, ref damage, ref knockback);
        }
        finally
        {
            if (scope.Item is { } item && scope.Blocked) item.consumable = scope.Consumable;
            _ammo = null;
            try { if (scope.Clear) scope.Item!.TurnToAir(); }
            finally { _ammo = outer; }
        }
    }

    private static Item PickItem(On.Terraria.Player.orig_PickAmmo_PickAmmoItem orig, Player self, Item weapon)
    {
        var item = orig(self, weapon);
        var scope = _ammo;
        if (scope is null || scope.Player.Pointer != self.Pointer || !Same(scope.Weapon, weapon)) return item;
        try
        {
            if (item is null && !NeedsAmmo(weapon, self)) item = DefaultAmmo(weapon);
            if (item is null) return null!;
            scope.Item = item;
            scope.Stack = item.stack;
            scope.Consumable = item.consumable;
            if (!scope.DontConsume && item.consumable)
            {
                scope.Blocked = !CanConsume(weapon, item, self);
                if (scope.Blocked) item.consumable = false;
            }
        }
        catch (Exception e) { ContentErrors.Report("[ammo] 消耗检查异常:" + Safe.Describe(e)); }
        return item!;
    }

    private static bool CanConsume(Item weapon, Item ammo, Player player)
    {
        if (PlayerPipeline.Instance.ModsActive(HookId.CanConsumeAmmo))
        {
            var allowed = true;
            foreach (var mod in PlayerPipeline.Instance.ModsOf(player))
            {
                if (!mod.HookEnabled(HookId.CanConsumeAmmo)) continue;
                try { allowed &= mod.CanConsumeAmmo(player, weapon, ammo); }
                catch (Exception e) { ReportPlayer(mod, HookId.CanConsumeAmmo, e); }
            }
            if (!allowed) return false;
        }
        foreach (var global in GlobalItemRegistry.For(weapon, HookId.CanConsumeAmmo))
        {
            try { if (!global.CanConsumeAmmo(weapon, ammo, player)) return false; }
            catch (Exception e) { Report(global, HookId.CanConsumeAmmo, e); }
        }
        foreach (var global in GlobalItemRegistry.For(ammo, HookId.CanBeConsumedAsAmmo))
        {
            try { if (!global.CanBeConsumedAsAmmo(ammo, weapon, player)) return false; }
            catch (Exception e) { Report(global, HookId.CanBeConsumedAsAmmo, e); }
        }
        var own = Own(weapon);
        if (Has(own, HookId.CanConsumeAmmo))
        {
            try { if (!own!.CanConsumeAmmo(weapon, ammo, player)) return false; }
            catch (Exception e) { Report(own!, HookId.CanConsumeAmmo, e); }
        }
        own = Own(ammo);
        if (Has(own, HookId.CanBeConsumedAsAmmo))
        {
            try { if (!own!.CanBeConsumedAsAmmo(ammo, weapon, player)) return false; }
            catch (Exception e) { Report(own!, HookId.CanBeConsumedAsAmmo, e); }
        }
        return true;
    }

    private static void Finish(AmmoScope scope, ref int type, ref float speed, bool canShoot, ref int damage, ref float knockback)
    {
        if (scope.Item is not { } ammo || !canShoot) return;
        var previousType = type;
        var previousSpeed = speed;
        var previousKnockback = knockback;
        var modifier = StatModifier.Default;
        var own = Own(ammo);
        if (Has(own, HookId.PickAmmo))
        {
            try { own!.PickAmmo(ammo, scope.Weapon, scope.Player, ref type, ref speed, ref modifier, ref knockback); }
            catch (Exception e) { Report(own!, HookId.PickAmmo, e); }
        }
        foreach (var global in GlobalItemRegistry.For(ammo, HookId.PickAmmo))
        {
            try { global.PickAmmo(scope.Weapon, ammo, scope.Player, ref type, ref speed, ref modifier, ref knockback); }
            catch (Exception e) { Report(global, HookId.PickAmmo, e); }
        }
        if (modifier is not null && !modifier.Equals(StatModifier.Default))
        {
            var value = modifier.ApplyTo(damage);
            if (float.IsFinite(value)) damage = value >= int.MaxValue ? int.MaxValue : Math.Max(0, (int)value);
        }
        if (type < 0) type = previousType;
        if (!float.IsFinite(speed)) speed = previousSpeed;
        knockback = !float.IsFinite(knockback) ? previousKnockback : Math.Max(0, knockback);
        if (!scope.DontConsume && !scope.Blocked && ammo.stack < scope.Stack)
            Consumed(scope.Weapon, ammo, scope.Player);
    }

    private static void Consumed(Item weapon, Item ammo, Player player)
    {
        if (PlayerPipeline.Instance.ModsActive(HookId.OnConsumeAmmo))
        {
            foreach (var mod in PlayerPipeline.Instance.ModsOf(player))
            {
                if (!mod.HookEnabled(HookId.OnConsumeAmmo)) continue;
                try { mod.OnConsumeAmmo(player, weapon, ammo); }
                catch (Exception e) { ReportPlayer(mod, HookId.OnConsumeAmmo, e); }
            }
        }
        var own = Own(weapon);
        if (Has(own, HookId.OnConsumeAmmo))
        {
            try { own!.OnConsumeAmmo(weapon, ammo, player); }
            catch (Exception e) { Report(own!, HookId.OnConsumeAmmo, e); }
        }
        own = Own(ammo);
        if (Has(own, HookId.OnConsumedAsAmmo))
        {
            try { own!.OnConsumedAsAmmo(ammo, weapon, player); }
            catch (Exception e) { Report(own!, HookId.OnConsumedAsAmmo, e); }
        }
        foreach (var global in GlobalItemRegistry.For(weapon, HookId.OnConsumeAmmo))
        {
            try { global.OnConsumeAmmo(weapon, ammo, player); }
            catch (Exception e) { Report(global, HookId.OnConsumeAmmo, e); }
        }
        foreach (var global in GlobalItemRegistry.For(ammo, HookId.OnConsumedAsAmmo))
        {
            try { global.OnConsumedAsAmmo(ammo, weapon, player); }
            catch (Exception e) { Report(global, HookId.OnConsumedAsAmmo, e); }
        }
    }

    private static void TurnToAir(On.Terraria.Item.orig_TurnToAir orig, Item self)
    {
        var scope = _ammo;
        if (scope is not null && !scope.DontConsume && !scope.Blocked && Same(scope.Item, self) && self.stack < scope.Stack)
        {
            scope.Clear = true;
            return;
        }
        orig(self);
    }

    private static void Report(ModType author, HookId hook, Exception e) =>
        ContentErrors.Report($"[ammo] {author.Name}.{hook} 异常:{Safe.Describe(e)}");
    private static void ReportPlayer(ModPlayer author, HookId hook, Exception e) =>
        ContentErrors.Report($"[ammo] {author.GetType().Name}.{hook} 异常:{Safe.Describe(e)}");
}
