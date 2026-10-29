using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria;
using Terraria.ID;
using Terraria.ObjectData;

namespace FrameMod
{
    internal sealed class TestTile : ModTile
    {
        internal static int Kills;
        internal static int Lights;

        public override void SetStaticDefaults()
        {
            Main.tileSolid[Type] = true;
            Main.tileNoSunLight[Type] = true;
            Main.tileLighted[Type] = true;
            Main.tileLavaDeath[Type] = true;
            AddMapEntry(new Color(120, 80, 200));
        }

        public override int DustType => DustID.Stone;
        public override int ItemDrop => ModContent.ItemType<TestTileItem>();
        public override int MineResist => 2;
        public override int HitSound => SoundID.Tink;

        public override void KillTile(int i, int j, ref bool fail, ref bool effectOnly, ref bool noItem)
        {
            Kills++;
        }

        public override void ModifyLight(int i, int j, ref float r, ref float g, ref float b)
        {
            Lights++;
            r = 0.4f;
            g = 0.2f;
            b = 0.8f;
        }
    }
}


