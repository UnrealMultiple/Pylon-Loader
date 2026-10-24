using PipelineTest.Content;
using Pylon.ModLoader;
using Terraria;
using Terraria.GameContent.UI.ResourceSets;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Pipelines;
using PlayerDrawLayers = Pylon.ModLoader.Content.PlayerDrawLayers;

namespace PipelineTest.Tests;

/// <summary>Player.TML.cs 等价面：ModPlayer 查找、生命/魔力上限、免疫时间、跳跃状态、快照属性与原版层锚点。</summary>
internal static class PlayerApiTests
{
    public static void Register()
    {
        PT.Group("player-api", Phase.Ready, RunReady);
        PT.Group("player-api-w", Phase.World, RunWorld);
    }

    private static void RunReady()
    {
        PT.Case("PLAYER-API-1", c =>
        {
            c.Eq(Player.LifeCrystalMax, 15, "生命水晶上限常量");
            c.Eq(Player.LifeFruitMax, 20, "生命果上限常量");
            c.Eq(Player.ManaCrystalMax, 9, "魔力水晶上限常量");
        });

        PT.Case("PLAYER-API-2", c =>
        {
            c.True(PlayerDrawLayers.HairBack is not null, "HairBack 锚点");
            c.True(PlayerDrawLayers.MountBack is not null, "MountBack 锚点");
            c.True(PlayerDrawLayers.HeadBack is not null, "HeadBack 锚点");
            c.True(PlayerDrawLayers.BalloonAcc is not null, "BalloonAcc 锚点");
            c.True(PlayerDrawLayers.HandOnAcc is not null, "HandOnAcc 锚点");
            c.True(PlayerDrawLayers.FrontAccFront is not null, "FrontAccFront 锚点");
            c.True(PlayerDrawLayers.FrontAccBack is not null, "FrontAccBack 锚点");
            c.True(PlayerDrawLayers.CaptureTheGem is not null, "CaptureTheGem 锚点");
            c.True(!ReferenceEquals(PlayerDrawLayers.Robe, PlayerDrawLayers.Leggings), "Robe 与 Leggings 是独立锚点");
        });
    }

    private static void RunWorld()
    {
        var player = Main.LocalPlayer;
        if (player is null || !player.active)
        {
            PT.SkipCase("PLAYER-API-W*", "玩家还没就绪");
            return;
        }

        PT.Case("PLAYER-API-W1", c =>
        {
            var all = player.ModPlayers;
            c.True(all.Length > 0, $"ModPlayers 非空（{all.Length}）");

            var probe = player.GetModPlayer<TestPlayerLifecycle>();
            c.True(probe is not null, "GetModPlayer<T>() 拿到实例");
            c.True(ReferenceEquals(probe, all[probe.Index]), "实例下标与 ModPlayers 一致");

            c.True(player.TryGetModPlayer<TestPlayerLifecycle>(out var probed) && ReferenceEquals(probed, probe),
                "TryGetModPlayer<T>() 命中同一实例");
            c.True(player.TryGetModPlayer(probe, out var byInstance) && ReferenceEquals(byInstance, probe),
                "TryGetModPlayer<T>(baseInstance) 命中同一实例");
        });

        PT.Case("PLAYER-API-W2", c =>
        {
            var p = new Player { statLifeMax = 100, statManaMax = 20 };
            c.Eq(p.ConsumedLifeCrystals, 0, "新玩家生命水晶 0");
            p.ConsumedLifeCrystals = 5;
            c.Eq(p.statLifeMax, 200, "5 个生命水晶写回上限");
            c.Eq(p.ConsumedLifeCrystals, 5, "生命水晶读回");
            // 生命果的读回公式以 400（15 水晶）为基数，先铺满水晶再算果。
            p.statLifeMax = 400;
            p.ConsumedLifeFruit = 10;
            c.Eq(p.statLifeMax, 450, "10 个生命果写回上限");
            c.Eq(p.ConsumedLifeFruit, 10, "生命果读回");
            c.Eq(p.ConsumedLifeCrystals, 15, "满果时水晶读数落在 400");
            p.ConsumedManaCrystals = 9;
            c.Eq(p.statManaMax, 200, "9 个魔力水晶写回上限");
            c.Eq(p.ConsumedManaCrystals, 9, "魔力水晶读回");

            p.statLife = 50;
            p.statLifeMax2 = 100;
            p.whoAmI = 255;
            p.UseHealthMaxIncreasingItem(20);
            c.Eq(p.statLife, 70, "生命上限道具加当前生命");
            c.Eq(p.statLifeMax2, 120, "生命上限道具加最大生命");

            p.statMana = 10;
            p.statManaMax2 = 20;
            p.UseManaMaxIncreasingItem(20);
            c.Eq(p.statMana, 30, "魔力上限道具加当前魔力");
            c.Eq(p.statManaMax2, 40, "魔力上限道具加最大魔力");

            p.immuneTime = 0;
            p.AddImmuneTime(-1, 25);
            c.Eq(p.immuneTime, 25, "AddImmuneTime 走通用冷却");
        });

        PT.Case("PLAYER-API-W3", c =>
        {
            var p = new Player();
            var buffs = p.buffType;
            if (buffs is null || buffs.Length == 0)
            {
                PT.Info("新玩家没有 buff 槽位，跳过 HasBuff 断言");
                return;
            }

            buffs[0] = Terraria.ID.BuffID.Regeneration;
            p.buffTime[0] = 60;
            c.True(p.HasBuff(Terraria.ID.BuffID.Regeneration), "HasBuff 命中第一个槽位");
            c.True(!p.HasBuff(Terraria.ID.BuffID.Poisoned), "HasBuff 不误报别的 buff");
        });

        PT.Case("PLAYER-API-W4", c =>
        {
            ref var state = ref player.GetJumpState<TestJump>();
            state.Enable();
            c.True(player.GetJumpState<TestJump>().Enabled, "GetJumpState<T>() 返回同一份状态");
            c.Eq(player.ExtraJumps.Length, ExtraJumpTable.ExtraJumpCount, "ExtraJumps 长度等于跳跃表长度");

            var saved = player.blockExtraJumps;
            player.blockExtraJumps = true;
            c.True(player.blockExtraJumps, "blockExtraJumps 可写");
            player.blockExtraJumps = saved;
        });

        PT.Case("PLAYER-API-W5", c =>
        {
            var snapshot = new PlayerStatsSnapshot(player);
            c.True(snapshot.AmountOfLifeHearts is >= 0 and <= 20, $"AmountOfLifeHearts 在 0..20（{snapshot.AmountOfLifeHearts}）");
            c.True(snapshot.AmountOfManaStars is >= 0 and <= 20, $"AmountOfManaStars 在 0..20（{snapshot.AmountOfManaStars}）");
        });
    }
}
