using Pylon.ModLoader;
using Terraria;
using Terraria.ID;

namespace FrameMod
{
    internal sealed class TestTileItem : ModItem
    {
        public override void SetDefaults(Item item)
        {
            item.width = 16;
            item.height = 16;
            item.maxStack = 999;
            item.useTurn = true;
            item.autoReuse = true;
            item.useAnimation = 10;
            item.useTime = 10;
            item.useStyle = (byte)ItemUseStyleID.Swing;
            item.consumable = true;
            item.createTile = ModContent.TileType<TestTile>();
            item.placeStyle = (byte)0;
            item.value = 10;
        }
    }
}




