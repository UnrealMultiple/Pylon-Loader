using System.Linq;
using Pylon.ModLoader;
using PipelineTest.Content;
using Terraria;
using PlayerDrawLayers = Pylon.ModLoader.PlayerDrawLayers;

namespace PipelineTest.Tests;

/// <summary>
/// </summary>
internal static class PlayerDrawLayerTests
{
    public static void Register()
    {
        PT.Group("playerdrawlayer", Phase.Ready, RunReady);
        PT.Group("playerdrawlayer-w", Phase.World, RunWorld);
        PT.Group("playerdrawlayer-vis", Phase.World, RunVisuals);
    }

    private static int IndexOf(PlayerDrawLayer layer) => PlayerDrawLayerLoader.DrawOrder.ToList().IndexOf(layer);

    private static void RunReady()
    {
        PT.Case("PDLAYER-R1", c =>
        {
            var between = ModContent.GetInstance<TestLayerBetween>();
            var torso = PlayerDrawLayers.Torso;
            var offhand = PlayerDrawLayers.OffhandAcc;

            var at = IndexOf(between);
            c.True(at > IndexOf(torso) && at < IndexOf(offhand),
                $"Between 层夹在 Torso 与 OffhandAcc 之间（{IndexOf(torso)} < {at} < {IndexOf(offhand)}）");
        });

        PT.Case("PDLAYER-R2", c =>
        {
            var after = ModContent.GetInstance<TestLayerChildAfter>();
            var before = ModContent.GetInstance<TestLayerChildBefore>();

            c.True(PlayerDrawLayers.Torso.ChildrenAfter.Contains(after), "AfterParent 挂进父层的后子层");
            c.True(PlayerDrawLayers.Torso.ChildrenBefore.Contains(before), "BeforeParent 挂进父层的前子层");
        });

        PT.Case("PDLAYER-R3", c =>
        {
            c.True(PlayerDrawLayerLoader.DrawOrder.Count >= PlayerDrawLayers.VanillaLayers.Count,
                $"排程里含全部原版层（{PlayerDrawLayerLoader.DrawOrder.Count} ≥ {PlayerDrawLayers.VanillaLayers.Count}）");
            c.Eq(IndexOf(PlayerDrawLayers.FirstVanillaLayer), 0, "第一个原版层排在 0");
        });

        PT.Case("PDLAYER-R4", c =>
        {
            var multiple = ModContent.GetInstance<TestLayerMultiple>();
            c.True(PlayerDrawLayerLoader.Layers.Contains(multiple), "多槽层也进了排程");
        });
    }

    private static void RunWorld()
    {
        PlayerDrawProbe.Reset();
        PlayerDrawProbe.ShowVisuals = false;
        PlayerDrawProbe.HideTarget = true; // W2 验的就是"在 HideDrawLayers 里 Hide 掉"

        PT.Later("PDLAYER-W1", 5, c =>
        {
            c.True(PlayerDrawProbe.BetweenCount > 0, $"Between 层每帧都画（{PlayerDrawProbe.BetweenCount} 次）");
            c.True(PlayerDrawProbe.ChildAfterCount > 0 && PlayerDrawProbe.ChildBeforeCount > 0,
                $"父子层跟着父层画（前 {PlayerDrawProbe.ChildBeforeCount} / 后 {PlayerDrawProbe.ChildAfterCount}）");
        });

        PT.Later("PDLAYER-W2", 8, c =>
        {
            c.Eq(PlayerDrawProbe.HiddenCount, 0, "HideDrawLayers 里 Hide 掉的层不画");
            PlayerDrawProbe.HideTarget = false;
        });

        PT.Later("PDLAYER-W3", 11, c =>
        {
            c.True(PlayerDrawProbe.HiddenCount > 0, $"不藏之后开始画（{PlayerDrawProbe.HiddenCount} 次）");
            c.True(PlayerDrawProbe.MultipleCount > 0, $"多槽层按条件画（{PlayerDrawProbe.MultipleCount} 次）");
        });

        PT.Later("PDLAYER-W4", 14, c =>
        {
            PlayerDrawProbe.Reset();
            PlayerDrawProbe.ShowVisuals = true;
        });

        PT.Later("PDLAYER-W5", 18, c =>
        {
            c.True(PlayerDrawProbe.AddOk, "AddDrawData 返回 true");
            c.True(PlayerDrawProbe.CacheBefore > 0, $"塞之前缓存里已有原版数据（{PlayerDrawProbe.CacheBefore} 条）");
            c.Eq(PlayerDrawProbe.CacheAfter, PlayerDrawProbe.CacheBefore + 1, "塞一条只长一格");
            c.True(PlayerDrawProbe.TextureWidth > 0, $"贴图来自游戏资产（宽 {PlayerDrawProbe.TextureWidth}）");
            PlayerDrawProbe.ShowVisuals = false;
        });
    }

    /// <summary>只看画面用：把剑画在身上。</summary>
    private static void RunVisuals()
    {
        PlayerDrawProbe.ShowVisuals = true;
        PT.Info("绘制缓存里塞一把剑（躯干与副手之间），15 秒后关掉");

        PT.Later("PDLAYER-V1", 5, c =>
        {
            c.True(PlayerDrawProbe.Added > 0, $"已塞入 {PlayerDrawProbe.Added} 条绘制数据");
        });

        PT.Later("PDLAYER-V2", 900, c =>
        {
            PlayerDrawProbe.ShowVisuals = false;
            c.True(PlayerDrawProbe.Added > 0, $"累计塞入 {PlayerDrawProbe.Added} 条");
        });
    }
}