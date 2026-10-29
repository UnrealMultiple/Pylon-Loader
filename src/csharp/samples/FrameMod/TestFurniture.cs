using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria;
using Terraria.DataStructures;
using Terraria.Enums;
using Terraria.ID;
using Terraria.ObjectData;

namespace FrameMod
{
    internal sealed class TestFurniture : ModTile
    {
        internal static int Kills;

        public override void SetStaticDefaults()
        {
            Main.tileFrameImportant[Type] = true;
            Main.tileNoAttach[Type] = true;
            Main.tileLavaDeath[Type] = true;
            TileID.Sets.DisableSmartCursor[Type] = true;

            var data = TileObjectData.newTile;
            data.CopyFrom(TileObjectData.Style2x2);
            data.AnchorBottom = new AnchorData(AnchorType.SolidTile | AnchorType.SolidSide, data.Width, 0);
            TileObjectData.addTile(Type);
            AddMapEntry(new Color(200, 160, 80), CreateMapEntryName());
        }

        public override void KillMultiTile(int i, int j, int frameX, int frameY) => Kills++;
    }
}



