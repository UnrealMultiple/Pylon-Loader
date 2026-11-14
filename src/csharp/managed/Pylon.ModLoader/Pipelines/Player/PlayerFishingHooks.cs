using Terraria;
using Terraria.DataStructures;

namespace Pylon.ModLoader;

/// <summary>在原版选鱼前修改条件，在掷骰后回写物品和敌人结果。</summary>
internal static class PlayerFishingHooks
{
    private static bool _installed;
    [ThreadStatic] private static nint _catchPlayer;
    [ThreadStatic] private static bool _catchPending;
    private static PlayerPipeline Players => PlayerPipeline.Instance;

    internal static void Install()
    {
        if (_installed) return;
        _installed = true;
        On.Terraria.Projectile.FishingCheck_ProbeForQuestFish += Probe;
        On.Terraria.Projectile.FishingCheck_RollItemDrop += Roll;
        On.Terraria.Projectile.AI_061_FishingBobber_GiveItemToPlayer += Give;
        On.Terraria.Player.QuickSpawnItem_IEntitySource_Item += QuickSpawn;
        On.Terraria.Player.QuickSpawnItem_IEntitySource_Item_GetItemSettings += QuickSpawnWithSettings;
        On.Terraria.Player.ItemCheck_CheckFishingBobber_ConsumeBait += ConsumeBait;
        FishingLineHooks.Install();
    }

    private static bool ConsumeBait(On.Terraria.Player.orig_ItemCheck_CheckFishingBobber_ConsumeBait orig,
                                    Player self, Projectile bobber, out int baitTypeUsed)
    {
        var mods = LoaderHost.Current.Hooks.Channel(HookId.ConsumeBait);
        if (mods.IsEmpty)
            return orig(self, bobber, out baitTypeUsed);

        var inv = self.inventory;
        Span<int> before = stackalloc int[inv.Length * 2];
        Snapshot(inv, before);

        var caught = orig(self, bobber, out baitTypeUsed);
        var vanillaBait = baitTypeUsed;

        bool? decision = null;
        foreach (var mod in mods)
        {
            try
            {
                var d = mod.ConsumeBait(ref baitTypeUsed);
                if (d is not null && decision != false) decision = d;
            }
            catch (Exception e)
            {
                Safe.Report(mod.Name + ".ConsumeBait", e);
            }
        }

        if (decision.HasValue)
        {
            var changed = FindConsumedSlot(inv, before);
            if (decision.Value)
            {
                if (baitTypeUsed != vanillaBait && changed >= 0) RestoreSlot(inv, changed, before);
                if (baitTypeUsed != vanillaBait || changed < 0) ConsumeOne(inv, baitTypeUsed);
            }
            else if (changed >= 0)
            {
                RestoreSlot(inv, changed, before);
            }
        }

        return caught;
    }

    private static void Snapshot(Item[] inv, Span<int> before)
    {
        for (var i = 0; i * 2 + 1 < before.Length && i < inv.Length; i++)
        {
            var item = inv[i];
            before[i * 2] = item?.type ?? 0;
            before[i * 2 + 1] = item?.stack ?? 0;
        }
    }

    private static int FindConsumedSlot(Item[] inv, Span<int> before)
    {
        for (var i = 0; i < inv.Length && i * 2 + 1 < before.Length; i++)
        {
            var snapType = before[i * 2];
            if (snapType <= 0) continue;
            var item = inv[i];
            if (item is null || item.type == 0 || (item.type == snapType && item.stack < before[i * 2 + 1]))
                return i;
        }

        return -1;
    }

    private static void RestoreSlot(Item[] inv, int slot, Span<int> before)
    {
        var item = inv[slot];
        if (item is null || item.type == 0)
        {
            var restored = new Item { type = before[slot * 2], stack = before[slot * 2 + 1] };
            inv[slot] = restored;
        }
        else
        {
            item.stack++;
        }
    }

    private static void ConsumeOne(Item[] inv, int type)
    {
        if (type <= 0) return;

        for (var i = 0; i < inv.Length; i++)
        {
            var item = inv[i];
            if (item is null || item.type != type || item.stack <= 0) continue;
            item.stack--;
            if (item.stack <= 0) inv[i] = new Item();
            return;
        }
    }

    private static Player? Owner(Projectile projectile, HookId hook)
    {
        if (!Players.ModsActive(hook)) return null;
        var players = Main.player;
        var index = projectile.owner;
        if (players is null || (uint)index >= (uint)players.Length || index == 255) return null;
        return players[index];
    }

    private static void Probe(On.Terraria.Projectile.orig_FishingCheck_ProbeForQuestFish orig,
                              Projectile self, ref FishingAttempt attempt)
    {
        var player = Owner(self, HookId.ModifyFishingAttempt);
        if (player is not null)
        {
            var item = attempt.rolledItemDrop;
            var npc = attempt.rolledEnemySpawn;
            foreach (var mod in Players.ModsOf(player))
            {
                if (!mod.HookEnabled(HookId.ModifyFishingAttempt)) continue;
                try { mod.ModifyFishingAttempt(player, ref attempt); }
                catch (Exception e) { Fail(mod, HookId.ModifyFishingAttempt, e); }
            }
            attempt.rolledItemDrop = item;
            attempt.rolledEnemySpawn = npc;
        }
        orig(self, ref attempt);
    }

    private static void Roll(On.Terraria.Projectile.orig_FishingCheck_RollItemDrop orig,
                             Projectile self, ref FishingAttempt attempt)
    {
        orig(self, ref attempt);
        var player = Owner(self, HookId.CatchFish);
        if (player is null) return;
        foreach (var mod in Players.ModsOf(player))
        {
            if (!mod.HookEnabled(HookId.CatchFish)) continue;
            try { mod.CatchFish(player, ref attempt); }
            catch (Exception e) { Fail(mod, HookId.CatchFish, e); }
        }
    }

    private static void Give(On.Terraria.Projectile.orig_AI_061_FishingBobber_GiveItemToPlayer orig,
                             Projectile self, Player player, int itemType)
    {
        var outerPlayer = _catchPlayer;
        var outerPending = _catchPending;
        _catchPlayer = player.Pointer;
        _catchPending = Players.ModsActive(HookId.ModifyCaughtFish);
        try { orig(self, player, itemType); }
        finally
        {
            _catchPlayer = outerPlayer;
            _catchPending = outerPending;
        }
    }

    private static void Caught(Player player, Item fish)
    {
        if (!_catchPending || _catchPlayer != player.Pointer) return;
        _catchPending = false;
        foreach (var mod in Players.ModsOf(player))
        {
            if (!mod.HookEnabled(HookId.ModifyCaughtFish)) continue;
            try { mod.ModifyCaughtFish(player, fish); }
            catch (Exception e) { Fail(mod, HookId.ModifyCaughtFish, e); }
        }
    }

    private static void QuickSpawn(On.Terraria.Player.orig_QuickSpawnItem_IEntitySource_Item orig,
                                   Player self, IEntitySource source, Item item)
    {
        Caught(self, item);
        orig(self, source, item);
    }

    private static void QuickSpawnWithSettings(On.Terraria.Player.orig_QuickSpawnItem_IEntitySource_Item_GetItemSettings orig,
                                               Player self, IEntitySource source, Item item, GetItemSettings settings)
    {
        Caught(self, item);
        orig(self, source, item, settings);
    }

    private static void Fail(ModPlayer mod, HookId hook, Exception e) =>
        ContentErrors.Report($"[fishing] {mod.GetType().Name}.{hook} 异常:{Safe.Describe(e)}");
}
