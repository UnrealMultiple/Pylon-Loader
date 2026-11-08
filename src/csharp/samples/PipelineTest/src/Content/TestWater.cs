using Pylon.ModLoader;

namespace PipelineTest.Content;

/// <summary>测试水样式：贴图按 <Name>.png（水面）、<Name>_Block.png、<Name>_Slope.png。</summary>
public sealed class TestWaterStyle : ModWaterStyle
{
    public override string TexturePath => "assets/water/TestWaterStyle.png";

    // 瀑布槽没有公开 TypeOf 门面：Ready 阶段由用例断言 fall.Type 后对比
    public override int ChooseWaterfallStyle() => 0;
}

public sealed class TestWaterfallStyle : ModWaterfallStyle
{
    public override string TexturePath => "assets/water/TestWaterfallStyle.png";

    public override void ColorMultiplier(ref float r, ref float g, ref float b, ref float a)
    {
        r = 1.2f; g = 0.6f; b = 1.1f; a = 0.9f;
    }
}