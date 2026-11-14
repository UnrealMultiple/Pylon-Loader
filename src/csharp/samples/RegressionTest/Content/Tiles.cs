using System;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria;
using Terraria.DataStructures;
using Terraria.Localization;
using Terraria.Enums;
using Terraria.ID;
using Terraria.ObjectData;

namespace RegressionTest.Content
{
    /// <summary>
    /// 测试家具（3×3，底行 18px，3 帧竖排动画，双地图色，AdjTiles 制作站，作者数据+绘制钩全套）。
    /// 服务行：P1-6a(18px)/P1-6b(动画)/P1-6c(作者帧)/P1-6e(缓存)、P1-7、P1-8a(存档)、P2-3a(地图色)、P2-4、GiMms。
    /// </summary>
    public sealed class FurnitureTile : ModTile
    {
        public static int SaveCalls, LoadCalls, RandomCalls, IndiCalls, DrawPosCalls, SpriteCalls, PreDrawCalls;

        public override void SetStaticDefaults()
        {
            Main.tileFrameImportant[Type] = true;
            Main.tileNoAttach[Type] = true;
            Main.tileLighted[Type] = true;

            var tile = TileObjectData.newTile;
            tile.CopyFrom(TileObjectData.Style3x3);
            tile.CoordinateHeights = new[] { 16, 16, 18 }; // 底行 18px（水槽式）
            tile.AnchorBottom = new AnchorData(AnchorType.SolidTile, 3, 0);
            TileObjectData.addTile(Type);

            AddMapEntry(new Color(200, 120, 60), new LocalizedText("MapObject.FurnitureTile", "回归家具"));
            AddMapEntry(new Color(90, 180, 220), new LocalizedText("MapObject.FurnitureTile2", "回归家具·蓝"));
        }

        public override int AnimationFrameHeight => 3; // 3 帧竖排
        public override int DustType => DustID.Stone;
        public override int[] AdjTiles => new int[] { TileID.WorkBenches, TileID.Anvils }; // mod 制作站（GiMms）

        /// <summary>相邻两件家具给不同地图色（P2-3a 的双色对照）。</summary>
        public override int GetMapOption(int i, int j) => (i / 3) % 2;

        /// <summary>P1-7：家具本体也挂随机更新（计数用，生长主体在 CropTile）。</summary>
        public override void RandomUpdate(int i, int j) => RandomCalls++;

        /// <summary>P1-8a：作者数据（写简单 int/string，避开 NaN 雷区）。</summary>
        public override void SaveData(int x, int y, TagCompound tag)
        {
            SaveCalls++;
            tag.Set("rt", 1);
            tag.Set("note", "F" + x + "," + y);
        }

        public override void LoadData(int x, int y, TagCompound tag)
        {
            LoadCalls++;
            var note = tag.GetString("note");
            RegressionTestMod.Log?.Info($"[rt] FurnitureTile.LoadData({x},{y}) note={note}");
        }

        /// <summary>P1-6c：作者帧偏移（相邻家具错开 18px 看镜像/偏移）。</summary>
        public override void AnimateIndividualTile(int type, int i, int j, ref int frameXOffset, ref int frameYOffset)
        {
            IndiCalls++;
            frameXOffset += ((i / 3) % 2) * 18;
        }

        /// <summary>P1-6a：底行 18px 要完整显示（height=18）。</summary>
        public override void SetDrawPositions(int i, int j, ref int width, ref int offsetY, ref int height,
                                              ref int tileFrameX, ref int tileFrameY)
        {
            DrawPosCalls++;
            height = 18;
        }

        public override void SetSpriteEffects(int i, int j, ref SpriteEffects spriteEffects)
        {
            SpriteCalls++;
        }

        public override bool PreDraw(int i, int j, Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch spriteBatch)
        {
            PreDrawCalls++;
            return true;
        }
    }

    /// <summary>放下测试家具。</summary>
    public sealed class FurnitureItem : ModItem
    {
        public override string DisplayName => "回归家具";

        public override void SetDefaults(Item item)
        {
            item.width = 24;
            item.height = 24;
            item.maxStack = 99;
            item.rare = 1;
            item.DefaultToPlaceableTile(ModContent.TileType<FurnitureTile>());
        }
    }

    /// <summary>
    /// 测试作物（1×1，3 生长阶段走 frameY=0/20/40，RandomUpdate 推进）。
    /// 服务行：P1-7（地表+地下生长）。
    /// </summary>
    public sealed class CropTile : ModTile
    {
        public static int GrowthEvents;

        public override void SetStaticDefaults()
        {
            Main.tileFrameImportant[Type] = true;
            var tile = TileObjectData.newTile;
            tile.CopyFrom(TileObjectData.Style1x1);
            tile.AnchorBottom = new AnchorData(AnchorType.SolidTile, 1, 0);
            TileObjectData.addTile(Type);
            AddMapEntry(new Color(120, 220, 120), new LocalizedText("MapObject.CropTile", "回归作物"));
        }

        public override int DustType => DustID.Grass;

        /// <summary>随机更新推进一步：frameY 0→20→40（贴图 20×60 三段）。</summary>
        public override void RandomUpdate(int i, int j)
        {
            var tile = Main.tile[i, j];
            if (tile == null || !tile.active() || tile.type != Type) return;
            var fy = tile.frameY;
            if (fy >= 40) return;

            tile.frameY = (short)(fy + 20);
            GrowthEvents++;
            if (GrowthEvents % 5 == 1)
                RegressionTestMod.Log?.Info($"[rt] 作物生长：({i},{j}) 阶段 {fy / 20}→{fy / 20 + 1}（地表={j < Main.worldSurface}）");
        }
    }

    /// <summary>
    /// 裸 1×1 实心方块：不声明 TileObjectData（域 3-B3 对照 + P1-5 实心块键表）。
    /// </summary>
    public sealed class BareTile : ModTile
    {
        public override void SetStaticDefaults()
        {
            Main.tileSolid[Type] = true;
            Main.tileMergeDirt[Type] = true;
            AddMapEntry(new Color(160, 160, 90), new LocalizedText("MapObject.BareTile", "回归裸方块"));
        }

        public override int DustType => DustID.Dirt;
    }

    /// <summary>放下裸方块。</summary>
    public sealed class BareTileItem : ModItem
    {
        public override string DisplayName => "回归裸方块";

        public override void SetDefaults(Item item) =>
            item.DefaultToPlaceableTile(ModContent.TileType<BareTile>());
    }
}
