using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;
using Pylon.ModLoader.Globals;
using Pylon.ModLoader.Hooks;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Terraria;
using Terraria.ID;

namespace Pylon.ModLoader.Pipelines;
/// <summary>使用时长与攻速：ApplyItemTime/ApplyItemAnimation 之后按作者倍率缩放，倍率为 ModItem×GlobalItem×ModPlayer 乘积并除以 UseSpeedMultiplier；连发与右键也在此。</summary>
internal static class ItemTimingHooks
{
    private static bool GlobalTime, GlobalAnimation, GlobalAutoReuse, GlobalAlt;

    [ThreadStatic] private static int _timing;

    internal static void Install()
    {
        RefreshGlobals();

        On.Terraria.Player.ApplyItemTime_Item += ApplyItemTime;
        On.Terraria.Player.ApplyItemTime_Item_float += ApplyItemTimeMultiplied;
        On.Terraria.Player.ApplyItemAnimation += ApplyItemAnimation;
        On.Terraria.Player.ItemCheck_AutoReuseLogic += AutoReuseLogic;
        On.Terraria.Player.ItemCheck_ManageRightClickFeatures += ManageRightClickFeatures;
    }

    internal static void RefreshGlobals()
    {
        GlobalTime = GlobalItemRegistry.AnyWith(HookId.UseTimeMultiplier, HookId.UseSpeedMultiplier);
        GlobalAnimation = GlobalItemRegistry.AnyWith(HookId.UseAnimationMultiplier, HookId.UseSpeedMultiplier);
        GlobalAutoReuse = GlobalItemRegistry.AnyWith(HookId.CanAutoReuseItem);
        GlobalAlt = GlobalItemRegistry.AnyWith(HookId.AltFunctionUse);
    }

    private static bool Wanted(bool global, Item? item) =>
        item is not null && item.type > 0 && (global || ModContent.IsCustom(ContentKind.Item, item.type));

    private static float Multiplier(Item item, Player player, HookId hook,
                                    Func<ModItem, Player, float> own, Func<GlobalItem, Player, float> global)
    {
        var multiplier = 1f;

        if (ItemPipeline.Instance.InstanceOf(item) is { } m && m.Plan.Has(hook))
        {
            try { multiplier = own(m, player); }
            catch (Exception e) { Report(m.FullKey, hook, e); }
        }

        foreach (var g in GlobalItemRegistry.For(item, hook))
        {
            try { multiplier *= global(g, player); }
            catch (Exception e) { Report(g.FullKey, hook, e); }
        }

        return multiplier;
    }

    private static float PlayerProduct(Player player, HookId hook, Func<ModPlayer, Item, float> call, Item item)
    {
        var result = 1f;
        var mods = PlayerPipeline.Instance.ModsOf(player);
        for (var i = 0; i < mods.Length; i++)
        {
            var mod = mods[i];
            if (!mod.HookEnabled(hook)) continue;

            try { result *= call(mod, item); }
            catch (Exception e) { Report(PlayerKey(mod), hook, e); }
        }

        return result;
    }

    private static string PlayerKey(ModPlayer mod) => $"{mod.Mod?.Name}/{mod.GetType().Name}";

    private static float SpeedMultiplier(Item item, Player player) =>
        Multiplier(item, player, HookId.UseSpeedMultiplier, (m, p) => m.UseSpeedMultiplier(p), (g, p) => g.UseSpeedMultiplier(item, p)) *
        PlayerProduct(player, HookId.UseSpeedMultiplier, (m, i) => m.UseSpeedMultiplier(i), item);

    private static float TimeFactor(Item item, Player player) =>
        Multiplier(item, player, HookId.UseTimeMultiplier, (m, p) => m.UseTimeMultiplier(p), (g, p) => g.UseTimeMultiplier(item, p)) *
        PlayerProduct(player, HookId.UseTimeMultiplier, (m, i) => m.UseTimeMultiplier(i), item) /
        SpeedMultiplier(item, player);

    private static float AnimationFactor(Item item, Player player) =>
        Multiplier(item, player, HookId.UseAnimationMultiplier, (m, p) => m.UseAnimationMultiplier(p), (g, p) => g.UseAnimationMultiplier(item, p)) *
        PlayerProduct(player, HookId.UseAnimationMultiplier, (m, i) => m.UseAnimationMultiplier(i), item) /
        SpeedMultiplier(item, player);

    private static void Report(string key, HookId hook, Exception e) =>
        ContentErrors.Callback($"[item] {key}.{hook}", e);

    private static void ScaleTime(Player self, Item sItem)
    {
        var factor = TimeFactor(sItem, self);
        if (factor == 1f || !float.IsFinite(factor) || factor <= 0) return;

        var frames = Math.Max(1, (int)(self.itemTime * factor));
        self.itemTime = frames;
        self.itemTimeMax = frames;
    }

    private static void ApplyItemTime(On.Terraria.Player.orig_ApplyItemTime_Item orig, Player self, Item sItem)
    {
        if (_timing > 0 || !Wanted(GlobalTime, sItem))
        {
            orig(self, sItem);
            return;
        }

        _timing++;
        try { orig(self, sItem); }
        finally { _timing--; }

        ScaleTime(self, sItem);
    }

    private static void ApplyItemTimeMultiplied(On.Terraria.Player.orig_ApplyItemTime_Item_float orig, Player self, Item sItem, float multiplier)
    {
        if (_timing > 0 || !Wanted(GlobalTime, sItem))
        {
            orig(self, sItem, multiplier);
            return;
        }

        _timing++;
        try { orig(self, sItem, multiplier); }
        finally { _timing--; }

        ScaleTime(self, sItem);
    }

