using System;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria;
using Terraria.ID;

namespace PipelineTest.Tests;

/// <summary>
/// 管线 26：BossBar。
/// Ready：内容/实例/关联。
/// World：刷测试 Boss，等 BigProgressBarSystem 找到它，验证 Collected → ModifyInfo → GetIconTexture → PreDraw/PostDraw、
/// GUIBossBar.Draw、GlobalBossBar、ModifyInfo false 隐藏。
/// </summary>
internal static class BossBarTests
{
    private static int _boss = -1;
    private static bool _hooked;

    public static void Register()
    {
        PT.Group("bossbar", Phase.Ready, RunReady);
        PT.Group("bossbar-w", Phase.World, RunWorld);
    }

    private static void RunReady()
    {
        PT.Case("BOSSBAR-R1", c =>
        {
            var bar = ModContent.GetInstance<Content.TestBossBar>();
            c.NotNull(bar, "TestBossBar 实例");
            c.NotNull(ModContent.GetInstance<Content.TestGlobalBossBar>(), "TestGlobalBossBar 实例");

            var npc = ModContent.NpcType<Content.TestBossNpc>();
            c.True(npc >= NPCID.Count, $"TestBossNpc type={npc}");

            var inst = ModContent.GetInstance<Content.TestBossNpc>();
            c.True(ReferenceEquals(inst?.BossBar, bar), "TestBossNpc.BossBar 关联");
        });
    }

    private static void RunWorld()
    {
        var player = Main.player[Main.myPlayer];
        if (player is null || !player.active)
        {
            PT.SkipCase("BOSSBAR-W1", "玩家还没就绪");
            return;
        }

        PT.Case("BOSSBAR-W1", c =>
        {
            if (!_hooked)
            {
                On.GUIBossBar.Draw += DrawHook;
                _hooked = true;
            }

            _boss = NPC.NewNPC(player.GetProjectileSource_Misc(0),
                (int)player.Center.X + 100, (int)player.Center.Y - 40,
                ModContent.NpcType<Content.TestBossNpc>(), 0, 0f, 0f, 0f, 0f, 0);
            c.True(_boss >= 0, $"刷出测试 Boss index={_boss}");
        });

        PT.Later("BOSSBAR-W2", 120, c =>
        {
            if (_boss < 0 || _boss >= Main.npc.Length)
            {
                c.Fail("没有测试 Boss");
                return;
            }

            var npc = Main.npc[_boss];
            c.True(npc.active, "Boss active");
            c.Eq((int)Content.TestBossBar.CollectedLifeMax, 5000, "Collected.LifeMax");
            c.Eq((int)Content.TestBossBar.CollectedLife, 5000, "Collected.Life");
            c.True(Content.TestBossBar.CollectedCalls > 0, "Collected 调用");
            c.True(Content.TestBossBar.IconCalls > 0, "GetIconTexture 调用");
            c.True(Content.TestBossBar.InfoCalls > 0, "ModifyInfo 调用");
            c.True(Content.TestBossBar.PreCalls > 0, "PreDraw 调用");
            c.True(Content.TestBossBar.PostCalls > 0, "PostDraw 调用");
            c.Eq(Content.TestBossBar.LastFrame.Width, 16, "IconFrame.Width");
            c.Eq(Content.TestBossBar.LastFrame.Height, 16, "IconFrame.Height");
            c.Eq((int)Content.TestBossBar.IconSeenLife, (int)Content.TestBossBar.CollectedLife, "GetIconTexture 时已 Collected");
            c.True(Content.TestGlobalBossBar.PreCalls > 0, "GlobalBossBar.PreDraw");
            c.True(Content.TestGlobalBossBar.PostCalls > 0, "GlobalBossBar.PostDraw");
            c.True(Content.TestBossBar.DrawCalls > 0, "GUIBossBar.Draw");
        });

        PT.Later("BOSSBAR-W3", 180, c =>
        {
            var before = Content.TestBossBar.PreCalls;
            Content.TestBossBar.Hide = true;
            PT.Later("BOSSBAR-W4", 30, cc =>
            {
                cc.Eq(Content.TestBossBar.PreCalls, before, "ModifyInfo false 后不再 PreDraw");
                Content.TestBossBar.Hide = false;
                PT.Info("血条隐藏验证结束，已恢复显示；测试 Boss 保留在场上供肉眼确认");
            });
        });
    }

    private static void DrawHook(On.GUIBossBar.orig_Draw orig, GUIBossBar self, float life, float lifeMax,
                                 Texture2D icon, Il2CppMicrosoft.Xna.Framework.Rectangle frame,
                                 float shield, float shieldMax)
    {
        if (lifeMax >= 4999f && lifeMax <= 5001f) Content.TestBossBar.DrawCalls++;
        orig(self, life, lifeMax, icon, frame, shield, shieldMax);
    }
}
