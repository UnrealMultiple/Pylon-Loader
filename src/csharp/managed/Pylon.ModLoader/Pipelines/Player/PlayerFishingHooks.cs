using Pylon.ModLoader.Content;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Terraria;
using Terraria.DataStructures;

namespace Pylon.ModLoader.Pipelines;
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
        On.Terraria.Player.GetFishingConditions += Conditions;
        On.Terraria.Player.GetAnglerReward += AnglerReward;
        On.Terraria.Player.GetAnglerRewardRarityMultiplier += AnglerRarity;
        On.Terraria.Player.GetOrDropItem += OrDrop;
        FishingLineHooks.Install();
    }

    private static bool _loggedLevel;

    private static PlayerFishingConditions Conditions(On.Terraria.Player.orig_GetFishingConditions orig, Player self)
    {
        var result = orig(self);

        if (result.BaitItemType == 2673 || result.BaitPower == 0 || result.PolePower == 0) return result;
        if (!Players.ModsActive(HookId.GetFishingLevel)) return result;

        var rod = FindItem(self, result.PoleItemType, [self.selectedItem]);
        var bait = FindItem(self, result.BaitItemType, [54, 55, 56, 57]);

        var multiplier = result.LevelMultipliers;
        foreach (var mod in Players.ModsOf(self))
        {
            if (!mod.HookEnabled(HookId.GetFishingLevel)) continue;

            try { mod.GetFishingLevel(rod, bait, ref multiplier); }
            catch (Exception e) { Fail(mod, HookId.GetFishingLevel, e); }
        }

        if (!float.IsFinite(multiplier) || multiplier == result.LevelMultipliers) return result;

        var level = BaseLevel(self, result);
        result.LevelMultipliers = multiplier;
        result.FinalFishingLevel = Math.Max(0, (int)(level * multiplier));
        return result;
    }

    private static Item FindItem(Player player, int type, int[] first)
    {
        var inventory = player.inventory;
        if (type <= 0 || inventory is null) return new Item();

        foreach (var i in first)
        {
            if ((uint)i >= (uint)inventory.Length) continue;
            var item = inventory[i];
            if (item is not null && item.type == type && item.stack > 0) return item;
        }

        for (var i = 0; i < 58 && i < inventory.Length; i++)
        {
            var item = inventory[i];
            if (item is not null && item.type == type && item.stack > 0) return item;
        }

        return new Item();
    }

    private static int BaseLevel(Player player, PlayerFishingConditions result)
    {
        var bonus = 0;
        if (player.FindBuffIndex(25) != -1) bonus += 5;
        if (player.canFloatInWater && player.wet) bonus += 5;
        if (player.sitting.isSitting) bonus += 5;

        var level = result.BaitPower + result.PolePower + player.fishingSkill + bonus;
        if ((int)(level * result.LevelMultipliers) == result.FinalFishingLevel || result.LevelMultipliers <= 0) return level;

        if (!_loggedLevel)
        {
            _loggedLevel = true;
            LoaderHost.Current.Log.Info(
                $"[fishing] 原版最终等级 {result.FinalFishingLevel} ≠ 基础 {level} × {result.LevelMultipliers}；改按商回退");
        }

        return (int)MathF.Round(result.FinalFishingLevel / result.LevelMultipliers);
    }

    private sealed class RewardScope
    {
        public Player Player = null!;
        public float Multiplier = 1f;
        public List<Item> Items { get; } = [];
        public GetItemSettings? Settings;
        public bool HasSettings;
    }

    [ThreadStatic] private static RewardScope? _rewards;

    private static void AnglerReward(On.Terraria.Player.orig_GetAnglerReward orig, Player self, NPC angler, int questItemType)
    {
        var outer = _rewards;
        var scope = new RewardScope { Player = self };
        _rewards = scope;
        try
        {
            orig(self, angler, questItemType);
        }
        finally
        {
            _rewards = outer;
        }

        if (Players.ModsActive(HookId.AnglerQuestReward))
        {
            foreach (var mod in Players.ModsOf(self))
            {
                if (!mod.HookEnabled(HookId.AnglerQuestReward)) continue;

                try { mod.AnglerQuestReward(scope.Multiplier, scope.Items); }
                catch (Exception e) { Fail(mod, HookId.AnglerQuestReward, e); }
            }
        }

        var settings = (scope.HasSettings ? scope.Settings : null) ?? GetItemSettings.NPCEntityToPlayerInventorySettings;
        foreach (var item in scope.Items)
        {
            if (item is not null && item.type > 0 && item.stack > 0) self.GetOrDropItem(item, settings);
        }
    }

    private static float AnglerRarity(On.Terraria.Player.orig_GetAnglerRewardRarityMultiplier orig, int questsDone)
    {
        var value = orig(questsDone);
        if (_rewards is not null) _rewards.Multiplier = value;
        return value;
    }

    private static void OrDrop(On.Terraria.Player.orig_GetOrDropItem orig, Player self, Item item, GetItemSettings settings)
    {
        var scope = _rewards;
        if (scope is null || !ReferenceEquals(scope.Player, self))
        {
            orig(self, item, settings);
            return;
        }

        scope.Items.Add(item);
        scope.Settings = settings;
        scope.HasSettings = true;
    }

    private static bool ConsumeBait(On.Terraria.Player.orig_ItemCheck_CheckFishingBobber_ConsumeBait orig,
                                    Player self, Projectile bobber, out int baitTypeUsed)
    {
        var wantPlayers = Players.ModsActive(HookId.CanConsumeBait);
        if (!wantPlayers)
            return orig(self, bobber, out baitTypeUsed);

        var inv = self.inventory;
        Span<int> before = stackalloc int[inv.Length * 2];
        Snapshot(inv, before);

        var caught = orig(self, bobber, out baitTypeUsed);
        var vanillaBait = baitTypeUsed;

        bool? decision = null;

        if (caught && baitTypeUsed > 0)
        {
            var bait = FindBait(inv, baitTypeUsed);
            foreach (var mod in Players.ModsOf(self))
            {
                if (!mod.HookEnabled(HookId.CanConsumeBait)) continue;
                try
                {
                    var d = mod.CanConsumeBait(bait);
                    if (d is not null && decision != false) decision = d;
                }
                catch (Exception e) { Fail(mod, HookId.CanConsumeBait, e); }
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
            else
            {
                GiveBack(inv, vanillaBait);
            }
        }

        return caught;
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

    private static Item FindBait(Item[] inv, int type)
    {
        for (var i = 0; i < inv.Length; i++)
        {
            var item = inv[i];
            if (item is not null && item.type == type) return item;
        }

        return new Item { type = type, stack = 1 };
    }

    private static void GiveBack(Item[] inv, int type)
    {
        for (var i = 0; i < inv.Length; i++)
        {
            var item = inv[i];
            if (item is null || item.type != type || item.stack <= 0) continue;
            item.stack++;
            return;
        }

        for (var i = 0; i < inv.Length; i++)
        {
            if (inv[i] is null || inv[i]!.type == 0 || inv[i]!.stack == 0)
            {
                inv[i] = new Item { type = type, stack = 1 };
                return;
            }
        }
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
                try { mod.ModifyFishingAttempt(ref attempt); }
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

        var item = attempt.rolledItemDrop;
        var npc = attempt.rolledEnemySpawn;
        var sonar = new AdvancedPopupRequest();
        var position = self.position;

        foreach (var mod in Players.ModsOf(player))
        {
            if (!mod.HookEnabled(HookId.CatchFish)) continue;
            try { mod.CatchFish(attempt, ref item, ref npc, ref sonar, ref position); }
            catch (Exception e) { Fail(mod, HookId.CatchFish, e); }
        }

        attempt.rolledItemDrop = item;
        attempt.rolledEnemySpawn = npc;

        if (player.sonarPotion && sonar.Text is not null &&
            !ReferenceEquals(sonar.Text, Terraria.Localization.LocalizedText.Empty))
        {
            PopupText.AssignAsSonarText(PopupText.NewText(sonar, position));
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
            try { mod.ModifyCaughtFish(fish); }
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
