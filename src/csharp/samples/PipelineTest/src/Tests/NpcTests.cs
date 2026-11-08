using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;

namespace PipelineTest.Tests;

/// <summary>
/// 管线 07：Npc —— 注册面 + 功能面。
/// Ready：发号 / SetDefaults 落位 / 贴图进 TextureAssets.Npc。
/// World：在玩家身边刷两只测试史莱姆（会朝玩家跳），并验证 OnSpawn。
/// </summary>
internal static class NpcTests
{
    public static void Register()
    {
        PT.Group("npc", Phase.Ready, RunReady);
        PT.Group("npc-w", Phase.World, RunWorld);
    }

    private static void RunReady()
    {
        var type = ModContent.NpcType<Content.TestNpc>();

        PT.Case("NPC-R1", c =>
        {
            c.True(type >= NPCID.Count, $"type={type}（原版 {NPCID.Count}）");
            c.NotNull(ModContent.GetInstance<Content.TestNpc>(), "GetInstance");
        });

        PT.Case("NPC-T1", c =>
        {
            var table = TextureAssets.Npc;
            var len = table?.Length ?? 0;
            c.True(len > type, $"NPC 贴图表长={len}（应 > {type}）");
            if (len > type) c.NotNull(table![type], $"[{type}] 槽位");
        });
    }

    private static void RunWorld()
    {
        var type = ModContent.NpcType<Content.TestNpc>();
        var player = Main.player[Main.myPlayer];
        if (player is null || !player.active)
        {
            PT.SkipCase("NPC-W1", "玩家还没就绪");
            return;
        }

        PT.Case("NPC-W1", c =>
        {
            var before = Content.TestNpc.OnSpawnCount;
            var source = player.GetProjectileSource_Misc(0);
            var center = player.Center;

            NpcContactTest.BeforeLife = player.statLife;

            var first = NPC.NewNPC(source, (int)center.X - 120, (int)center.Y - 40, type, 0, 0f, 0f, 0f, 0f, 0);
            var second = NPC.NewNPC(source, (int)center.X + 120, (int)center.Y - 40, type, 0, 0f, 0f, 0f, 0f, 0);
            // 第三只直接叠在玩家身上，保证一定会碰撞（验证接触伤害链）。
            var third = NPC.NewNPC(source, (int)center.X, (int)center.Y, type, 0, 0f, 0f, 0f, 0f, 0);
            c.True(third >= 0, $"重叠那只 NewNPC 返回 {third}");
            if (third >= 0)
            {
                var n = Main.npc[third];
                n.noGravity = true;
                n.velocity = Vector2.Zero;
            }

            c.True(first >= 0, $"第 1 只 NewNPC 返回 {first}");
            c.True(second >= 0, $"第 2 只 NewNPC 返回 {second}");
            c.Eq(Content.TestNpc.OnSpawnCount - before, 3, "OnSpawn 次数");

            if (first >= 0)
            {
                var npc = Main.npc[first];
                c.True(npc.active, "NPC active");
                c.Eq(npc.aiStyle, 1, "aiStyle");
                // 旅程模式会把血量/伤害按难度缩放，这里只要求落在合理区间。
                c.InRange(npc.lifeMax, 1, 120, "lifeMax");
                c.InRange(npc.damage, 0, 8, "damage");

                PT.Info($"测试史莱姆：life={npc.life}/{npc.lifeMax} damage={npc.damage} friendly={npc.friendly} " +
                        $"alpha={npc.alpha} frame={npc.frame} size={npc.width}x{npc.height} aiStyle={npc.aiStyle}");
            }
        });
    }
}
