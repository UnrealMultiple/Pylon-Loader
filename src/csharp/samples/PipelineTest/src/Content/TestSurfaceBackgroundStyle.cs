using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;

namespace PipelineTest.Content;

/// <summary>
/// <c>BackgroundTextureLoader.GetBackgroundSlot(Mod, "Backgrounds/...")</c> 拿槽位，远/中/近三层。
/// 计数用来断言 DrawBackground 真的走进了这三层。
/// </summary>
public sealed class TestSurfaceBackgroundStyle : ModSurfaceBackgroundStyle
{
    public static int CtFar, CtMiddle, CtClose, CtPreDrawClose, CtFades;
    public static float LastParallax;

    private static int _midFrameCounter;
    private static int _midFrame;

    public override void ModifyFarFades(float[] fades, float transitionSpeed)
    {
        CtFades++;

        try { base.ModifyFarFades(fades, transitionSpeed); }
        catch { /* 探针只验证派发；引擎侧数组写入失败由管线记日志 */ }
    }

    public override int ChooseFarTexture()
    {
        CtFar++;
        return BackgroundTextureLoader.GetBackgroundSlot(Mod, "assets/backgrounds/ExampleBiomeSurfaceFar");
    }

    public override int ChooseMiddleTexture()
    {
        CtMiddle++;

        if (++_midFrameCounter > 12)
        {
            _midFrame = (_midFrame + 1) % 4;
            _midFrameCounter = 0;
        }

        return _midFrame switch
        {
            0 => BackgroundTextureLoader.GetBackgroundSlot(Mod, "assets/backgrounds/ExampleBiomeSurfaceMid0"),
            1 => BackgroundTextureLoader.GetBackgroundSlot(Mod, "assets/backgrounds/ExampleBiomeSurfaceMid1"),
            2 => BackgroundTextureLoader.GetBackgroundSlot(Mod, "assets/backgrounds/ExampleBiomeSurfaceMid2"),
            _ => BackgroundTextureLoader.GetBackgroundSlot(Mod, "assets/backgrounds/ExampleBiomeSurfaceMid3"),
        };
    }

    public override int ChooseCloseTexture(ref float scale, ref double parallax, ref float a, ref float b)
    {
        CtClose++;
        LastParallax = (float)parallax;
        return BackgroundTextureLoader.GetBackgroundSlot(Mod, "assets/backgrounds/TestSurfaceClose");
    }

    public override bool PreDrawCloseBackground(SpriteBatch spriteBatch)
    {
        CtPreDrawClose++;
        return true;
    }

    public static void Reset() => CtFar = CtMiddle = CtClose = CtPreDrawClose = CtFades = 0;
}