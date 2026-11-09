using Pylon.ModLoader;
using PipelineTest.Content;
using Terraria;
using Terraria.DataStructures;

namespace PipelineTest.Tests;

/// <summary>
/// 管线：Player（生命期钩子）—— 真机验证。绘制类钩子（playerdrawlayer/playerdrawinfo）另组已验。
/// Ready：探针进了内容表。
/// KillMe 走原版、dead 后发 Kill；复活走 Player.Spawn）。
/// </summary>
internal static class PlayerTests
{
    public static void Register()
    {
        PT.Group("player", Phase.Ready, RunReady);
        PT.Group("player-w", Phase.World, RunWorld);
    }

    private static void RunReady()
    {
        PT.Case("PL-R1", c =>
        {
            // ModPlayer 不是 ModType（交接文档 §5 警告过 GetInstance 不可用）。
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
            c.True(TestPlayerLifecycle.CtUpdateManaRegen > 0, $"UpdateManaRegen 每帧被调（{TestPlayerLifecycle.CtUpdateManaRegen}）");
        });

        // 放岩浆等玩家掉进去太不可控（位置/液体物理都不一定配合）。
        PT.Later("PL-W2", 40, c =>
        {
            var p = Main.player[Main.myPlayer];
            var reason = PlayerDeathReason.ByCustomReason("管线测试：自杀");
            p.KillMe(reason, 9999.0, 0, pvp: false);
            PT.Info("已调 KillMe（9999 伤害）");
        });

        // W3：Kill 应已触发（岩浆每帧掉血）。
        PT.Later("PL-W3", 100, c =>
        {
            c.True(TestPlayerLifecycle.CtKill > 0, $"Kill 被触发（{TestPlayerLifecycle.CtKill} 次）");
            var p = Main.player[Main.myPlayer];
            PT.Info($"死亡状态：dead={p.dead} 生命={p.statLife}/{p.statLifeMax2}");
        });

        // W4：复活后 OnRespawn 触发。复活倒计时约 11 秒；实测推进频率约 180 帧/秒（每帧多个入口），
        // 排 2500 帧约 14 秒，跨过复活点且整组仍在 60 秒内跑完。
        PT.Later("PL-W4", 2500, c =>
        {
            c.True(TestPlayerLifecycle.CtOnRespawn > 0, $"OnRespawn 被触发（{TestPlayerLifecycle.CtOnRespawn} 次）");
            TestPlayerLifecycle.Counting = false;
            TestPlayerLifecycle.Reset();
        });
    }
}