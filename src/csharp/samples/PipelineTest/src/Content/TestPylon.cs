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
        // Texture 是 Asset（不是路径），同目录后缀贴图走 TextureSibling。
        _mapIcon = ModContent.Request(Mod, TextureSibling("_MapIcon"));
        _crystal = ModContent.Request(Mod, TextureSibling("_Crystal"));
    }

    /// <summary>放下时建实体（左上角）；多格放置走 CallPostPlacementPlayerHook，1x1 走 WorldGen.PlaceTile，两边都幂等。</summary>
    public override int Hook_AfterPlacement(int i, int j, int type, int style, int direction, int alternate)
    {
        var data = TileObjectData.GetTileData(type, style, alternate);
        if (data is null) return -1;

        int topLeftX = i - data.Origin.X, topLeftY = j - data.Origin.Y;
        var entity = ModContent.GetInstance<TestPylonEntity>();
        int id = entity.Find(topLeftX, topLeftY);
        return id >= 0 ? id : entity.Place(topLeftX, topLeftY);
    }

    public override void PlaceInWorld(int i, int j, Item item)
    {
        // TileObject.Place 不走 WorldGen.PlaceTile 时的兜底：手动补一次（幂等）。
        if (ModContent.GetInstance<TestPylonEntity>().Find(i, j) < 0)
        {
            var data = TileObjectData.GetTileData(Main.tile[i, j]);
            if (data is not null && Main.tile[i, j].frameY == 0 && Main.tile[i, j].frameX % data.CoordinateFullWidth == 0)
                ModContent.GetInstance<TestPylonEntity>().Place(i, j);
        }
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
    public override void SetDefaults(Item item) => item.DefaultToPlaceableTile(ModContent.TileType<TestPylon>());
}

public sealed class TestPylonFarItem : ModItem
{
    public override void SetDefaults(Item item) => item.DefaultToPlaceableTile(ModContent.TileType<TestPylonFar>());
}
