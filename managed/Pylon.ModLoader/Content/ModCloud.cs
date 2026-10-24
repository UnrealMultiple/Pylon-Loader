using Terraria;

namespace Pylon.ModLoader
{
    /// <summary>自定义云：一个类型一个实例；贴图由管线按 &lt;Name&gt; 找，SpawnChance 是它在云朵抽签里的权重。</summary>
    public abstract class ModCloud : ModType
    {
        /// <summary>稀有云（对的是游戏那批稀有云，不是 22 朵普通云）；false = 普通云。</summary>
        public virtual bool RareCloud => false;

        /// <summary>抽签权重（0 = 不参与）；普通云对游戏 22 朵，稀有云对游戏那批稀有云。</summary>
        public virtual int SpawnChance() => 1;

        /// <summary>这朵云出现之后（云已经是活的，位置和大小都定了）。</summary>
        public virtual void OnSpawn(Cloud cloud) { }
    }
}
