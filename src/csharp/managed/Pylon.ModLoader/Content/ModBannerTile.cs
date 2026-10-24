using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Pipelines;
using Terraria;
using Terraria.DataStructures;
using Terraria.Enums;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ObjectData;

namespace Pylon.ModLoader.Content;
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
        // Il2CppStructArray 只吃 int[] 的隐式转换，不能用集合表达式构造。
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

    /// <summary>不画默认贴图（返回 false）；左上角那格交给管线的随风绘制。</summary>
    public override bool PreDraw(int i, int j, SpriteBatch spriteBatch)
    {
        var tile = Main.tile[i, j];
        var data = Terraria.ObjectData.TileObjectData.GetTileData(tile);
        if (data is not null && tile.frameX % data.CoordinateFullWidth == 0 && tile.frameY % data.CoordinateFullHeight == 0)
            TileDrawPipeline.AddVine(i, j);
        return false;
    }

    /// <summary>往下挪 2 像素再画（只用 width 与 offsetY 两个参数）。</summary>
    public override void SetDrawPositions(int i, int j, ref int width, ref int offsetY, ref int height, ref int tileFrameX, ref int tileFrameY) => offsetY += 2;

    /// <summary>旗在玩家附近（buff 扫描面）：按掉落物反查旗子号，点亮扫描 SceneMetrics 的 NPCBannerBuff/hasBanner。</summary>
    public override void NearbyEffects(int i, int j, bool closer)
    {
        if (closer) return;

        var item = TileTable.DropOf(Type, TileObjectDataTml.GetTileStyle(Main.tile[i, j]));
        if (item <= 0 || !BannerTable.TryGetBannerForItem(item, out var banner)) return;

        var strength = ItemID.Sets.BannerStrength;
        if (strength is null || (uint)item >= (uint)strength.Length || !strength[item].Enabled) return;

        var metrics = TileEffectsHooks.ScanningMetrics;
        BannerTable.Fit(metrics);
        var buffs = metrics.NPCBannerBuff;
        if (buffs is not null && banner < buffs.Length) buffs[banner] = true;
        metrics.hasBanner = true;
    }
}
