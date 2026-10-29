using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;

namespace Pylon.ModLoader
{
    /// <summary>自定义瀑布样式：类同名 PNG 做贴图；占游戏 28 个之后的一号，由 ModWaterStyle.ChooseWaterfallStyle 选中。</summary>
    public abstract class ModWaterfallStyle : ModType
    {
        /// <summary>每帧一次：给这风格在屏幕上的每个瀑布点加光（Lighting.AddLight）。</summary>
        public virtual void AddLight(int i, int j) { }

        /// <summary>瀑布颜色：r/g/b 可写（0..255，当地光 × 不透明度），a 是不透明度。</summary>
        public virtual void ColorMultiplier(ref float r, ref float g, ref float b, ref float a) { }

        /// <summary>只放共享数值，不要碰游戏对象。</summary>
        public override void SetStaticDefaults() { }

        // register 故意没翻：桥接入口（实例化 + WaterfallStyleLoader.Add + Safe.Run）；Pylon 的注册由 ModType 自动发现，编号分配在管线里。
    }
}