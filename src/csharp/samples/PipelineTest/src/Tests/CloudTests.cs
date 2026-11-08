using Pylon.ModLoader;
using Terraria;

namespace PipelineTest.Tests;

/// <summary>
/// 管线 7：Cloud —— 功能验证。发号在扩表期（Base 运行期捕获），抽签走 Cloud.addCloud/RollRareCloud 钩子。
/// 真实云生成在菜单/世界背景帧驱动；这里验证注册面 + 贴图进表 + 抽签权重函数。
/// </summary>
internal static class CloudTests
{
    public static void Register() => PT.Group("cloud", Phase.Ready, Run);

    private static void Run()
    {
        var cloud = ModContent.GetInstance<Content.TestCloud>();
        var rare = ModContent.GetInstance<Content.TestRareCloud>();

        PT.Case("CLOUD-R1", c =>
        {
            c.NotNull(cloud, "TestCloud 实例");
            c.NotNull(rare, "TestRareCloud 实例");
            c.True(cloud.Type > 0, $"Type={cloud.Type}（扩表期发号）");
            c.True(rare.Type > 0, $"Rare Type={rare.Type}");
            c.True(cloud.Type != rare.Type, "两朵云不同号");
        });

        // T1：贴图进 TextureAssets.Cloud（LoadTextures + CloudPipeline.Build 扩表写槽）
        PT.Case("CLOUD-T1", c =>
        {
            var table = Terraria.GameContent.TextureAssets.Cloud;
            var len = table?.Length ?? 0;
            c.True(len > cloud.Type, $"云贴图表长={len}（应 > {cloud.Type}）");
            if (len > cloud.Type)
            {
                var asset = table![cloud.Type];
                c.NotNull(asset, $"[{cloud.Type}] 槽位为空");
            }
        });

        // H1：抽签权重函数直接验证（SpawnChance=3 参与，返回 mod 号或 0）
        PT.Case("CLOUD-H1", c =>
        {
            c.Eq(cloud.SpawnChance(), 3, "SpawnChance 声明值");
            c.True(!cloud.RareCloud, "TestCloud.RareCloud=false");
            c.True(rare.RareCloud, "TestRareCloud.RareCloud=true");
        });

        // E1：CloudColor 钩子不崩（直接调作者回调）
        PT.Case("CLOUD-E1", c =>
        {
            c.NoThrow(() =>
            {
                var vanilla = new Il2CppMicrosoft.Xna.Framework.Graphics.Color(200, 200, 255, 255);
                var dyed = cloud.CloudColor(null!, vanilla);
                c.True(dyed.R == 255 && dyed.G == 100, $"CloudColor 染色 {dyed}（期望 R=255 G=100）");
            }, "CloudColor 调用");
        });
    }
}