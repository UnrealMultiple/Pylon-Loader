using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;

namespace Pylon.ModLoader
{
    /// <summary>自定义水样式：抢到玩家场景时出现；占游戏 15 个之后的一号。贴图：类同名 PNG（水面）、X_Block（方块）、X_Slope（斜坡，缺则用方块）。</summary>
    public abstract class ModWaterStyle : ModType
    {
        /// <summary>这水用哪条瀑布（ModWaterfallStyle 的 Slot，或游戏的 0 = 森林）。</summary>
        public virtual int ChooseWaterfallStyle() => 0;

        /// <summary>落水溅起的水花尘（默认 DustID.Water = 33）。</summary>
        public virtual int GetSplashDust() => 33;

        /// <summary>方块滴下的水珠（默认 GoreID.WaterDrip = 706）。</summary>
        public virtual int GetDropletGore() => 706;

        /// <summary>多少光能穿过水（r/g/b 可写，默认 0.88 / 0.96 / 1.015）。</summary>
        public virtual void LightColorMultiplier(ref float r, ref float g, ref float b)
        {
            r = 0.88f;
            g = 0.96f;
            b = 1.015f;
        }

        /// <summary>雨水用哪一列（贴图宽度 / 4；游戏每样式 3 列，0..2 是森林）。</summary>
        public virtual int GetRainVariant() => Main.rand.Next(3);

        /// <summary>雨贴图：Mod 目录里的 PNG 路径；null = 用游戏自己的。</summary>
        public virtual string GetRainTexture() => null;

        /// <summary>这水的生物群系染发色（默认森林色）。</summary>
        public virtual Color BiomeHairColor() => new Color(28, 216, 94, 255);

        /// <summary>只放共享数值，不要碰游戏对象。</summary>
        public override void SetStaticDefaults() { }

        // register 故意没翻：桥接入口（实例化 + WaterStyleLoader.Add + Safe.Run(SetStaticDefaults)）；Pylon 的注册由 ModType 自动发现，编号分配在管线里。
    }
}
