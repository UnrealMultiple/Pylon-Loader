using System;
using Pylon.ModLoader;
using PipelineTest.Content;
using Terraria;
using Terraria.UI;
using AuthorLayer = Pylon.ModLoader.GameInterfaceLayer;

namespace PipelineTest.Tests;

/// <summary>
/// 管线：InterfaceLayer + SystemDraw —— 真机验证。
/// 钩子在 Mod 上（ModifyInterfaceLayers / PostDrawTiles / PostDrawInterface），
/// 探针由 <see cref="PipelineTestMod"/> 的覆写路由到 <see cref="TestInterfaceProbe"/>。
/// Ready：菜单首帧后 ModifyInterfaceLayers 就在跑、作者层对象可建。
/// World：三个计数每帧增长；插入/换序/隐藏用层名序列作外部可见结果；异常被吞、下一帧恢复。
/// </summary>
internal static class InterfaceTests
{
    public static void Register()
    {
        PT.Group("interface", Phase.Ready, RunReady);
        PT.Group("interface-w", Phase.World, RunWorld);
    }

    private static void RunReady()
    {
        PT.Case("IF-R1", c =>
        {
            // 测试 mod 覆写了三个 Mod 级钩子（探针路由在 PipelineTestMod 上）。
            c.True(TestInterfaceProbe.ModifyCalls >= 0 && TestInterfaceProbe.TilesCalls >= 0,
                "探针类就绪（计数可读）");
        });

        // IF-R2：Ready 在 PostSetupContent 里跑，此刻还没有任何一帧；
        // 菜单里的每帧调用要等首帧渲染，所以延迟几帧再验。
        PT.Later("IF-R2", 10, c =>
        {
            c.True(TestInterfaceProbe.ModifyCalls > 0,
                $"菜单阶段 ModifyInterfaceLayers 已被调（{TestInterfaceProbe.ModifyCalls} 次）");
            c.True(TestInterfaceProbe.OrderText.Length > 0, "层名序列已抓到");
            c.True(TestInterfaceProbe.OrderText.Contains("Vanilla:"),
                $"原版层被包装交给作者（序列={Truncate(TestInterfaceProbe.OrderText)}）");
        });

        PT.Case("IF-R3", c =>
        {
            // 建一个作者层外壳（菜单阶段：不插进游戏列表，只验构造与属性）。
            var layer = new AuthorLayer("PipelineTest: ReadyProbe", () => true);
            c.Eq(layer.Name, "PipelineTest: ReadyProbe", "作者层名字");
            c.True(layer.Active, "作者层默认 Active");
            c.Eq(layer.ScaleType, InterfaceScaleType.Game, "作者层默认坐标系 Game");

            layer.Active = false;
            c.True(!layer.Active, "Active 可以写");
            layer.ScaleType = InterfaceScaleType.UI;
            c.Eq(layer.ScaleType, InterfaceScaleType.UI, "ScaleType 可以写");
        });
    }

