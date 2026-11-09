using System;
using Pylon.ModLoader;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ObjectData;

namespace PipelineTest.Tests;

/// <summary>
/// 管线 23：Pylon（晶塔）。
/// Ready：类型号、实例、贴图。
/// World：放两座晶塔 → 实体在左上角 → 一个类型只一座 → 进游戏晶塔列表 →
/// 传送（Biome 拒绝 / 放行、NPC 检查）→ 点它开地图 → 拆掉后从列表消失、能再放。
/// 地图图标要等地图真的画过一帧：放在延迟用例里查。
/// </summary>
internal static class PylonTests
{
    private static int _nearX, _farX, _nearFloor, _farFloor;
    private static bool _prepared;

    public static void Register()
    {
        PT.Group("pylon", Phase.Ready, RunReady);
        PT.Group("pylon-w", Phase.World, RunWorld);
    }

    private static void RunReady()
    {
        PT.Case("PYLON-R1", c =>
        {
            var near = ModContent.GetInstance<Content.TestPylon>();
            var far = ModContent.GetInstance<Content.TestPylonFar>();
            c.NotNull(near, "TestPylon 实例");
            c.NotNull(far, "TestPylonFar 实例");
            c.True(near.PylonType >= 11, $"近塔类型号={near.PylonType}（应 >= 11）");
            c.True(far.PylonType > near.PylonType, $"远塔类型号={far.PylonType}（应比近塔大）");
            c.True(near.Texture is not null, "晶塔贴图已上传");
            c.True(near.TextureSibling("_Crystal") is { Length: > 0 }, $"晶石贴图路径={near.TextureSibling("_Crystal")}");
            var sheet = Terraria.GameContent.TextureAssets.Tile[near.Type];
            c.True(sheet is { Value: not null }, $"方块贴图表槽已写（Tile[{near.Type}]）");

            // 3x4 的形状必须落在 TileObjectData 里，否则方块按 1x1 画，只剩底座。
            var data = TileObjectData.GetTileData(near.Type, 0);
            c.True(data is not null && data.Width == 3 && data.Height == 4,
                $"TileObjectData 形状={(data is null ? "null" : data.Width + "x" + data.Height)}（期望 3x4）");
        });
    }

