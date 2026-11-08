using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria;
using Terraria.DataStructures;
using Terraria.Enums;
using Terraria.ID;
using Terraria.ObjectData;

namespace PipelineTest.Content;

/// <summary>测试实心方块：挖/尘土/通电/右键/边框都计数。</summary>
public sealed class TestTileBlock : ModTile
{
    public static bool Veto;
    public static int CanKillCalls, Kills, Dusts, Wires, Clicks, Frames;

    public override void SetStaticDefaults()
    {
        Main.tileSolid[Type] = true;
        Main.tileBlockLight[Type] = true;
        Main.tileMergeDirt[Type] = true;
        AddMapEntry(new Color(70, 140, 200));
    }

    public override int DustType => DustID.Stone;

    public override bool CanKillTile(int i, int j, ref bool blockDamaged)
    {
        CanKillCalls++;
        return !Veto;
    }

    public override void KillTile(int i, int j, ref bool fail, ref bool effectOnly, ref bool noItem) => Kills++;

    public override bool CreateDust(int i, int j, ref int type)
    {
        Dusts++;
        return true;
    }

    public override void HitWire(int i, int j) => Wires++;

    public override bool RightClick(int i, int j)
    {
        Clicks++;
        return true;
    }

    public override bool TileFrame(int i, int j, ref bool resetFrame, ref bool noBreak)
    {
        Frames++;
        return true;
    }
}

/// <summary>测试 2x2 家具：形状/锚点/Origin 全在 SetStaticDefaults 里写；多格破坏计数。</summary>
public sealed class TestTileObject : ModTile
{
    public static int MultiKills;

    public override int[] AdjTiles => new[] { (int)TileID.WorkBenches };

    public override void SetStaticDefaults()
    {
        Main.tileFrameImportant[Type] = true;
        Main.tileNoAttach[Type] = true;

        var tile = TileObjectData.newTile;
        tile.CopyFrom(TileObjectData.Style2x2);
        tile.Origin = new Point16(0, 1);
        tile.CoordinateHeights = new[] { 16, 18 };
        tile.AnchorBottom = new AnchorData(AnchorType.SolidTile, 2, 0);
        tile.StyleHorizontal = true;
        TileObjectData.addTile(Type);

        AddMapEntry(new Color(180, 120, 60));
    }

    public override void KillMultiTile(int i, int j, int frameX, int frameY) => MultiKills++;
}

/// <summary>放下测试实心方块。</summary>
public sealed class TestTileBlockItem : ModItem
{
    public override string DisplayName => "测试方块";

    public override void SetDefaults(Item item) => item.DefaultToPlaceableTile(ModContent.TileType<TestTileBlock>());
}

/// <summary>放下测试 2x2 家具。</summary>
public sealed class TestTileObjectItem : ModItem
{
    public override string DisplayName => "测试家具";

    public override void SetDefaults(Item item) => item.DefaultToPlaceableTile(ModContent.TileType<TestTileObject>());
}
