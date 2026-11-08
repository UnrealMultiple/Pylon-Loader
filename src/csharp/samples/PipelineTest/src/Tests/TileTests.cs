using System;
using Pylon.ModLoader;
using Terraria;
using Terraria.ID;
using Terraria.ObjectData;

namespace PipelineTest.Tests;

/// <summary>
/// 管线 27：Tile。
/// Ready：类型/静态表/物品/2x2 形状。
/// World：放方块、CanKill 否决、挖掉 + 掉落、2x2 放置/多格破坏、制作站、通电/右键。
/// </summary>
internal static class TileTests
{
    public static void Register()
    {
        PT.Group("tile", Phase.Ready, RunReady);
        PT.Group("tile-w", Phase.World, RunWorld);
    }

    private static void RunReady()
    {
        PT.Case("TILE-R1", c =>
        {
            var block = ModContent.TileType<Content.TestTileBlock>();
            var obj = ModContent.TileType<Content.TestTileObject>();
            var blockItem = ModContent.ItemType<Content.TestTileBlockItem>();
            var objItem = ModContent.ItemType<Content.TestTileObjectItem>();

            c.True(block >= TileID.Count, $"方块 type={block}");
            c.True(obj >= TileID.Count, $"家具 type={obj}");
            c.True(Main.tileSolid[block], "Main.tileSolid");
            c.True(Main.tileFrameImportant[obj], "Main.tileFrameImportant");

            var data = TileObjectData.GetTileData(obj, 0, 0);
            c.NotNull(data, "TileObjectData");
            if (data is not null)
            {
                c.Eq(data.Width, 2, "形状宽");
                c.Eq(data.Height, 2, "形状高");
                c.Eq(data.CoordinateHeights[1], 18, "第二行坐标高");
                c.Eq(data.Origin.Y, 1, "Origin.Y");
            }

            c.True(TileObjectData.readOnlyData, "安装后 TileObjectData 已锁回只读");
            c.Eq(ContentSamples.ItemsByType[blockItem]?.createTile ?? -1, block, "方块物品 createTile");
            c.Eq(ContentSamples.ItemsByType[objItem]?.createTile ?? -1, obj, "家具物品 createTile");
        });
    }

