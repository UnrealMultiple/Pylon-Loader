using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.Enums;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ObjectData;

namespace Pylon.ModLoader
{
    /// <summary>敌人的旗子（1x3 的 ModTile）：样式指向放置它的物品，NPC 的 BannerItem 再从这个物品指到代表 NPC。</summary>
    public abstract class ModBannerTile : ModTile
    {
        /// <summary>只放共享数值：多格对象、不贴墙、熔岩即毁、去智能光标；形状从 1x2Top 拷成 3 格高（3 个 16 像素行）并横排，另加平台/整高锚点的备用形状；地图名 MapObject.Banner。</summary>
        public override void SetStaticDefaults()
        {
            Main.tileFrameImportant[Type] = true;
            Main.tileNoAttach[Type] = true;
            Main.tileLavaDeath[Type] = true;
            TileID.Sets.DisableSmartCursor[Type] = true;

            var tile = TileObjectData.newTile;
            tile.CopyFrom(TileObjectData.Style1x2Top);
            tile.Height = 3;
            tile.CoordinateHeights = new[] { 16, 16, 16 };
            tile.StyleHorizontal = true;
            tile.AnchorTop = new AnchorData(AnchorType.SolidTile | AnchorType.SolidSide | AnchorType.SolidBottom | AnchorType.PlanterBox, tile.Width, 0);
            tile.DrawYOffset = -2;

            var alt = TileObjectData.newAlternate;
            alt.CopyFrom(tile);
            alt.AnchorTop = new AnchorData(AnchorType.PlatformNonHammered | AnchorType.AllFlatHeight, tile.Width, 0);
            alt.DrawYOffset = -10;

            TileObjectData.addAlternate(0);
            TileObjectData.addTile(Type);

            AddMapEntry(new Color(13, 88, 130), Language.GetText("MapObject.Banner"));
        }

        /// <summary>砸/挖时不出尘土（ModTile.DustType 只读，落成覆写）。</summary>
        public override int DustType => -1;

        /// <summary>不画默认贴图（返回 false）；命中左上角那格的特殊绘制点由管线接。</summary>
        public override bool PreDraw(int i, int j, SpriteBatch spriteBatch) => false;

        /// <summary>往下挪 2 像素再画（只用 width 与 offsetY 两个参数）。</summary>
        public override void SetDrawPositions(int i, int j, ref int width, ref int offsetY, ref int height, ref int tileFrameX, ref int tileFrameY) => offsetY += 2;

        /// <summary>旗在玩家附近；按掉落物查 BannerLoader.ByItem 拿代表 NPC，点亮 Main.SceneMetrics 的 NPCBannerBuff/hasBanner（由管线接）。</summary>
        public override void NearbyEffects(int i, int j, bool closer) { }
    }
}
