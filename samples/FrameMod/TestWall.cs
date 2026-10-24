using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria;
using Terraria.ID;

namespace FrameMod
{
    internal sealed class TestWall : ModWall
    {
        internal static int Kills;

        public override void SetStaticDefaults()
        {
            Main.wallHouse[Type] = true;
            Main.wallLight[Type] = true;
            AddMapEntry(new Color(80, 120, 160));
        }

        public override int DustType => DustID.Stone;
        public override int ItemDrop => ModContent.ItemType<TestWallItem>();
        public override int HitSound => SoundID.Tink;

        public override void KillWall(int i, int j, ref bool fail)
        {
            Kills++;
        }
    }
}