    private static void ApplyItemAnimation(On.Terraria.Player.orig_ApplyItemAnimation orig, Player self, Item sItem)
    {
        if (sItem is null || sItem.type <= 0)
        {
            orig(self, sItem);
            return;
        }

        if (ItemPipeline.Instance.InstanceOf(sItem) is { } m && m.Plan.Has(HookId.UseAnimation))
        {
            try { m.UseAnimation(self); }
            catch (Exception e) { Report(m.FullKey, HookId.UseAnimation, e); }
        }

        foreach (var g in GlobalItemRegistry.For(sItem, HookId.UseAnimation))
        {
            try { g.UseAnimation(sItem, self); }
            catch (Exception e) { Report(g.FullKey, HookId.UseAnimation, e); }
        }

        orig(self, sItem);

        if (!Wanted(GlobalAnimation, sItem)) return;

        var factor = AnimationFactor(sItem, self);
        if (factor == 1f || !float.IsFinite(factor) || factor <= 0) return;

        var frames = Math.Max(1, (int)(self.itemAnimation * factor));
        self.itemAnimation = frames;
        self.itemAnimationMax = frames;
    }

    private static bool? CanAutoReuseItem(Item item, Player player)
    {
        bool? flag = null;

        var mods = PlayerPipeline.Instance.ModsOf(player);
        for (var i = 0; i < mods.Length; i++)
        {
            var mod = mods[i];
            if (!mod.HookEnabled(HookId.CanAutoReuseItem)) continue;

            try
            {
                var value = mod.CanAutoReuseItem(item);
                if (value.HasValue && flag != false) flag = value.Value;
            }
            catch (Exception e) { Report(PlayerKey(mod), HookId.CanAutoReuseItem, e); }
        }

        if (flag == false) return false;

        foreach (var g in GlobalItemRegistry.For(item, HookId.CanAutoReuseItem))
        {
            try
            {
                var allow = g.CanAutoReuseItem(item, player);
                if (allow == false) return false;
                if (allow == true) flag = true;
            }
            catch (Exception e) { Report(g.FullKey, HookId.CanAutoReuseItem, e); }
        }

        if (ItemPipeline.Instance.InstanceOf(item) is { } m && m.Plan.Has(HookId.CanAutoReuseItem))
        {
            try
            {
                var allow = m.CanAutoReuseItem(player);
                if (allow == false) return false;
                if (allow == true) flag = true;
            }
            catch (Exception e) { Report(m.FullKey, HookId.CanAutoReuseItem, e); }
        }

        return flag;
    }

    private static void AutoReuseLogic(On.Terraria.Player.orig_ItemCheck_AutoReuseLogic orig, Player self, Item sItem)
    {
        var decision = Wanted(GlobalAutoReuse, sItem) ? CanAutoReuseItem(sItem, self) : null;
        if (decision is not bool allow)
        {
            orig(self, sItem);
            return;
        }

        var previous = sItem.autoReuse;
        var all = self.autoReuseAllWeapons;
        var glove = self.autoReuseGlove;
        sItem.autoReuse = allow;
        if (!allow)
        {
            self.autoReuseAllWeapons = false;
            self.autoReuseGlove = false;
        }

        try { orig(self, sItem); }
        finally
        {
            sItem.autoReuse = previous;
            self.autoReuseAllWeapons = all;
            self.autoReuseGlove = glove;
        }
    }

    private static bool AltFunctionUse(Item item, Player player)
    {
        if (ItemPipeline.Instance.InstanceOf(item) is { } m && m.Plan.Has(HookId.AltFunctionUse))
        {
            try
            {
                if (m.AltFunctionUse(player)) return true;
            }
            catch (Exception e) { Report(m.FullKey, HookId.AltFunctionUse, e); }
        }

        foreach (var g in GlobalItemRegistry.For(item, HookId.AltFunctionUse))
        {
            try
            {
                if (g.AltFunctionUse(item, player)) return true;
            }
            catch (Exception e) { Report(g.FullKey, HookId.AltFunctionUse, e); }
        }

        return false;
    }

    private static void ManageRightClickFeatures(On.Terraria.Player.orig_ItemCheck_ManageRightClickFeatures orig, Player self)
    {
        Item? held = null;
        var check = false;
        try
        {
            var slot = self.selectedItem;
            held = slot >= 0 && slot < 58 ? self.inventory[slot] : null;
            if (held is not null && held.stack > 0 && Wanted(GlobalAlt, held))
            {
                var repeated = ItemID.Sets.ItemsThatAllowRepeatedRightClick;
                var repeat = repeated is not null && (uint)held.type < (uint)repeated.Length && repeated[held.type];
                check = self.controlUseTile && Main.myPlayer == self.whoAmI && !self.tileInteractionHappened && self.releaseUseItem &&
                        !self.controlUseItem && !self.mouseInterface && !Main.LocalPlayerHasPendingInventoryActions() && (repeat || Main.mouseRightRelease);
            }
        }
        catch (Exception e)
        {
            ContentErrors.Callback("[item] AltFunctionUse", e);
        }

        orig(self);

        if (!check || self.altFunctionUse != 0 || held is null || !AltFunctionUse(held, self)) return;

        self.altFunctionUse = 1;
        self.controlUseItem = true;
    }
}
