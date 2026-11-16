using Pylon.ModLoader;
using Terraria;

namespace PipelineTest.Content;

/// <summary>测试旗子物品：放下 <see cref="TestZBannerTile"/>；领取旗子时发的就是它。</summary>
public sealed class TestZBannerItem : ModItem
{
    public override string DisplayName => "测试旗子";

    public override void SetDefaults()
    {
        Item.width = 12;
        Item.height = 30;
        Item.maxStack = 999;
        Item.value = 100;
        Item.DefaultToPlaceableTile(ModContent.TileType<TestZBannerTile>(), 0);
    }
}
