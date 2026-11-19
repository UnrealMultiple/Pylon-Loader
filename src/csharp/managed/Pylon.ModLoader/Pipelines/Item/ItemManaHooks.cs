using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;
using Pylon.ModLoader.Hooks;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Terraria;

namespace Pylon.ModLoader.Pipelines;
/// <summary>魔力开销：进 orig 前把 player.manaCost 临时换成作者算出的值（reduce×mult），orig 后恢复；钩子链 ModItem→GlobalItem→ModPlayer。</summary>
internal static class ItemManaHooks
{
    private static bool _globalMana;

    [ThreadStatic] private static bool _checking;

    internal static void Install()
    {
        RefreshGlobals();

        On.Terraria.Player.CheckMana += CheckMana;
        On.Terraria.Player.CheckManaPredictWithoutUse += CheckManaPredictWithoutUse;
    }

    internal static void RefreshGlobals() =>
        _globalMana = GlobalItemRegistry.AnyWith(HookId.ModifyManaCost, HookId.OnMissingMana, HookId.OnConsumeMana);

    private static bool Wanted(Item? item) =>
        item is not null && item.type > 0 && (_globalMana || ModContent.IsCustom(ContentKind.Item, item.type));

    private static Item? Held(Player player)
    {
        var slot = player.selectedItem;
        return slot >= 0 && slot < 59 ? player.inventory[slot] : null;
    }

    private static float Cost(Player player, Item item)
    {
        var reduce = player.manaCost;
        float mult = 1f;

        if (ItemPipeline.Instance.InstanceOf(item) is { } m && m.Plan.Has(HookId.ModifyManaCost))
        {
            try { m.ModifyManaCost(player, ref reduce, ref mult); }
            catch (Exception e) { Report(m.FullKey, e); }
        }

        foreach (var g in GlobalItemRegistry.For(item, HookId.ModifyManaCost))
        {
            try { g.ModifyManaCost(item, player, ref reduce, ref mult); }
            catch (Exception e) { Report(g.FullKey, e); }
        }

        var mods = PlayerPipeline.Instance.ModsOf(player);
        for (var i = 0; i < mods.Length; i++)
        {
            var mod = mods[i];
            if (!mod.HookEnabled(HookId.ModifyManaCost)) continue;

            try { mod.ModifyManaCost(item, ref reduce, ref mult); }
            catch (Exception e) { Report(PlayerKey(mod), e); }
        }

        var value = reduce * mult;
        return float.IsFinite(value) ? Math.Max(0f, value) : player.manaCost;
    }

    private static bool CheckMana(On.Terraria.Player.orig_CheckMana orig, Player self, int amount, bool pay, bool blockQuickMana)
    {
        var item = Held(self);
        if (_checking || item is null || item.stack <= 0 || !Wanted(item)) return orig(self, amount, pay, blockQuickMana);

        var previous = self.manaCost;
        _checking = true;
        try
        {
            self.manaCost = Cost(self, item);

            var needed = Math.Max(0, (int)(amount * self.manaCost));
            if (self.statMana < needed && !blockQuickMana) OnMissingMana(item, self, needed);

            var before = self.statMana;
            var result = orig(self, amount, pay, blockQuickMana);

            if (result && pay && needed > 0)
            {
                var spent = before - self.statMana;
                var consumed = self.slowMagicUse ? Math.Max(0, spent) : spent >= 0 && spent < needed ? spent : needed;
                if (consumed > 0) OnConsumeMana(item, self, consumed);
            }

            return result;
        }
        finally
        {
            self.manaCost = previous;
            _checking = false;
        }
    }

    private static bool CheckManaPredictWithoutUse(On.Terraria.Player.orig_CheckManaPredictWithoutUse orig,
                                                   Player self, int amountBeforeManaCost, bool allowQuickMana)
    {
        var item = Held(self);
        if (item is null || item.stack <= 0 || !Wanted(item)) return orig(self, amountBeforeManaCost, allowQuickMana);

        var previous = self.manaCost;
        self.manaCost = Cost(self, item);
        try { return orig(self, amountBeforeManaCost, allowQuickMana); }
        finally { self.manaCost = previous; }
    }

    private static void OnMissingMana(Item item, Player player, int neededMana)
    {
        if (ItemPipeline.Instance.InstanceOf(item) is { } m && m.Plan.Has(HookId.OnMissingMana))
        {
            try { m.OnMissingMana(player, neededMana); }
            catch (Exception e) { Report(m.FullKey, e); }
        }

        foreach (var g in GlobalItemRegistry.For(item, HookId.OnMissingMana))
        {
            try { g.OnMissingMana(item, player, neededMana); }
            catch (Exception e) { Report(g.FullKey, e); }
        }

        var mods = PlayerPipeline.Instance.ModsOf(player);
        for (var i = 0; i < mods.Length; i++)
        {
            var mod = mods[i];
            if (!mod.HookEnabled(HookId.OnMissingMana)) continue;

            try { mod.OnMissingMana(item, neededMana); }
            catch (Exception e) { Report(PlayerKey(mod), e); }
        }
    }

    private static void OnConsumeMana(Item item, Player player, int manaConsumed)
    {
        if (ItemPipeline.Instance.InstanceOf(item) is { } m && m.Plan.Has(HookId.OnConsumeMana))
        {
            try { m.OnConsumeMana(player, manaConsumed); }
            catch (Exception e) { Report(m.FullKey, e); }
        }

        foreach (var g in GlobalItemRegistry.For(item, HookId.OnConsumeMana))
        {
            try { g.OnConsumeMana(item, player, manaConsumed); }
            catch (Exception e) { Report(g.FullKey, e); }
        }

        var mods = PlayerPipeline.Instance.ModsOf(player);
        for (var i = 0; i < mods.Length; i++)
        {
            var mod = mods[i];
            if (!mod.HookEnabled(HookId.OnConsumeMana)) continue;

            try { mod.OnConsumeMana(item, manaConsumed); }
            catch (Exception e) { Report(PlayerKey(mod), e); }
        }
    }

    private static string PlayerKey(ModPlayer mod) => $"{mod.Mod?.Name}/{mod.GetType().Name}";

    private static void Report(string key, Exception e) =>
        ContentErrors.Report($"[item] {key} 异常：{Safe.Describe(e)}");
}
