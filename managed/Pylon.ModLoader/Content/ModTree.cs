using Il2CppMicrosoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.ID;

namespace Pylon.ModLoader
{
    /// <summary>自定义树；游戏眼里它就是 tile 5 那种树，认谁的是看树干底下那块地（GrowsOnTileId）。</summary>
    public abstract class ModTree : ModType
    {
        /// <summary>贴图相对 Mod 目录的路径（默认 assets/trees/&lt;Name&gt;.png）。</summary>
        public override string TexturePath => $"assets/trees/{Name}.png";

        /// <summary>这树长在哪些地上（写自己的 ModTile 编号）；一个地只能给一棵树。</summary>
        public int[] GrowsOnTileId = new int[0];

        /// <summary>树干贴图（tile 5 那张图里 176 宽的一条）。</summary>
        public virtual Asset<Texture2D> GetTexture() => Texture;

        /// <summary>树顶贴图（3 张 80x80，间隔 2）。</summary>
        public virtual Asset<Texture2D>[] GetTopTextures() => new Asset<Texture2D>[0];

        /// <summary>树枝贴图（左右 2 列，每列 3 张 40x40）。</summary>
        public virtual Asset<Texture2D>[] GetBranchTextures() => new Asset<Texture2D>[0];

        // CountsAsTreeType 故意没翻：本版 Terraria.Enums.TreeTypes 只到 Ash = 12，没有 Custom，不拿别的值顶替。

        /// <summary>打树干时出的尘土编号；&lt; 0 = 不出尘土。</summary>
        public virtual int CreateDust() => 7;

        /// <summary>叶子的 gore 编号（长出来、摇树、刮风时用）；-1 = 不要。</summary>
        public virtual int TreeLeaf() => -1;

        /// <summary>摇这棵树；(x, y) 是树顶，createLeaves 是引用，false = 不要游戏那套掉落。</summary>
        public virtual bool Shake(int x, int y, ref bool createLeaves) => true;

        /// <summary>砍这棵树掉不掉橡果。</summary>
        public virtual bool CanDropAcorn() => true;

        /// <summary>橡果种在这树的地上长成哪个苗（style 可写）。</summary>
        public virtual int SaplingGrowthType(ref int style)
        {
            style = 0;
            return TileID.Saplings;
        }

        /// <summary>砍了掉哪种木头。</summary>
        public virtual int DropWood() => ItemID.Wood;
    }
}
