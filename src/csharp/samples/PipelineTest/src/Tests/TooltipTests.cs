#nullable enable
using System;
using Pylon.ModLoader;
using Terraria;

namespace PipelineTest.Tests;

/// <summary>
/// 管线 27：Tooltip（物品提示）。
/// Ready：方法面（ModItem 五个 tooltip 钩子都在基类上）。
/// World：把光标模拟到热键栏第 1 格（测试物品所在）触发真实悬停链，
/// 断言 ModifyTooltips / PreDrawTooltip / PostDrawTooltipLine 被派发；开关控制改行与删行。
/// </summary>
internal static class TooltipTests
{
    public static void Register()
    {
        PT.Group("tooltip", Phase.Ready, RunReady);
        PT.Group("tooltip-w", Phase.World, RunWorld);
    }

    private static void RunReady()
    {
        PT.Case("TIP-R1", c =>
        {
            var type = typeof(ModItem);
            c.NotNull(type.GetMethod("ModifyTooltips"), "ModItem.ModifyTooltips");
            c.NotNull(type.GetMethod("PreDrawTooltip"), "ModItem.PreDrawTooltip");
            c.NotNull(type.GetMethod("PostDrawTooltip"), "ModItem.PostDrawTooltip");
            c.NotNull(type.GetMethod("PreDrawTooltipLine"), "ModItem.PreDrawTooltipLine");
            c.NotNull(type.GetMethod("PostDrawTooltipLine"), "ModItem.PostDrawTooltipLine");
        });

        // 管线的取行链（MouseText 之外的最短验证）：RebuildTooltip 走游戏自己的重建。
        PT.Case("TIP-R2", c =>
        {
            var probe = new Item();
            probe.SetDefaults(ModContent.TypeOf<Content.TestItem>(), null);
            probe.RebuildTooltip();
            c.True(probe.ToolTip.Lines >= 2, $"RebuildTooltip 行数={probe.ToolTip.Lines}（声明 1 + 钩子加 1）");
        });
    }

    private static void RunWorld()
    {
        var player = Main.player[Main.myPlayer];
        var held = player?.inventory is { Length: > 0 } inv && inv[0] is { } slot && slot.type == ModContent.TypeOf<Content.TestItem>();
        if (player is null || !player.active || !held)
        {
            PT.SkipCase("TIP-W1", "热键栏第 1 格不是测试物品");
            return;
        }

        Content.TestItem.ResetTooltipProbe();

        // W1：开光标扫描（PostUpdateEverything 每帧扫热键栏第 1 格）+ 改行加行开关。
        PT.Later("TIP-W1", 10, c =>
        {
            PipelineTestMod.TooltipSweep = true;
            Content.TestItem.MutateTooltip = true;
            PT.Info("光标扫描 + 改行加行已开（约 3 秒后断言）");
        });

        // W2：改行加行下五个钩子都应派发；屏幕左上 tooltip 肉眼可见 [管线改写]/[管线加行]。
        PT.Later("TIP-W2", 220, c =>
        {
            c.True(Content.TestItem.CtModifyTooltips > 0, $"ModifyTooltips 派发（{Content.TestItem.CtModifyTooltips} 次，行数 {Content.TestItem.LastLineCount}）");
            c.True(Content.TestItem.CtPreDrawTooltip > 0, $"PreDrawTooltip 派发（{Content.TestItem.CtPreDrawTooltip} 次）");
            c.True(Content.TestItem.CtPreDrawTooltipLine > 0, $"PreDrawTooltipLine 派发（{Content.TestItem.CtPreDrawTooltipLine} 次）");
            c.True(Content.TestItem.CtPostDrawTooltip > 0, $"PostDrawTooltip 派发（{Content.TestItem.CtPostDrawTooltip} 次）");
            c.True(Content.TestItem.CtPostDrawTooltipLine > 0, $"PostDrawTooltipLine 派发（{Content.TestItem.CtPostDrawTooltipLine} 次）");
            PT.Info("肉眼验收：tooltip 应含 [管线改写]/[管线加行]");
            Content.TestItem.MutateTooltip = false;
            Content.TestItem.MutatedLineCount = Content.TestItem.LastLineCount;
            Content.TestItem.RemoveTooltipLine = true;
        });

        // W3：删行开关下行数应下降（ModifyTooltips 继续每帧派发）。
        PT.Later("TIP-W3", 420, c =>
        {
            c.True(Content.TestItem.CtModifyTooltips > 0, $"删行后 ModifyTooltips 仍派发（{Content.TestItem.CtModifyTooltips} 次）");
            c.True(Content.TestItem.LastLineCount < Content.TestItem.MutatedLineCount, $"删行后行数 {Content.TestItem.LastLineCount} < 改行时 {Content.TestItem.MutatedLineCount}");
            Content.TestItem.RemoveTooltipLine = false;
            PipelineTestMod.TooltipSweep = false;
            Main.mouseLeft = false;
        });
    }
}
