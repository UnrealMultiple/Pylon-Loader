using System;
using System.Collections.Generic;
using PipelineTest.Content;
using Pylon.ModLoader;
using Terraria;

namespace PipelineTest.Tests;

/// <summary>
/// 额外跳跃管线：排序安插、mod 跳跃起跳/结束、原版跳跃的镜像与八钩子、状态往返、清空。
/// 用例直接驱动 Player 的跳跃方法（一个用例一段节奏）。
/// </summary>
internal static class JumpTests
{
    public static void Register() => PT.Group("jump-w", Phase.World, Run);

    private static void Press(Player p)
    {
        p.jump = 0;
        var v = p.velocity;
        v.Y = 1f;
        p.velocity = v;
        p.controlJump = true;
        p.releaseJump = true;
        p.sliding = false;
    }

    private static void NoVanilla(Player p)
    {
        p.hasJumpOption_Cloud = p.canJumpAgain_Cloud = p.isPerformingJump_Cloud = false;
        p.hasJumpOption_Sandstorm = p.canJumpAgain_Sandstorm = p.isPerformingJump_Sandstorm = false;
        p.hasJumpOption_Blizzard = p.canJumpAgain_Blizzard = p.isPerformingJump_Blizzard = false;
        p.hasJumpOption_Fart = p.canJumpAgain_Fart = p.isPerformingJump_Fart = false;
        p.hasJumpOption_Sail = p.canJumpAgain_Sail = p.isPerformingJump_Sail = false;
    }

    private static void Run()
    {
        var player = Main.LocalPlayer;
        var savedSpeed = player.maxRunSpeed;
        JumpProbe.Reset();
        JumpProbe.Active = true;
        try
        {
            Order(player);
            ModJump(player);
            Vanilla(player);
            States(player);
            Cleared(player);
        }
        finally
        {
            player.maxRunSpeed = savedSpeed;
            JumpProbe.Active = false;
            JumpProbe.Reset();
        }
    }

    private static void Order(Player p)
    {
        PT.Case("JUMP-1", c =>
        {
            var names = new List<string>();
            foreach (var jump in ExtraJumpTable.OrderedJumps) names.Add(jump.Name);
            var test = names.IndexOf("TestJump");
            var cloud = names.IndexOf(nameof(CloudInABottleJump));
            c.Eq(ExtraJumpTable.ExtraJumpCount, 11, "内置 10 + mod 1");
            c.Eq(test, cloud + 1, "TestJump 安插在原版瓶类之后");
        });
    }

    private static void ModJump(Player p)
    {
        PT.Case("JUMP-2", c =>
        {
            NoVanilla(p);
            ExtraJumpTable.GetJumpState<TestJump>(p).Enable();
            p.RefreshDoubleJumps();
            c.Eq(JumpProbe.Count("OnRefreshed"), 1, "回充走内容类 OnRefreshed");
            c.True(JumpProbe.Logged("OnExtraJumpRefreshed:TestJump"), "OnExtraJumpRefreshed 链");
            c.True(ExtraJumpTable.GetJumpState<TestJump>(p).Available, "回充后可用");

            JumpProbe.TestFactor = 1.5f;
            Press(p);
            p.JumpMovement();
            JumpProbe.TestFactor = 1f;
            var state = ExtraJumpTable.GetJumpState<TestJump>(p);
            c.True(state.Active && !state.Available, "起跳后 Active 且消耗掉可用");
            c.Eq(JumpProbe.Count("OnStarted"), 1, "OnStarted 一次");
            c.True(JumpProbe.Logged("OnExtraJumpStarted:TestJump") && JumpProbe.Logged("CanStartExtraJump:TestJump"),
                "OnExtraJumpStarted/CanStartExtraJump 链");
            var expected = (int)(Player.jumpHeight * 2f * 1.5f);
            c.True(p.jump == expected || (p.jump == expected - 1 && p.velocity.Y < 0),
                $"跳帧=倍率×jumpHeight jump={p.jump} 期望={expected}");

            p.HorizontalMovement();
            c.True(JumpProbe.Count("UpdateHorizontalSpeeds") >= 1, "跳跃中跑水平速度修正");

            p.DoubleJumpVisuals();
            c.Eq(JumpProbe.Count("ShowVisuals"), 1, "ShowVisuals 一次");
            c.True(JumpProbe.Logged("ExtraJumpVisuals:TestJump"), "ExtraJumpVisuals 链");

            p.jump = 0;
            p.CancelAllJumpVisualEffects(false);
            var after = ExtraJumpTable.GetJumpState<TestJump>(p);
            c.True(!after.Active, "取消后不再 Active");
            c.Eq(JumpProbe.Count("OnEnded"), 1, "OnEnded 一次");
            c.True(JumpProbe.Logged("OnExtraJumpEnded:TestJump"), "OnExtraJumpEnded 链");
        });
    }

