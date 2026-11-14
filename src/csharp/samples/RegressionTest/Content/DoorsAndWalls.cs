using System;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria;
using Terraria.DataStructures;
using Terraria.Enums;
using Terraria.ID;
using Terraria.ObjectData;

namespace RegressionTest.Content
{
    /// <summary>
    /// 测试门·关门态（本体）。DoorOpenType → 开门态方块。
    /// 服务行：P2-6a（贴身自动开/离开自动关）、P2-6b/c（NPC/僵尸穿门）、P2-6d 对照。
    /// </summary>
    public sealed class DoorClosedTile : ModTile
    {
        public override void SetStaticDefaults()
        {
            Main.tileFrameImportant[Type] = true;
            Main.tileNoAttach[Type] = true;

            var tile = TileObjectData.newTile;
            tile.Width = 1;
            tile.Height = 3;
            tile.Origin = new Point16(0, 2); // 底行落地：PlaceObject 的 y = 底行
            tile.AnchorBottom = new AnchorData(AnchorType.SolidTile | AnchorType.SolidSide, 1, 0);
            tile.CoordinateWidth = 16;
            tile.CoordinatePadding = 2;
            tile.CoordinateHeights = new[] { 16, 16, 16 };
            TileObjectData.addTile(Type);

            AddMapEntry(new Color(160, 110, 60), new Terraria.Localization.LocalizedText("MapObject.DoorClosedTile", "回归门"));
        }

        public override int DustType => DustID.Dirt;
        public override int DoorOpenType => ModContent.TileType<DoorOpenTile>();

        public override bool HasSmartInteract(int i, int j, object settings) => true;
    }

    /// <summary>测试门·开门态（占邻格）。DoorClosedType → 关门态方块。</summary>
    public sealed class DoorOpenTile : ModTile
    {
        public override void SetStaticDefaults()
        {
            Main.tileFrameImportant[Type] = true;
            Main.tileNoAttach[Type] = true;

            var tile = TileObjectData.newTile;
            tile.Width = 1;
            tile.Height = 3;
            tile.Origin = new Point16(0, 2);
            tile.AnchorBottom = new AnchorData(AnchorType.SolidTile | AnchorType.SolidSide, 1, 0);
            tile.CoordinateWidth = 16;
            tile.CoordinatePadding = 2;
            tile.CoordinateHeights = new[] { 16, 16, 16 };
            tile.StyleHorizontal = true;
            TileObjectData.addTile(Type);

            AddMapEntry(new Color(210, 160, 100), new Terraria.Localization.LocalizedText("MapObject.DoorOpenTile", "回归门·开"));
        }

        public override int DustType => DustID.Dirt;
        public override int DoorClosedType => ModContent.TileType<DoorClosedTile>();
    }

    /// <summary>放下测试门（关门态）。</summary>
    public sealed class DoorItem : ModItem
    {
        public override string DisplayName => "回归门";

        public override void SetDefaults(Item item)
        {
            item.width = 20;
            item.height = 34;
            item.maxStack = 99;
            item.DefaultToPlaceableTile(ModContent.TileType<DoorClosedTile>());
        }
    }

    /// <summary>
    /// 测试墙三变体（P1-8e/f、P2-3c）。普通/染色（wallColor）/全亮（fullbright）逐格属性在布景时设置。
    /// </summary>
    public sealed class PlainWall : ModWall
    {
        public override void SetStaticDefaults()
        {
            Main.wallHouse[Type] = true;
            AddMapEntry(new Color(150, 150, 150), new Terraria.Localization.LocalizedText("MapObject.PlainWall", "回归墙·普通"));
        }

        public override int DustType => DustID.Dirt;
    }

    public sealed class DyedWall : ModWall
    {
        public override void SetStaticDefaults()
        {
            Main.wallHouse[Type] = true;
            AddMapEntry(new Color(180, 120, 200), new Terraria.Localization.LocalizedText("MapObject.DyedWall", "回归墙·染色"));
        }

        public override int DustType => DustID.Stone;
    }

    public sealed class GlowWall : ModWall
    {
        public override void SetStaticDefaults()
        {
            Main.wallHouse[Type] = true;
            AddMapEntry(new Color(240, 220, 120), new Terraria.Localization.LocalizedText("MapObject.GlowWall", "回归墙·全亮"));
        }

        public override int DustType => DustID.Stone;
    }
}