    private static void RunWorld()
    {
        var player = Main.player[Main.myPlayer];
        if (player is null || !player.active)
        {
            PT.SkipCase("TILE-W1", "玩家还没就绪");
            return;
        }

        var block = ModContent.TileType<Content.TestTileBlock>();
        var obj = ModContent.TileType<Content.TestTileObject>();
        var blockItem = ModContent.ItemType<Content.TestTileBlockItem>();
        var objItem = ModContent.ItemType<Content.TestTileObjectItem>();
        var x = (int)(player.Center.X / 16f) + 4;
        var y = (int)(player.Center.Y / 16f) - 1;

        PT.Case("TILE-W1", c =>
        {
            for (var i = x - 1; i <= x + 12; i++)
                for (var j = y - 2; j <= y + 5; j++)
                    WorldGen.KillTile(i, j, false, false, true);

            var placed = WorldGen.PlaceTile(x, y, block, true, true, -1, 0);
            var tile = Main.tile[x, y];
            c.True(placed && tile.active() && tile.type == block, $"放方块 placed={placed} type={tile.type}");
        });

        PT.Case("TILE-W2", c =>
        {
            Content.TestTileBlock.Veto = true;
            var calls = Content.TestTileBlock.CanKillCalls;
            var vetoed = !WorldGen.CanKillTile(x, y, out _);
            c.True(vetoed, $"CanKillTile 否决（回调 {Content.TestTileBlock.CanKillCalls - calls} 次）");
            Content.TestTileBlock.Veto = false;
            c.True(WorldGen.CanKillTile(x, y, out _), "CanKillTile 放行");

            var before = Content.TestTileBlock.Kills;
            WorldGen.KillTile(x, y, false, false, false);
            c.True(!Main.tile[x, y].active(), "方块已挖掉");
            c.True(Content.TestTileBlock.Kills > before, $"KillTile 调用={Content.TestTileBlock.Kills}");
            c.True(Content.TestTileBlock.Dusts > 0, "CreateDust 调用");
            c.True(HasDrop(blockItem), "掉出测试方块物品");
        });

        PT.Case("TILE-W3", c =>
        {
            for (var i = x + 4; i <= x + 9; i++)
                for (var j = y - 1; j <= y + 4; j++)
                    WorldGen.KillTile(i, j, false, false, true);

            WorldGen.PlaceTile(x + 6, y + 4, TileID.Stone, true, true, -1, 0);
            WorldGen.PlaceTile(x + 7, y + 4, TileID.Stone, true, true, -1, 0);
            // PlaceObject 收锚点（Origin.Y=1，锚在底行 y+3），左上角在 y+2。
            var placed = WorldGen.PlaceObject(x + 6, y + 3, obj, true, 0);
            var top = Main.tile[x + 6, y + 2];
            var bottom = Main.tile[x + 7, y + 3];
            c.True(placed && top.active() && top.type == obj && bottom.active() && bottom.type == obj,
                $"放 2x2 placed={placed} top={top.type} bottom={bottom.type}");


            // 世界地上可能已有旧掉落（旧测试往地上丢过木材），所以只比这次挖掉前后的增量。
            var selfBefore = CountDrops(objItem);
            var woodBefore = CountDrops(ItemID.Wood);

            var before = Content.TestTileObject.MultiKills;
            // 挖左上角一格，管线应替我们清掉其余三格，并只发一次 KillMultiTile。
            WorldGen.KillTile(x + 6, y + 2, false, false, false);

            c.Eq(Content.TestTileObject.MultiKills, before + 1, "整件家具毁后 KillMultiTile 只发一次");
            c.True(!Main.tile[x + 6, y + 2].active() && !Main.tile[x + 7, y + 2].active() &&
                   !Main.tile[x + 6, y + 3].active() && !Main.tile[x + 7, y + 3].active(), "挖一格后四格都已清除");

            var selfDrops = CountDrops(objItem) - selfBefore;
            var woodDrops = CountDrops(ItemID.Wood) - woodBefore;
            PT.Info($"家具掉落：自身={selfDrops} 木材={woodDrops}");
            c.Eq(selfDrops, 1, $"家具掉回自身物品一个（实际 {selfDrops}）");
            c.Eq(woodDrops, 0, $"家具不掉木材（实际 {woodDrops}）");
        });

        PT.Case("TILE-W4", c =>
        {
            WorldGen.PlaceTile(x + 9, y + 4, TileID.Stone, true, true, -1, 0);
            WorldGen.PlaceTile(x + 10, y + 4, TileID.Stone, true, true, -1, 0);
            WorldGen.PlaceObject(x + 9, y + 3, obj, true, 0);

            var previous = player.position;
            try
            {
                player.position = new Il2CppMicrosoft.Xna.Framework.Vector2((x + 9) * 16f, (y + 4) * 16f - player.height);
                player.AdjTiles();
                c.True(player.adjTile[TileID.WorkBenches], "制作站 AdjTiles");
            }
            finally
            {
                player.position = previous;
            }
        });

        PT.Case("TILE-W5", c =>
        {
            WorldGen.PlaceTile(x + 2, y + 2, block, true, true, -1, 0);
            var wires = Content.TestTileBlock.Wires;
            Wiring.HitWireSingle(x + 2, y + 2);
            c.Eq(Content.TestTileBlock.Wires, wires + 1, "HitWire 调用");

            Player.tileTargetX = x + 2;
            Player.tileTargetY = y + 2;
            player.tileInteractAttempted = true;
            player.releaseUseTile = true;
            var clicks = Content.TestTileBlock.Clicks;
            player.TileInteractionsUse(x + 2, y + 2);
            c.True(Content.TestTileBlock.Clicks > clicks, $"RightClick 调用={Content.TestTileBlock.Clicks}");
        });
    }

    private static int CountDrops(int type)
    {
        var items = Main.item;
        if (items is null) return 0;
        var count = 0;
        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            if (item is not null && item.active && item.type == type) count++;
        }
        return count;
    }

    private static bool HasDrop(int type)
    {
        var items = Main.item;
        if (items is null) return false;
        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            if (item is not null && item.active && item.type == type) return true;
        }
        return false;
    }
}
