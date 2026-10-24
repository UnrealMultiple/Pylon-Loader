namespace Pylon.ModLoader
{
    /// <summary>自定义地下背景风格：ModSceneEffect/ModBiome 的 UndergroundBackgroundStyle 抢到场景时用；占游戏 22 个风格之后的编号。</summary>
    public abstract class ModUndergroundBackgroundStyle : ModType
    {
        /// <summary>填各层贴图编号：[0] 天/地边界 160x16、[1] 土、[2] 土/石边界 160x16、[3] 石 160x96、[4] 通往地狱（随游戏）。</summary>
        public virtual void FillTextureArray(int[] textureSlots) { }

        /// <summary>只放共享数值，不要碰游戏对象。</summary>
        public override void SetStaticDefaults() { }

        // register 故意没翻：桥接入口（实例化 + UndergroundBackgroundLoader.Add + Safe.Run(SetStaticDefaults)）；Pylon 的注册由 ModType 自动发现，编号分配与调用在管线里。
    }
}
