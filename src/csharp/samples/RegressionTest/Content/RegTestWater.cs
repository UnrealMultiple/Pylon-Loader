using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader;
using Terraria;
using Terraria.ID;

namespace RegressionTest.Content
{
    /// <summary>mod 水样式（P2-7）：编号 ≥16，水面/方块/斜坡三张贴图。</summary>
    public sealed class RegTestWater : ModWaterStyle
    {
        public override string TexturePath => $"assets/water/{Name}.png";

        public override int GetSplashDust() => DustID.Dirt;

        public override void LightColorMultiplier(ref float r, ref float g, ref float b)
        {
            r = 0.8f;
            g = 1.0f;
            b = 0.9f;
        }

        public override Il2CppMicrosoft.Xna.Framework.Graphics.Color BiomeHairColor() =>
            new Il2CppMicrosoft.Xna.Framework.Graphics.Color(120, 255, 200, 255);
    }

    /// <summary>
    /// 回归场景效果：玩家在布景水池附近时抢到场景，把水样式换成 RegTestWater（P2-7）。
    /// 没有它 mod 水样式永远不会被场景选中（水永远原版蓝）。
    /// </summary>
    public sealed class RegTestSceneEffect : ModSceneEffect
    {
        public override bool IsSceneEffectActive(Player player)
        {
            var (wx, wy) = RegTestScene.WaterPos;
            if (wx < 0) return false;
            var px = player.position.X / 16f;
            var py = player.position.Y / 16f;
            var dx = px - (wx + 2);
            var dy = py - (wy + 2);
            return dx * dx + dy * dy < 60 * 60; // 水池 60 格内
        }

        public override int Priority => 4; // Biome 级：盖过森林

        public override ModWaterStyle WaterStyle => ModContent.GetInstance<RegTestWater>();
    }
}
