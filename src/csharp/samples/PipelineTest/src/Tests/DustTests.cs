using System;
using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader;
using Terraria;
using Terraria.ID;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;
using Pylon.ModLoader.Pipelines;

namespace PipelineTest.Tests;

/// <summary>管线 11：Dust —— 注册面（Ready：发号 / 贴图 / GetAlpha；mod 尘土走 DustPipeline.Spawn 生成，由管线自己 Update / Draw）+ 功能面（World：在玩家周围撒一圈，肉眼可见地飘散收缩）。</summary>
internal static class DustTests
{
    public static void Register()
    {
        PT.Group("dust", Phase.Ready, RunReady);
        PT.Group("dust-w", Phase.World, RunWorld);
    }

    private static void RunReady()
    {
        var dust = ModContent.GetInstance<Content.TestDust>();

        PT.Case("DUST-R1", c =>
        {
            c.NotNull(dust, "GetInstance");
            if (dust is null) return;
            c.True(dust.Type >= 332 && dust.Type < 10000, $"type={dust.Type}（mod 尘土号段 332+k）");
        });

        PT.Case("DUST-R2", c =>
        {
            c.Eq((int)DustID.WoodFurniture, 7, "WoodFurniture 别名");
            c.Eq((int)DustID.Rain, 154, "Rain 别名");
            c.Eq((int)DustID.LastPrism, 267, "LastPrism 别名");
            c.Eq((int)DustID.Water, 33, "Water 别名");
        });

        PT.Case("DUST-H1", c =>
        {
            // 验证脚手架适配：菜单期原版 Dust.NewDust 有 gameMenu 短路（不建尘土直接返回），
            // Ready 期 Spawn 派发不到 OnSpawn；OnSpawn/noGravity 断言由世界期的 DUST-W1 承接。
            if (Main.gameMenu)
            {
                c.Skip("菜单期原版 NewDust 短路，OnSpawn 断言由 DUST-W1 承接");
                return;
            }
            if (dust is null) { c.Fail("没有实例"); return; }
            var before = Content.TestDust.OnSpawnCount;
            var d = dust.Spawn(new Vector2(100f, 100f), Vector2.Zero);
            c.NotNull(d, "Spawn 返回 dust");
            c.True(Content.TestDust.OnSpawnCount > before,
                $"OnSpawn 未触发（{before} -> {Content.TestDust.OnSpawnCount}）");
            if (d is not null) c.True(d.noGravity, "OnSpawn 里设置的 noGravity 生效");
        });

        PT.Case("DUST-T1", c =>
        {
            if (dust is null) { c.Fail("没有实例"); return; }
            c.NotNull(dust.Texture, "Texture");
            if (dust.Texture is not null)
                c.True(dust.Texture.Value is not null && dust.Texture.Value.Pointer != IntPtr.Zero, "贴图已解码");
        });

        PT.Case("DUST-H3", c =>
        {
            if (dust is null) { c.Fail("没有实例"); return; }
            var alpha = dust.GetAlpha(null!, new Il2CppMicrosoft.Xna.Framework.Graphics.Color(255, 255, 255, 255));
            c.True(alpha is { } mine && mine.R == 255 && mine.G == 0, $"GetAlpha={alpha}（期望洋红）");
        });
    }

    private static void RunWorld()
    {
        var dust = ModContent.GetInstance<Content.TestDust>();
        var killDust = ModContent.GetInstance<Content.TestDustKill>();
        var player = Main.player[Main.myPlayer];
        if (dust is null || player is null || !player.active)
        {
            PT.SkipCase("DUST-W1", "玩家/尘土未就绪");
            return;
        }

        PT.Case("DUST-W1", c =>
        {
            var before = Content.TestDust.OnSpawnCount;
            var center = player.Center;
            Terraria.Dust? first = null;
            for (var i = 0; i < 24; i++)
            {
                var angle = i / 24f * MathHelper.TwoPi;
                var dir = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
                var d = dust.Spawn(center + dir * 60f, dir * 2.2f, 0, null, 3f);
                c.NotNull(d, $"第 {i + 1} 颗 Spawn");
                first ??= d;
            }
            c.Eq(Content.TestDust.OnSpawnCount - before, 24, "OnSpawn 次数");
            // 验证脚手架适配：菜单期 DUST-H1 短路，OnSpawn 副作用（noGravity）挪到世界期验。
            if (first is not null) c.True(first.noGravity, "OnSpawn 里设置的 noGravity 生效（世界期）");
        });

        // B1 验证脚手架：击杀型尘土——存活 KillFrames 帧后作者 Update 置 active=false，当场消失。
        if (killDust is not null)
        {
            Content.TestDustKill.Reset();
            PT.Case("DUST-W3", c =>
            {
                var d = killDust.Spawn(player.Center, new Vector2(0.3f, -0.5f));
                c.NotNull(d, "击杀型 Spawn");
                c.Eq(Content.TestDustKill.SpawnCount, 1, "OnSpawn 计数");
            });

            // 验证脚手架适配：手机版尘土 Update 每帧可能跑多 tick（180 tick 不等于 60 帧），
            // 「存活断言」改巡检式：每 30 帧查一次，只记不判（见数就判），死了交 W3-KILL 收口。
            var aliveRounds = 0;
            var sawInactive = false;
            void PollAlive(Check c)
            {
                if (Content.TestDustKill.Killed)
                {
                    c.True(!sawInactive, $"存活期巡检（{aliveRounds} 轮均 active，直至作者击杀）");
                    return;
                }

                if (Content.TestDustKill.Last is not { active: true }) sawInactive = true;
                if (++aliveRounds >= 40)
                {
                    c.True(!sawInactive, $"存活期巡检（{aliveRounds} 轮未见击杀，期间均 active={sawInactive == false}）");
                    return;
                }

                c.Pending();
                PT.RetryLater("DUST-W3-ALIVE", 30, PollAlive);
            }

            PT.RetryLater("DUST-W3-ALIVE", 30, PollAlive);

            var killRounds = 0;
            void PollKill(Check c)
            {
                if (!Content.TestDustKill.Killed && ++killRounds < 20)
                {
                    c.Pending();
                    PT.RetryLater("DUST-W3-KILL", 30, PollKill);
                    return;
                }

                c.True(Content.TestDustKill.Killed, $"作者 Update 已置 active=false（{Content.TestDustKill.UpdateCount} 帧，巡检 {killRounds} 轮）");
                c.True(Content.TestDustKill.Last is { active: false }, "尘土当场消失");
            }

            PT.RetryLater("DUST-W3-KILL", 240, PollKill);
        }
    }
}
