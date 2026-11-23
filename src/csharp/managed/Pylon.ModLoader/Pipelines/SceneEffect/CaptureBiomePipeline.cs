using Pylon.ModLoader.Content;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Terraria;
using Terraria.Graphics.Capture;

namespace Pylon.ModLoader.Pipelines;
/// <summary>
/// 截屏生物群系：游戏拍图 / 全屏地图问"这里是哪个生物群系"时，如果这一帧的场景赢家把水或表层背景
/// 换成了 mod 槽位（原版编号表装不下），而原版自己没有给出群系，就自己造一份 CaptureBiome 回过去——
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
        FromScene(orig(), static scene => scene.WaterStyle.Value >= Main.maxLiquidTypes);

    private static CaptureBiome BiomeByLocation(On.Terraria.Graphics.Capture.CaptureBiome.orig_GetBiomeByLocation orig) =>
        FromScene(orig(), static scene => scene.SurfaceBackground.Value >= BackgroundTable.Base);

    private static CaptureBiome FromScene(CaptureBiome vanilla, Func<SceneEffectInstance, bool> modded)
    {
        if (vanilla.Pointer != nint.Zero || Main.gameMenu) return vanilla;

        try
        {
            var scene = SceneEffectPipeline.Scene;
            if (modded(scene))
                return new CaptureBiome(scene.SurfaceBackground.Value, scene.WaterStyle.Value, scene.TileColorStyle);
        }
        catch (Exception e)
        {
            ContentErrors.Line("[capturebiome] 场景赢家换群系异常：" + Safe.Dump(e));
        }

        return vanilla;
    }
}