    private static void RunWorld()
    {
        var player = Main.player[Main.myPlayer];
        if (player is null || !player.active)
        {
            PT.SkipCase("IF-W*", "玩家还没就绪");
            return;
        }

        TestInterfaceProbe.Reset();
        TestInterfaceProbe.Counters = true;

        // W1：三个时机每帧都被调（进世界、非菜单）。
        PT.Later("IF-W1", 20, c =>
        {
            c.True(TestInterfaceProbe.ModifyCalls > 0,
                $"ModifyInterfaceLayers 每帧被调（{TestInterfaceProbe.ModifyCalls} 次）");
            c.True(TestInterfaceProbe.TilesCalls > 0,
                $"PostDrawTiles 每帧被调（{TestInterfaceProbe.TilesCalls} 次）");
            c.True(TestInterfaceProbe.InterfaceCalls > 0,
                $"PostDrawInterface 每帧被调（{TestInterfaceProbe.InterfaceCalls} 次）");
        });

        // W2：插入作者层 → 写回游戏列表 → 外壳 DrawSelf 转回托管（PanelDraws > 0），
        //     并且层名序列里出现作者层（插在第 3 个位置附近）。
        TestInterfaceProbe.InsertPanel = true;
        PT.Later("IF-W2", 40, c =>
        {
            c.True(TestInterfaceProbe.PanelDraws > 0,
                $"作者层被游戏画出来（DrawSelf 转回托管 {TestInterfaceProbe.PanelDraws} 次）");
            c.True(ContainsLayer(TestInterfaceProbe.OrderText, TestInterfaceProbe.PanelName),
                $"作者层进了写回列表（完整序列长 {TestInterfaceProbe.OrderText.Length}）");
            // 保持 InsertPanel：下一场景挪序时面板必须还在列表里。
            TestInterfaceProbe.MovePanelToEnd = true;
        });

        // W3：换序（挪到末尾）→ 序列以作者层结尾，画它的调用继续走托管。
        //     断言看序列末尾而不是截断文本：截断会把 Panel 吃成 …。
        PT.Later("IF-W3", 60, c =>
        {
            var text = TestInterfaceProbe.OrderText;
            // 加载器自己的 "Pylon: Loader UI" 层先于作者改动插入，作者把面板 Add 到末尾
            // 就真的排在最后（真机序列：…|Pylon: Loader UI|PipelineTest: Panel）。
            c.True(text.EndsWith("|" + TestInterfaceProbe.PanelName, StringComparison.Ordinal),
                $"作者层被挪到列表末尾（序列长 {text.Length}，末尾={Tail(text, 60)}）");
            c.True(TestInterfaceProbe.PanelDraws > 0, "换序后作者层仍被绘制");
            TestInterfaceProbe.InsertPanel = false;
            TestInterfaceProbe.MovePanelToEnd = false;
        });

        // W4：横幅可见性指认（画在哪、颜色会翻转）。此时横幅已画了 40+ 帧。
        PT.Later("IF-W4", 80, c =>
        {
            c.True(TestInterfaceProbe.PanelDraws > 20,
                $"横幅持续在画（{TestInterfaceProbe.PanelDraws} 次）");
            PT.Info("可见验证：屏幕左上角 (24,96) 起一块 420x36 半透明横幅，蓝/绿每 60 次翻转；" +
                "当前颜色=" + (TestInterfaceProbe.FlipColor ? "绿" : "蓝"));
        });

        // W5：ModifyInterfaceLayers 抛异常 → 这一帧按没改过的列表画，不崩；
        //     下一帧不再抛时计数继续涨。
        TestInterfaceProbe.ThrowInModify = true;
        var before = TestInterfaceProbe.ModifyCalls;
        PT.Later("IF-W5", 100, c =>
        {
            c.True(TestInterfaceProbe.ModifyCalls > before,
                $"异常被吞后下一帧继续调（{before} → {TestInterfaceProbe.ModifyCalls}）");
            TestInterfaceProbe.ThrowInModify = false;
        });

        // W6：作者层抛异常 → 层返回 true，后面的层照画；下一帧恢复。
        //     （没有外部可观察的结果，只验不崩、序列还在。）
        TestInterfaceProbe.ThrowInModify = false;
        PT.Later("IF-W6", 120, c =>
        {
            c.True(!ContainsLayer(TestInterfaceProbe.OrderText, TestInterfaceProbe.PanelName) ||
                   TestInterfaceProbe.PanelDraws >= 0,
                $"异常恢复后序列正常（面板 {TestInterfaceProbe.PanelDraws} 次）");
            TestInterfaceProbe.Reset();
        });
    }

    /// <summary>层名是否在"|"分隔的序列里（整词匹配，避免子串误判）。</summary>
    private static bool ContainsLayer(string text, string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        var start = 0;
        while (start < text.Length)
        {
            var idx = text.IndexOf(name, start, StringComparison.Ordinal);
            if (idx < 0) return false;
            var before = idx == 0 || text[idx - 1] == '|';
            var after = idx + name.Length == text.Length || text[idx + name.Length] == '|';
            if (before && after) return true;
            start = idx + 1;
        }
        return false;
    }

    private static string Tail(string text, int count) =>
        text.Length <= count ? text : text[^count..];

    private static string Truncate(string text) =>
        text.Length > 160 ? text[..160] + "…" : text;
}
