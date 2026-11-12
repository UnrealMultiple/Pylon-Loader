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
        FishingLineHooks.Install();
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
            // 条件修改发生在掷骰之前；结果只能由后续掷骰与 CatchFish 决定。
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
        // 每层领奖独立消费一次，嵌套且无订阅的领奖也不能借用外层状态。
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
        // 两个重载可能相互转调；先消费，作者内部另行生成物品也不会递归触发。
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