    private static void RunWorld()
    {
        var player = Main.player[Main.myPlayer];
        if (player is null || !player.active)
        {
            PT.SkipCase("PYLON-W1", "玩家还没就绪");
            return;
        }

        if (!_prepared)
        {
            _nearX = (int)(player.Center.X / 16f) + 8;
            _farX = _nearX + 24;
            _nearFloor = FindFloor(_nearX + 1);
            _farFloor = FindFloor(_farX + 1);
            _prepared = true;
        }

        var near = ModContent.GetInstance<Content.TestPylon>();
        var far = ModContent.GetInstance<Content.TestPylonFar>();
        var nearType = near.Type;
        var farType = far.Type;

        PT.Case("PYLON-W1", c =>
        {
            bool nearOk = Place(_nearX, _nearFloor, nearType);
            bool farOk = Place(_farX, _farFloor, farType);
            c.True(nearOk, $"近塔放下（x={_nearX} floor={_nearFloor}）");
            c.True(farOk, $"远塔放下（x={_farX} floor={_farFloor}）");
        });

        // 放完就刷新一次游戏列表：RequestImmediateUpdate 只是打标记，默认要等下一帧。
        PylonSupport.Refresh();

        PT.Case("PYLON-W2", c =>
        {
            var entity = ModContent.GetInstance<Content.TestPylonEntity>();
            int id = entity.Find(_nearX, _nearFloor - 4);
            c.True(id >= 0 && ModTileEntity.ByID.ContainsKey(id), "实体注册在晶塔左上角");
            if (id >= 0)
                c.True(ModTileEntity.ByID[id] is Content.TestPylonEntity pte && pte.TryGetModPylon() == near, "实体指向 TestPylon");
            else
                c.True(false, "没有实体");
        });

        PT.Case("PYLON-W3", c =>
        {
            bool again = CanPlace(_nearX + 20, _nearFloor - 1, nearType);
            c.True(!again, "CanPlacePylon：一个类型世界里只一座（第二座被拒）");
        });

        PT.Case("PYLON-W4", c =>
        {
            // 进了游戏自己的晶塔列表，原版查询就认得它。
            c.True(Main.PylonSystem.HasPylonOfType((TeleportPylonType)near.PylonType), "近塔进了游戏的晶塔列表");
            c.True(Main.PylonSystem.HasPylonOfType((TeleportPylonType)far.PylonType), "远塔进了游戏的晶塔列表");
            c.True(TeleportPylonsSystem.IsPlayerNearAPylon(Main.player[Main.myPlayer]), "玩家附近有晶塔（含 mod 晶塔）");
        });

        PT.Case("PYLON-W5", c =>
        {
            var target = new TeleportPylonInfo
            {
                PositionInTiles = new Point16(_farX, _farFloor - 4),
                TypeOfPylon = (TeleportPylonType)far.PylonType,
            };
            var p = Main.player[Main.myPlayer];
            float startX = p.position.X;

            Content.TestPylonBase.BiomeOk = false;
            Main.PylonSystem.RequestTeleportation(target, p);
            bool stayed = Math.Abs(p.position.X - startX) < 32f;
            int biomeChecks = Content.TestPylonBase.BiomeChecks;

            Content.TestPylonBase.BiomeOk = true;
            Main.PylonSystem.RequestTeleportation(target, p);
            bool moved = Math.Abs(p.position.X / 16f - _farX) < 6f;

            c.True(stayed, $"Biome 检查拒绝时没动（stayed={stayed}）");
            c.True(biomeChecks >= 1, $"Biome 检查调用={biomeChecks}");
            c.True(moved, $"放行后传到了远塔（x={p.position.X / 16f:0}）");
            c.True(Content.TestPylonBase.NpcChecks >= 1, $"NPC 检查调用={Content.TestPylonBase.NpcChecks}");
            p.Teleport(new Il2CppMicrosoft.Xna.Framework.Vector2(startX, p.position.Y), 0, 0);
        });

        PT.Case("PYLON-W6", c =>
        {
            // 点晶塔开全屏地图（ModPylon.RightClick → PylonPipeline.OpenMap）。
            near.RightClick(_nearX + 1, _nearFloor - 2);
            c.True(Main.mapFullscreen, $"点晶塔打开了全屏地图（mapFullscreen={Main.mapFullscreen}）");
            c.Eq(Main.MapPylonTile.X, _nearX, "地图原点 X");
            c.Eq(Main.MapPylonTile.Y, _nearFloor - 4, "地图原点 Y");

            Break(_nearX, _nearFloor);
            PylonSupport.Refresh();

            bool gone = ModContent.GetInstance<Content.TestPylonEntity>().Find(_nearX, _nearFloor - 4) < 0;
            c.True(Content.TestPylonBase.Kills >= 1, $"KillMultiTile 调用={Content.TestPylonBase.Kills}");
            c.True(gone, "实体已移除");
            c.True(!Main.PylonSystem.HasPylonOfType((TeleportPylonType)near.PylonType), "拆掉后从游戏晶塔列表消失");
            c.True(Main.PylonSystem.HasPylonOfType((TeleportPylonType)far.PylonType), "远塔还在游戏晶塔列表里");
            // 原位刚被清空、脚下也垫过实心土，这里能再放才算真的解了禁。
            c.True(CanPlace(_nearX + 1, _nearFloor - 1, nearType), "拆掉后能再放");
        });

        // 地图开着，等它真的画过几帧再查图标（TeleportPylonsMapLayer.Draw 只在地图打开时跑）。
        PT.Later("PYLON-MAP", 30, c =>
        {
            c.True(Content.TestPylonBase.MapIcons >= 1, $"远塔在地图上画出了图标（调用={Content.TestPylonBase.MapIcons}）");
            Main.mapFullscreen = false;
            c.True(!Main.mapFullscreen, "全屏地图能关掉");
        });

        // 关掉地图之后世界才会画，晶石（SpecialDraw）也才会被调到。
        PT.Later("PYLON-CRYSTAL", 80, c =>
        {
            c.True(Content.TestPylonBase.Crystals >= 1, $"晶石绘制调用={Content.TestPylonBase.Crystals}（应 >= 1）");
        });
    }

    /// <summary>放下晶塔并补建实体（TileObject.Place 不走 WorldGen.PlaceTile 的实体钩子）。</summary>
    private static bool Place(int left, int floor, int type)
    {
        // 出生点附近地面常常不平：3x4 的空位整片清掉，脚下垫成实心土。
        for (int x = left - 2; x <= left + 4; x++)
        {
            for (int y = floor - 8; y < floor; y++)
                WorldGen.KillTile(x, y, false, false, true);

            WorldGen.KillTile(x, floor, false, false, true);
            WorldGen.PlaceTile(x, floor, TileID.Dirt, true, true, -1, 0);
        }

        int originX = left + 1, originY = floor - 1;
        var none = new Il2CppSystem.Nullable<int>();
        if (!TileObject.CanPlace(originX, originY, type, 0, 1, out var data, false, none)) return false;
        if (!TileObject.Place(data)) return false;

        // 玩家真正放下时就是这一条：实体由 ModPylon.Hook_AfterPlacement 建（不再手工补）。
        TileObjectData.CallPostPlacementPlayerHook(originX, originY, type, 0, 1, 0, data);
        return true;
    }

    private static bool CanPlace(int x, int y, int type)
    {
        var none = new Il2CppSystem.Nullable<int>();
        return TileObject.CanPlace(x, y, type, 0, 1, out _, false, none);
    }

    private static void Break(int left, int floor)
    {
        for (int y = floor - 4; y < floor; y++)
            for (int x = left; x < left + 3; x++)
                WorldGen.KillTile(x, y);
    }

    private static int FindFloor(int column)
    {
        for (int y = 50; y < Main.maxTilesY - 10; y++)
        {
            var tile = Main.tile[column, y];
            if (tile.active() && Main.tileSolid[tile.type]) return y;
        }
        return Main.spawnTileY;
    }
}