    private static void Vanilla(Player p)
    {
        PT.Case("JUMP-3", c =>
        {
            NoVanilla(p);
            p.hasJumpOption_Cloud = p.canJumpAgain_Cloud = true;
            Press(p);
            p.JumpMovement();
            c.True(p.isPerformingJump_Cloud && !p.canJumpAgain_Cloud, "原版云瓶起跳");
            c.True(JumpProbe.Logged("OnExtraJumpStarted:CloudInABottleJump"), "原版跳跃也发 OnExtraJumpStarted");
            p.jump = 0;
            p.CancelAllJumpVisualEffects(false);
            c.True(!p.isPerformingJump_Cloud && JumpProbe.Logged("OnExtraJumpEnded:CloudInABottleJump"),
                "原版跳跃结束也发链");

            JumpProbe.VetoCloud = true;
            p.canJumpAgain_Cloud = true;
            Press(p);
            p.JumpMovement();
            JumpProbe.VetoCloud = false;
            c.True(p.canJumpAgain_Cloud && !p.isPerformingJump_Cloud && JumpProbe.Logged("CanStartExtraJump:CloudInABottleJump"),
                "CanStartExtraJump 否决后云瓶保留");

            JumpProbe.CloudFactor = 2f;
            Press(p);
            p.JumpMovement();
            JumpProbe.CloudFactor = 1f;
            var expected = (int)(Player.jumpHeight * 0.75f * 2f);
            c.True(p.isPerformingJump_Cloud && !p.canJumpAgain_Cloud, "云瓶起跳（挂钩子后）");
            c.True(p.jump == expected || p.jump == expected - 1,
                $"ModifyExtraJumpDurationMultiplier 改原版跳帧 jump={p.jump} 期望={expected}");

            p.jump = 0;
            p.CancelAllJumpVisualEffects(false);
        });
    }

    private static void States(Player p)
    {
        PT.Case("JUMP-4", c =>
        {
            NoVanilla(p);
            ExtraJumpTable.GetJumpState(p, ExtraJump.FartInAJar).Enable();
            p.UpdateJumpHeight();
            var fartEnabled = p.hasJumpOption_Fart;
            p.hasJumpOption_Sandstorm = true;
            var sandstormSeen = ExtraJumpTable.GetJumpState(p, ExtraJump.SandstormInABottle).Enabled;
            ExtraJumpTable.GetJumpState(p, ExtraJump.SandstormInABottle).Disable();
            p.UpdateJumpHeight();
            var sandstormDisabled = !p.hasJumpOption_Sandstorm;
            c.True(fartEnabled && sandstormSeen && sandstormDisabled,
                $"状态往返 fart={fartEnabled} seen={sandstormSeen} disabled={sandstormDisabled}");
        });
    }

    private static void Cleared(Player p)
    {
        PT.Case("JUMP-5", c =>
        {
            NoVanilla(p);
            ref var test = ref ExtraJumpTable.GetJumpState<TestJump>(p);
            test.Enable();
            test.Available = true;
            p.hasJumpOption_Cloud = p.canJumpAgain_Cloud = true;
            var usable = ExtraJumpTable.AnyExtraJumpUsable(p);
            ExtraJumpTable.ConsumeAllExtraJumps(p);
            c.True(usable, "清空前可用");
            c.True(!ExtraJumpTable.AnyExtraJumpUsable(p) && !p.canJumpAgain_Cloud, "清空后全部耗尽");
            c.True(JumpProbe.Logged("OnExtraJumpCleared:TestJump") && JumpProbe.Logged("OnExtraJumpCleared:CloudInABottleJump"),
                "OnExtraJumpCleared 链（mod 与原版都发）");
        });
    }
}
