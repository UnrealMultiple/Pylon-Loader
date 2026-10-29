using Terraria;
using Terraria.ObjectData;

namespace Pylon.ModLoader
{
    /// <summary>Mod 晶塔的 tile entity（照 tModLoader 的 TEModdedPylon）：Mod 继承它（空实现也行），ModPylon 负责放/拆，放拆会通知游戏的晶塔系统。</summary>
    public class TEModdedPylon : ModTileEntity
    {
        /// <summary>只有晶塔左上角那格有效：方块是 ModPylon、frameY == 0、frameX 能被 TileObjectData 的 CoordinateFullWidth 整除。</summary>
        public override bool IsTileValidForEntity(int x, int y)
        {
            var tile = Main.tile[x, y];
            if (!tile.active()) return false;

            var modTile = ModContent.Find(ContentKind.Tile, tile.type);
            if (modTile is not ModPylon) return false;

            var data = TileObjectData.GetTileData(tile);
            return data != null && tile.frameY == 0 && tile.frameX % data.CoordinateFullWidth == 0;
        }

        /// <summary>这实体位置上的晶塔（ModPylon），不是就 null。</summary>
        public ModPylon TryGetModPylon()
        {
            var tile = Main.tile[Position.X, Position.Y];
            return ModContent.Find(ContentKind.Tile, tile.type) as ModPylon;
        }

        /// <summary>放置回调：转给 Generic_Hook_AfterPlacement（模板的 PlacementHook(false) 用左上角那一格）。</summary>
        public override int Hook_AfterPlacement(int i, int j, int type, int style, int direction, int alternate) =>
            base.Hook_AfterPlacement(i, j, type, style, direction, alternate);
    }
}
