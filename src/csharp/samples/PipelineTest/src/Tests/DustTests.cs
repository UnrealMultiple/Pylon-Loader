using System;
using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader;
using Terraria;

namespace PipelineTest.Tests;

/// <summary>
/// 管线 11：Dust —— 注册面 + 功能面。
/// mod 尘土不进游戏图集：走 DustPipeline.Spawn 生成，由管线自己 Update / Draw。
/// Ready：发号 / 贴图 / GetAlpha；World：在玩家周围撒一圈，肉眼可见地飘散收缩。
/// </summary>
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
            c.True(dust.Type >= 10000, $"type={dust.Type}（mod 尘土从 10000 起）");
        });

        // H1 功能：Spawn 真实生成 → OnSpawn 被调，noGravity 落位
        PT.Case("DUST-H1", c =>
        {
            if (dust is null) { c.Fail("没有实例"); return; }
            var before = Content.TestDust.OnSpawnCount;
            var d = dust.Spawn(new Vector2(100f, 100f), Vector2.Zero);
            c.NotNull(d, "Spawn 返回 dust");
            c.True(Content.TestDust.OnSpawnCount > before,
                $"OnSpawn 未触发（{before} -> {Content.TestDust.OnSpawnCount}）");
            if (d is not null) c.True(d.noGravity, "OnSpawn 里设置的 noGravity 生效");
        });

        // T1：贴图资源可加载（mod 尘土自己的贴图，不进游戏图集）
        PT.Case("DUST-T1", c =>
        {
            if (dust is null) { c.Fail("没有实例"); return; }
            c.NotNull(dust.Texture, "Texture");
            if (dust.Texture is not null)
                c.True(dust.Texture.Value is not null && dust.Texture.Value.Pointer != IntPtr.Zero, "贴图已解码");
        });

        // H3：GetAlpha 返回染色
        PT.Case("DUST-H3", c =>
        {
            if (dust is null) { c.Fail("没有实例"); return; }
            var alpha = dust.GetAlpha(new Il2CppMicrosoft.Xna.Framework.Graphics.Color(255, 255, 255, 255));
            c.True(alpha.R == 255 && alpha.G == 0, $"GetAlpha={alpha}（期望洋红）");
        });
    }

    /// <summary>进世界后：绕玩家撒 24 颗，向外飘散并随管线物理收缩。</summary>
    private static void RunWorld()
    {
        var dust = ModContent.GetInstance<Content.TestDust>();
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
            for (var i = 0; i < 24; i++)
            {
                var angle = i / 24f * MathHelper.TwoPi;
                var dir = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
                var d = dust.Spawn(center + dir * 60f, dir * 2.2f, 0, null, 3f);
                c.NotNull(d, $"第 {i + 1} 颗 Spawn");
            }
            c.Eq(Content.TestDust.OnSpawnCount - before, 24, "OnSpawn 次数");
        });
    }
}
