using System;
using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;

namespace PipelineTest.Tests;

/// <summary>
/// 管线 14：Gore —— 注册面 + 功能面。
/// Ready：发号 / 贴图进 TextureAssets.Gore / NewGore 真生成触发 OnSpawn。
/// World：在玩家周围喷一圈红色粒子，肉眼可见（受重力、落地）。
/// </summary>
internal static class GoreTests
{
    public static void Register()
    {
        PT.Group("gore", Phase.Ready, RunReady);
        PT.Group("gore-w", Phase.World, RunWorld);
    }

    private static void RunReady()
    {
        var type = ModContent.TypeOf<Content.TestGore>();

        PT.Case("GORE-R1", c =>
        {
            c.True(type >= GoreID.Count, $"type={type}（原版 {GoreID.Count}）");
            c.NotNull(ModContent.GetInstance<Content.TestGore>(), "GetInstance");
        });

        PT.Case("GORE-T1", c =>
        {
            var table = TextureAssets.Gore;
            var len = table?.Length ?? 0;
            c.True(len > type, $"粒子贴图表长={len}（应 > {type}）");
            if (len > type) c.NotNull(table![type], $"[{type}] 槽位");
        });

        PT.Case("GORE-H1", c =>
        {
            var before = Content.TestGore.OnSpawnCount;
            var idx = Gore.NewGore(new Vector2(100f, 100f), new Vector2(0f, -1f), type, 1f);
            c.True(idx >= 0, $"NewGore 返回 {idx}");
            c.True(Content.TestGore.OnSpawnCount > before,
                $"OnSpawn 未触发（{before} -> {Content.TestGore.OnSpawnCount}）");
        });
    }

    /// <summary>进世界后：绕玩家喷 12 粒红色血腥粒子。</summary>
    private static void RunWorld()
    {
        var type = ModContent.TypeOf<Content.TestGore>();
        var player = Main.player[Main.myPlayer];
        if (player is null || !player.active)
        {
            PT.SkipCase("GORE-W1", "玩家还没就绪");
            return;
        }

        PT.Case("GORE-W1", c =>
        {
            var before = Content.TestGore.OnSpawnCount;
            var center = player.Center;
            for (var i = 0; i < 12; i++)
            {
                var angle = i / 12f * MathHelper.TwoPi;
                var dir = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
                var idx = Gore.NewGore(center + dir * 20f, dir * 4.5f + new Vector2(0f, -2.5f), type, 1.5f);
                c.True(idx >= 0, $"第 {i + 1} 粒 NewGore 返回 {idx}");
            }
            c.Eq(Content.TestGore.OnSpawnCount - before, 12, "OnSpawn 次数");
        });
    }
}
