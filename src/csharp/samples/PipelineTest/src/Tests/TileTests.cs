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

        PT.Case("TILE-R2", c =>
        {
            c.Eq(ModContent.GetInstance<Content.TestTileBlock>()!.LocalizationCategory, "Tiles", "ModTile 本地化类别");
            c.Eq(ModContent.GetInstance<Content.TestWall>()!.LocalizationCategory, "Walls", "ModWall 本地化类别");

            var array = new int[] { 1, 2 };
            ModContent.GetInstance<Content.TestTileBlock>()!.AddToArray(ref array);
            c.Eq(array.Length, 3, "AddToArray 追加一格");
            c.Eq(array[2], ModContent.TileType<Content.TestTileBlock>(), "追加的是自己的 Type");

            bool threw = false;
            try
            {
                ModContent.GetInstance<Content.TestTileBlock>()!
                    .AddMapEntry(new Il2CppMicrosoft.Xna.Framework.Graphics.Color(1, 2, 3),
                        new Terraria.Localization.LocalizedText("k", "v"),
                        (_, _, _) => "x");
            }
            catch (NotSupportedException)
            {
                threw = true;
            }

            c.True(threw, "带 nameFunc 的地图项抛 NotSupported");
        });
    }

    private static void RunWorld()
    {
        Main.mapFullscreen = false;

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

        PT.Case("TILE-W6", c =>
        {
            // 全局横切链：CanKill/Kill/NumDust/CreateDust/CanDrop/TileFrame 全局也收到。
            Content.TestTileGlobalProbe.Reset();
            var placed = WorldGen.PlaceTile(x + 3, y + 2, block, true, true, -1, 0);
            c.True(placed, "探针方块已放");

            Content.TestTileGlobalProbe.VetoCanKill = true;
            c.True(!WorldGen.CanKillTile(x + 3, y + 2, out _), "全局否决挖不动");
            c.True(Content.TestTileGlobalProbe.CanKillCalls >= 1, $"全局 CanKillTile={Content.TestTileGlobalProbe.CanKillCalls}");
            Content.TestTileGlobalProbe.VetoCanKill = false;

            var frames = Content.TestTileBlock.Frames;
            WorldGen.TileFrame(x + 3, y + 2, false, false);
            c.True(Content.TestTileGlobalProbe.FrameCalls >= 1, $"全局 TileFrame={Content.TestTileGlobalProbe.FrameCalls}");
            c.True(Content.TestTileBlock.Frames > frames, "本地 TileFrame 也收到");

            var kills = Content.TestTileBlock.Kills;
            WorldGen.KillTile(x + 3, y + 2, false, false, false);
            c.True(Content.TestTileGlobalProbe.KillCalls >= 1, $"全局 KillTile={Content.TestTileGlobalProbe.KillCalls}");
            c.True(Content.TestTileGlobalProbe.NumDustCalls >= 1, $"全局 NumDust={Content.TestTileGlobalProbe.NumDustCalls}");
            c.True(Content.TestTileGlobalProbe.CreateDustCalls >= 1, $"全局 CreateDust={Content.TestTileGlobalProbe.CreateDustCalls}");
            c.True(Content.TestTileBlock.Kills > kills, "本地 KillTile 也收到");
        });

        PT.Case("TILE-W7", c =>
        {
            ClearItems();
            WorldGen.PlaceTile(x + 3, y + 2, block, true, true, -1, 0);

            Content.TestTileGlobalProbe.VetoCanDrop = true;
            WorldGen.KillTile(x + 3, y + 2, false, false, false);
            c.True(Content.TestTileGlobalProbe.CanDropCalls >= 1, $"全局 CanDrop={Content.TestTileGlobalProbe.CanDropCalls}");
            c.True(!HasDrop(blockItem), "全局否决 = 什么都不掉");
            Content.TestTileGlobalProbe.VetoCanDrop = false;

            ClearItems();
            WorldGen.PlaceTile(x + 3, y + 2, block, true, true, -1, 0);
            Content.TestTileGlobalProbe.Reset();
            WorldGen.KillTile(x + 3, y + 2, false, false, false);
            c.True(Content.TestTileGlobalProbe.DropCalls >= 1, $"全局 Drop={Content.TestTileGlobalProbe.DropCalls}");
            c.True(HasDrop(blockItem), "放行时掉出物品");
            ClearItems();
        });

        PT.Case("TILE-W8", c =>
        {
            var dirt = TileID.Dirt;
            WorldGen.PlaceTile(x + 4, y + 2, dirt, true, true, -1, 0);
            Content.TestTileGlobalProbe.Reset();
            WorldGen.ReplaceTile(x + 4, y + 2, block, 0);
            c.True(Content.TestTileGlobalProbe.CanReplaceCalls >= 1, $"全局 CanReplace={Content.TestTileGlobalProbe.CanReplaceCalls}");

            Content.TestTileGlobalProbe.VetoReplace = true;
            WorldGen.PlaceTile(x + 4, y + 2, dirt, true, true, -1, 0);
            var replaced = WorldGen.ReplaceTile(x + 4, y + 2, block, 0);
            c.True(!replaced, "全局否决替换 = 没换成");
            Content.TestTileGlobalProbe.VetoReplace = false;

            var slot = player.selectedItem;
            var previous = player.inventory[slot];
            var blockItemInstance = new Item(blockItem);
            player.inventory[slot] = blockItemInstance;
            Player.tileTargetX = x - 1;
            Player.tileTargetY = y + 2;

            Content.TestTileGlobalProbe.Reset();
            Content.TestTileGlobalProbe.VetoPlace = true;
            try
            {
                player.PlaceThing_Tiles(true);
                PT.Info($"PlaceThing 后：手里物品 createTile={player.inventory[slot]?.createTile} " +
                        $"目标格 type={Main.tile[x - 1, y + 2].type} CanPlaceCalls={Content.TestTileGlobalProbe.CanPlaceCalls}");
            }
            finally
            {
                Content.TestTileGlobalProbe.VetoPlace = false;
                player.inventory[slot] = previous;
            }

            var index = Projectile.NewProjectile(null, new Il2CppMicrosoft.Xna.Framework.Vector2(x * 16f, y * 16f),
                default, ProjectileID.Bomb, 0, 0, Main.myPlayer);
            var proj = Main.projectile[index];
            c.True(proj is not null && proj.active, "探针投射物已生成");
            if (proj is { active: true })
            {
                Content.TestTileGlobalProbe.VetoExplode = true;
                var exploded = proj.CanExplodeTile(x - 1, y + 2, false);
                c.True(Content.TestTileGlobalProbe.CanExplodeCalls >= 1, $"全局 CanExplode={Content.TestTileGlobalProbe.CanExplodeCalls}");
                c.True(!exploded, "全局否决爆炸 = 方块留着");
                Content.TestTileGlobalProbe.VetoExplode = false;
                proj.active = false;
            }
        });

        PT.Case("TILE-W9", c =>
        {
            Content.TestTileGlobalProbe.Reset();

            Content.TestPlayer.DangerDrive = true;
            PT.Info("危险/矿工高亮驱动：dangerSense/findTreasure 每帧补开（30 帧后断言全局计数）");

            // PlaceObject 收锚点（Origin.Y=1，锚在 y+3），左上角在 y+2（TILE-W3 已验证的摆位）。
            var placed = WorldGen.PlaceObject(x + 6, y + 3, obj, true, 0);
            c.True(placed, "2x2 已放");
            c.True(Main.tile[x + 6, y + 2].active() && Main.tile[x + 6, y + 2].type == obj,
                $"左上角那格是家具 type={Main.tile[x + 6, y + 2].type}");
            c.True(TileObjectDataTml.IsTopLeft(x + 6, y + 2), "左上角判定");
            c.True(!TileObjectDataTml.IsTopLeft(x + 7, y + 3), "右下不是左上角");
            var topLeft = TileObjectDataTml.TopLeft(x + 7, y + 3);
            c.True(topLeft.X == x + 6 && topLeft.Y == y + 2, $"TopLeft 折算=({topLeft.X},{topLeft.Y})");
            c.Eq(TileObjectDataTml.GetTileStyle(Main.tile[x + 6, y + 2]), 0, "样式号 0");

            c.True(!TileEntityTml.TryGet<Content.TestTileEntity>(x + 6, y + 3, out _),
                "没有实体的格 TryGet 取不到");

            PT.Later("TILE-W9B", 30, cc =>
            {
                cc.True(Content.TestTileGlobalProbe.DangerousCalls >= 1,
                    $"dangerSense 高亮派发 IsTileDangerous（{Content.TestTileGlobalProbe.DangerousCalls} 次）");
                cc.True(Content.TestTileGlobalProbe.SpelunkableCalls >= 1,
                    $"findTreasure 高亮派发 IsTileSpelunkable（{Content.TestTileGlobalProbe.SpelunkableCalls} 次）");
                Content.TestPlayer.DangerDrive = false;
            });
        });

        PT.Case("TILE-DRAW", c =>
        {
            Main.mapFullscreen = false;
            WorldGen.KillTile(x + 11, y, false, false, true);
            WorldGen.KillTile(x + 11, y + 1, false, false, true);

            var probeType = ModContent.TileType<Content.TestTileDrawProbe>();
            WorldGen.PlaceTile(x + 11, y + 1, TileID.Stone, true, true, -1, 0);
            var anchor = Main.tile[x + 11, y + 1];
            c.True(anchor.active() && anchor.type == TileID.Stone, $"锚石放下 type={anchor.type}");

            var can = Terraria.TileObject.CanPlace(x + 11, y, probeType, 0, 1, out var objectData, false,
                new Il2CppSystem.Nullable<int>());
            var placed = can && Terraria.TileObject.Place(objectData);
            c.True(placed && Main.tile[x + 11, y].active(), $"探针方块经放置对象放下 placed={placed} type={Main.tile[x + 11, y].type}");
            if (placed)
                Terraria.ObjectData.TileObjectData.CallPostPlacementPlayerHook(x + 11, y, probeType, 0, 1, 0, objectData);

            var probeData = Terraria.ObjectData.TileObjectData.GetTileData(probeType, 0);
            PT.Info($"探针形状：GetTileData(probe)={probeData?.Width}x{probeData?.Height} null={probeData is null} " +
                    $"_data.Count={Terraria.ObjectData.TileObjectData._data?.Count}");

            PT.Later("TILE-DRAW10", 10, cc =>
            {
                cc.True(Main.tile[x + 11, y].active(), $"+10帧 type={Main.tile[x + 11, y].type} " +
                    $"邻格({x + 11},{y - 1})={Main.tile[x + 11, y - 1].type} ({x + 11},{y + 1})={Main.tile[x + 11, y + 1].type}");
            });

            PT.Later("TILE-DRAW20", 20, cc =>
            {
                cc.True(Main.tile[x + 11, y].active(), $"+20帧 type={Main.tile[x + 11, y].type}");
            });

            PT.Later("TILE-DRAW30", 30, cc =>
            {
                cc.True(Main.tile[x + 11, y].active() &&
                        Main.tile[x + 11, y].type == probeType,
                    $"+30帧 type={Main.tile[x + 11, y].type}（期望 {probeType}）");
                cc.True(Content.TestTileDrawProbe.PreDraws >= 1, $"PreDraw={Content.TestTileDrawProbe.PreDraws}");
                cc.True(Content.TestTileDrawProbe.PostDraws >= 1, $"PostDraw={Content.TestTileDrawProbe.PostDraws}");
                cc.True(Content.TestTileDrawProbe.DrawEffectsCalls >= 1, $"DrawEffects={Content.TestTileDrawProbe.DrawEffectsCalls}");
                cc.True(Content.TestTileDrawProbe.SpecialDraws >= 1, $"SpecialDraw={Content.TestTileDrawProbe.SpecialDraws}");
            });
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

    private static void ClearItems()
    {
        var items = Main.item;
        if (items is null) return;
        for (var k = 0; k < items.Length; k++)
        {
            var item = items[k];
            if (item is not null && item.active) item.TurnToAir();
        }
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
