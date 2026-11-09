using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using ReLogic.Content;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ObjectData;

namespace PipelineTest.Content;

/// <summary>测试树：挂在 TestTreeGround 上，摇树/砍树/尘土/叶子全计数。</summary>
public sealed class TestTree : ModTree
{
    public static int Setups, Shakes, Dusts, Woods;

    public override void SetStaticDefaults()
    {
        Setups++;
        GrowsOnTileId = [ModContent.TileType<TestTreeGround>()];
    }

    public override Asset<Texture2D> GetTexture() => ModContent.Request(Mod, "assets/trees/TestTree.png");

    public override Asset<Texture2D>[] GetTopTextures() =>
        [ModContent.Request(Mod, "assets/trees/TestTree_Tops.png")];

    public override Asset<Texture2D>[] GetBranchTextures() =>
        [ModContent.Request(Mod, "assets/trees/TestTree_Branches.png")];

    // 橡果种在这树的地上换成自己的苗。
    public override int SaplingGrowthType(ref int style)
    {
        style = 0;
        return ModContent.TileType<TestTreeSapling>();
    }

    public override int DropWood()
    {
        Woods++;
        return ItemID.Gel;
    }

    public override int CreateDust()
    {
        Dusts++;
        return DustID.Stone;
    }

    public override int TreeLeaf() => GoreID.TreeLeaf_Normal;

    public override bool Shake(int x, int y, ref bool createLeaves)
    {
        Shakes++;
        return false;
    }
}

/// <summary>测试树的地面方块。</summary>
public sealed class TestTreeGround : ModTile
{
    public override void SetStaticDefaults()
    {
        Main.tileSolid[Type] = true;
        Main.tileBlockLight[Type] = true;
        Main.tileMergeDirt[Type] = true;
        AddMapEntry(new Color(120, 120, 200));
    }
}

/// <summary>测试树苗：1x2，只锚 TestTreeGround；长大交给基类默认（GrowTree）。</summary>
public sealed class TestTreeSapling : ModTile
{
    public static int Grows;

    public override void SetStaticDefaults()
    {
        Main.tileFrameImportant[Type] = true;
        Main.tileNoAttach[Type] = true;

        var tile = TileObjectData.newTile;
        tile.CopyFrom(TileObjectData.Style1x2);
        tile.Origin = new Point16(0, 1);
        tile.CoordinateHeights = new[] { 16, 18 };
        tile.AnchorValidTiles = new[] { ModContent.TileType<TestTreeGround>() };
        TileObjectData.addTile(Type);

        AddMapEntry(new Color(90, 160, 90));
    }

    public override int[] AdjTiles => new[] { (int)TileID.Saplings };

    public override bool GrowSapling(int i, int j, bool underground, int treeHeightAddon, bool ignoreWalls)
    {
        Grows++;
        return WorldGen.GrowTree(i, j, treeHeightAddon, ignoreWalls);
    }
}
