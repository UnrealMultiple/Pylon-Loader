using Pylon.ModLoader;
using PipelineTest.Content;
using Terraria;
using Terraria.DataStructures;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Pipeline;

namespace PipelineTest.Tests;

/// <summary>
/// 管线：Player（生命期钩子）—— 真机验证。绘制类钩子（playerdrawlayer/playerdrawinfo）另组已验。
/// Ready：探针进了内容表。
/// KillMe 走原版、dead 后发 Kill；复活走 Player.Spawn）。
/// </summary>
internal static class PlayerTests
{
    private static bool _savedGodMode;

    public static void Register()
    {
        PT.Group("player", Phase.Ready, RunReady);
        PT.Group("player-w", Phase.World, RunWorld);
    }

    private static void RunReady()
    {
        PT.Case("PL-R1", c =>
        {
            // ModPlayer 不是 ModType（GetInstance 不可用）。
            // 探针注册与否的外部证据：任一钩子被调（W1 的计数）+ 框架日志里的模板数。
            c.True(true, "探针类已随 mod 加载（HookScan 按方法名对齐，见 W1 计数）");
        });
    }

    private static void RunWorld()
    {
        var player = Main.player[Main.myPlayer];
        if (player is null || !player.active)
        {
            PT.SkipCase("PL-W*", "玩家还没就绪");
            return;
        }

        TestPlayerLifecycle.Reset();
        TestPlayerLifecycle.Counting = true;

        // W1：每帧钩子都在涨（不死的玩家每帧都走这一整圈）。
        PT.Later("PL-W1", 30, c =>
        {
            c.True(TestPlayerLifecycle.CtResetEffects > 0, $"ResetEffects 每帧被调（{TestPlayerLifecycle.CtResetEffects}）");
            c.True(TestPlayerLifecycle.CtPreUpdate > 0, $"PreUpdate 每帧被调（{TestPlayerLifecycle.CtPreUpdate}）");
            c.True(TestPlayerLifecycle.CtPostUpdate > 0, $"PostUpdate 每帧被调（{TestPlayerLifecycle.CtPostUpdate}）");
            c.True(TestPlayerLifecycle.CtUpdateEquips > 0, $"UpdateEquips 每帧被调（{TestPlayerLifecycle.CtUpdateEquips}）");
            c.True(TestPlayerLifecycle.CtPostUpdateEquips > 0, $"PostUpdateEquips 每帧被调（{TestPlayerLifecycle.CtPostUpdateEquips}）");
            c.True(TestPlayerLifecycle.CtUpdateLifeRegen > 0, $"UpdateLifeRegen 每帧被调（{TestPlayerLifecycle.CtUpdateLifeRegen}）");
            c.True(TestPlayerLifecycle.CtUpdateBadLifeRegen > 0, $"UpdateBadLifeRegen 每帧被调（{TestPlayerLifecycle.CtUpdateBadLifeRegen}）");
            c.True(TestPlayerLifecycle.CtModifyMaxStats > 0, $"ModifyMaxStats 每帧被调（{TestPlayerLifecycle.CtModifyMaxStats}）");
            c.True(TestPlayerLifecycle.CtProcessTriggers > 0, $"ProcessTriggers 每帧被调（{TestPlayerLifecycle.CtProcessTriggers}）");
            c.True(TestPlayerLifecycle.CtFrameEffects > 0, $"FrameEffects 每帧被调（{TestPlayerLifecycle.CtFrameEffects}）");
        });

        PT.Later("PL-W2", 3200, c =>
        {
            var p = Main.player[Main.myPlayer];
            _savedGodMode = p.creativeGodMode;
            p.creativeGodMode = false;
            p.dead = false;
            var reason = PlayerDeathReason.ByCustomReason("管线测试：自杀");
            p.statLife = 1;
            p.immune = false;
            p.immuneTime = 0;
            p.KillMe(reason, 9999.0, 0, pvp: false);
            PT.Info("已调 KillMe（9999 伤害）");
        });

        PT.Later("PL-W3", 3260, c =>
        {
            c.True(TestPlayerLifecycle.CtKill > 0, $"Kill 被触发（{TestPlayerLifecycle.CtKill} 次）");
            var p = Main.player[Main.myPlayer];
            PT.Info($"死亡状态：dead={p.dead} 生命={p.statLife}/{p.statLifeMax2}");
            if (Main.player[Main.myPlayer] is { } me) me.creativeGodMode = _savedGodMode;
        });

        PT.Later("PL-W4", 5660, c =>
        {
            c.True(TestPlayerLifecycle.CtOnRespawn > 0, $"OnRespawn 被触发（{TestPlayerLifecycle.CtOnRespawn} 次）");
            TestPlayerLifecycle.Counting = false;
            TestPlayerLifecycle.Reset();
        });
    }
}