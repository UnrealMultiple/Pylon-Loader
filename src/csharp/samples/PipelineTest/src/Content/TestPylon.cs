using System;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.Localization;
using Terraria.Map;
using Terraria.ObjectData;

namespace PipelineTest.Content;

/// <summary>晶塔的 tile entity：空壳即可，ModPylon 负责放/拆。</summary>
public sealed class TestPylonEntity : TEModdedPylon
{
}

public abstract class TestPylonBase : ModPylon
{
    public static int NpcChecks, BiomeChecks, MapIcons, Crystals, Kills;
    public static bool BiomeOk = true;

    private Asset<Texture2D> _mapIcon = null!;
    private Asset<Texture2D> _crystal = null!;

    public override void SetStaticDefaults()
    {
        Main.tileFrameImportant[Type] = true;
        TileObjectData.newTile.CopyFrom(TileObjectData.Style3x4);
        TileObjectData.newTile.LavaDeath = false;
        TileObjectData.newTile.StyleHorizontal = true;
        TileObjectData.addTile(Type);
        AddMapEntry(Color.White, CreateMapEntryName());
        RegisterItemDrop(ModContent.ItemType<TestPylonItem>());
        // Texture 是 Asset（不是路径），同目录后缀贴图走 TextureSibling。
        _mapIcon = Mod.Assets.Request<Texture2D>(TextureSibling("_MapIcon"));
        _crystal = Mod.Assets.Request<Texture2D>(TextureSibling("_Crystal"));
    }

    /// <summary>放下时建实体：多格放置的补建走这里（管线在 PlaceTile 与 CallPostPlacement 两处都会调，
    /// 客户端发请求等广播，服务端就地建；按左上角帧判定保证幂等）。</summary>
    public override void PlaceInWorld(int i, int j, Item item)
    {
        if (ModContent.GetInstance<TestPylonEntity>().Find(i, j) >= 0) return;

        var tile = Main.tile[i, j];
        var data = TileObjectData.GetTileData(tile);
        if (data is null) return;

        // 只在左上角那格落实体：i,j 是放置锚点，不是左上角时按 Origin 折回左上角再建。
        if (tile.frameY == 0 && tile.frameX % data.CoordinateFullWidth == 0)
            ModContent.GetInstance<TestPylonEntity>().Place(i, j);
        else if (data.Origin.X <= i && data.Origin.Y <= j)
            ModContent.GetInstance<TestPylonEntity>().Place(i - data.Origin.X, j - data.Origin.Y);
    }

    public override void KillMultiTile(int i, int j, int frameX, int frameY)
    {
        Kills++;
        ModContent.GetInstance<TestPylonEntity>().Kill(i, j);
    }

    public override bool ValidTeleportCheck_NPCCount(TeleportPylonInfo pylonInfo, int defaultNecessaryNPCCount)
    {
        NpcChecks++;
        return true;
    }

    public override bool ValidTeleportCheck_BiomeRequirements(TeleportPylonInfo pylonInfo, SceneMetrics sceneData)
    {
        BiomeChecks++;
        return BiomeOk;
    }

    public override void DrawMapIcon(ref MapOverlayDrawContext context, ref string mouseOverText, TeleportPylonInfo pylonInfo,
                                    bool isNearPylon, Color drawColor, float deselectedScale, float selectedScale)
    {
        MapIcons++;
        bool mouseOver = DefaultDrawMapIcon(ref context, _mapIcon, pylonInfo.PositionInTiles.ToVector2() + new Vector2(1.5f, 2f), drawColor, deselectedScale, selectedScale);
        DefaultMapClickHandle(mouseOver, pylonInfo, "Mods.PipelineTest.Pylon", ref mouseOverText);
    }

    public override void SpecialDraw(int i, int j, SpriteBatch spriteBatch)
    {
        Crystals++;
        DefaultDrawPylonCrystal(spriteBatch, i, j, _crystal, _crystal, new Vector2(0f, -12f), Color.White * 0.1f, Color.White, 1, 8);
    }
}

public sealed class TestPylon : TestPylonBase
{
}

public sealed class TestPylonFar : TestPylonBase
{
}

public sealed class TestPylonItem : ModItem
{
    public override void SetDefaults() => Item.DefaultToPlaceableTile(ModContent.TileType<TestPylon>());
}

public sealed class TestPylonFarItem : ModItem
{
    public override void SetDefaults() => Item.DefaultToPlaceableTile(ModContent.TileType<TestPylonFar>());
}
