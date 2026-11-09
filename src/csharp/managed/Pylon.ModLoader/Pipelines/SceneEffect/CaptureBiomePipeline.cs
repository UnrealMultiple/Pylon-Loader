using Terraria;
using Terraria.Graphics.Capture;

namespace Pylon.ModLoader;

/// <summary>
/// 截屏生物群系：游戏拍图 / 全屏地图问"这里是哪个生物群系"时，如果场景赢家把水或表层背景
/// 换成了 mod 槽位（原版编号表装不下），就自己造一份 CaptureBiome 回过去——
/// 否则图上要么用原版的水色，要么直接画不出来。
/// </summary>
internal static class CaptureBiomePipeline
{
    private static IModLog Log => LoaderHost.Current.Log;

    internal static void Install()
    {
        On.Terraria.Graphics.Capture.CaptureBiome.GetBiomeByWater += BiomeByWater;
        On.Terraria.Graphics.Capture.CaptureBiome.GetBiomeByLocation += BiomeByLocation;

        Log.Info("[capturebiome] 已挂（拍图与全屏地图的群系随场景赢家换 mod 水与背景）");
    }

    private static CaptureBiome BiomeByWater(On.Terraria.Graphics.Capture.CaptureBiome.orig_GetBiomeByWater orig) =>
        FromScene(orig(), static scene => scene.Water > 0);

    private static CaptureBiome BiomeByLocation(On.Terraria.Graphics.Capture.CaptureBiome.orig_GetBiomeByLocation orig) =>
        FromScene(orig(), static scene => scene.Surface >= BackgroundTable.Base);

    private static CaptureBiome FromScene(CaptureBiome vanilla, Func<(int Water, int Surface), bool> modded)
    {
        // 原版有话说就不抢；菜单里没有场景可言。
        if (Main.gameMenu || Main.LocalPlayer is not { } player)
            return vanilla;

        try
        {
            var effect = SceneEffectPipeline.Current;
            if (effect is null || !SceneEffectPipeline.AllowsVisuals(effect)) return vanilla;

            var water = -1;
            var surface = -1;

            if (effect.WaterStyle is { } w && w.Registered) water = w.Type;
            if (effect.SurfaceBackgroundStyle is { } s && s.Registered) surface = s.Slot;

            if (!modded((water, surface))) return vanilla;

            // 拍图的群系对象是一次性的：走原生静态构造，编号超出原版表也没关系（拍图只取水色与背景值）。
            var biome = new CaptureBiome(surface, water, CaptureBiome.TileColorStyle.Normal);
            return biome;
        }
        catch (Exception e)
        {
            ContentErrors.Report("[capturebiome] 场景赢家换群系异常：" + Safe.Describe(e));
            return vanilla;
        }
    }
}
