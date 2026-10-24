using Pylon.ModLoader;
using Terraria;
using Pylon.ModLoader.Content;

namespace PipelineTest.Content;

/// <summary>
/// 手动验证用：进世界后一直激活，Priority 100 无条件抢过原版视觉，
/// 把整片水换成 TestWaterStyle——屏幕上直接能看到 mod 水的主贴图 / _Block / _Slope / 瀑布。
/// WATER-W5 关闭后保持关闭，让水样式回原版。
/// </summary>
public sealed class TestWaterScene : ModSceneEffect
{
    public static bool Off;

    public override int Priority => 100;

    public override bool IsSceneEffectActive(Player player) => !Off && !Main.gameMenu && player.active;

    public override ModWaterStyle WaterStyle => ModContent.GetInstance<TestWaterStyle>();
}
