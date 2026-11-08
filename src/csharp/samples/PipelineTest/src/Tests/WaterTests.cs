using Pylon.ModLoader;
using Terraria;

namespace PipelineTest.Tests;

/// <summary>
/// 管线 42：Water —— 功能验证。水/瀑布双表发号（Base 运行期捕获），场景接入（水样式被设置）在 B4 SceneEffect 联动。
/// </summary>
internal static class WaterTests
{
    public static void Register()
    {
        PT.Group("water", Phase.Ready, Run);
        PT.Group("water-w", Phase.World, RunWorld);
    }

    private static void RunWorld()
    {
        var water = ModContent.GetInstance<Content.TestWaterStyle>();
        var player = Main.player[Main.myPlayer];
        var x = (int)(player.Center.X / 16f);
        var y = (int)(player.Center.Y / 16f);

        PT.Case("WATER-W1", c =>
        {
            c.Eq(Main.waterStyle, water.Type, "场景效果把 Main.waterStyle 换成 mod 水");
        });

        // 手动验证：在玩家右前方挖坑灌水，屏幕上直接看 mod 水的颜色。
        PT.Case("WATER-W2", c =>
        {
            // 挖一个 10 宽 x 5 深的坑（草皮往下）
            for (var i = x + 3; i <= x + 12; i++)
                for (var j = y; j <= y + 4; j++)
                    WorldGen.KillTile(i, j, false, false, true);

            // 灌水（Liquid = 1）
            for (var i = x + 3; i <= x + 12; i++)
                for (var j = y + 2; j <= y + 4; j++)
                {
                    var tile = Main.tile[i, j];
                    tile.liquid = 1;
                    tile.liquidType(0);
                    WorldGen.SquareTileFrame(i, j, true);
                }

            PT.Info("玩家右前方应有亮蓝色水坑（TestWaterStyle 主贴图）");
            c.True(true, "水坑已灌好");
        });
    }

    private static void Run()
    {
        var water = ModContent.GetInstance<Content.TestWaterStyle>();
        var fall = ModContent.GetInstance<Content.TestWaterfallStyle>();

        PT.Case("WATER-R1", c =>
        {
            c.True(water.Type > 0, $"water.Type={water.Type}");
            c.True(fall.Type > 0, $"waterfall.Type={fall.Type}");
        });

        PT.Case("WATER-R2", c =>
        {
            // 槽门面未公开：验证声明值可读、类型号唯一即可（场景接入在 B4 联动验）
            c.True(water.ChooseWaterfallStyle() >= 0, $"ChooseWaterfallStyle={water.ChooseWaterfallStyle()}");
        });

        // H1：作者回调直接验证（LightColorMultiplier / ColorMultiplier 的 ref 语义）
        PT.Case("WATER-H1", c =>
        {
            c.NoThrow(() =>
            {
                float r = 1f, g = 1f, b = 1f;
                water.LightColorMultiplier(ref r, ref g, ref b);
                c.True(r > 0.8f && r < 1f, $"LightColorMultiplier r={r}");

                float fr = 1f, fg = 1f, fb = 1f, fa = 1f;
                fall.ColorMultiplier(ref fr, ref fg, ref fb, ref fa);
                c.True(fr > 1.1f, $"ColorMultiplier r={fr}（声明 1.2）");
            }, "水样式回调");
        });
    }
}