using Pylon.ModLoader;
using Terraria;
using Terraria.ID;

namespace PipelineTest.Tests;

/// <summary>
/// 墙管线。
/// wall（World）：注册面（类型/贴图/物品 createWall/房屋墙标记）。
/// wall-w（World）：放墙 / 边框 / 挖墙回调链（尘土、声音、掉落）/ 挖掉对照墙。
/// </summary>
internal static class WallTests
{
    private static int _baseX, _floor;

    public static void Register()
    {
        PT.Group("wall", Phase.World, RunReady);
        PT.Group("wall-w", Phase.World, RunWorld);
    }

    private static void RunReady()
    {
        var player = Main.player[Main.myPlayer];
        if (player is null || !player.active)
        {
            PT.SkipCase("WALL-R1", "玩家还没就绪");
            return;
        }

        PT.Case("WALL-R1", c =>
        {
            var type = ModContent.WallType<Content.TestWall>();
            c.True(type > 0, $"墙 type={type}");

            var itemType = ModContent.ItemType<Content.TestWallItem>();
            var item = ContentSamples.ItemsByType[itemType];
            c.NotNull(item, "TestWallItem 模板");
            c.Eq(item?.createWall ?? (ushort)0, (ushort)type, "item.createWall");

            // 贴图进了游戏墙表（安装面）。
            var assets = Terraria.GameContent.TextureAssets.Wall;
            c.True(assets is not null && type < assets.Length && assets[type] is not null, $"墙贴图槽 {type}");
            c.True(Main.wallHouse[type], "wallHouse 标记");
        });
    }

    private static void RunWorld()
    {
        var player = Main.player[Main.myPlayer];
        if (player is null || !player.active)
        {
            PT.SkipCase("WALL-W1", "玩家还没就绪");
            return;
        }

        var type = ModContent.WallType<Content.TestWall>();
        var plainType = ModContent.WallType<Content.TestWallPlain>();
        // 摆在玩家旁边（脚底那一排往上 3 行）：那里是空的，墙看得见，也不用挖开地面。
        _baseX = (int)(player.Center.X / 16) + 3;
        _floor = (int)(player.Center.Y / 16);

        if (_baseX + 8 >= Main.maxTilesX) _baseX = Main.maxTilesX - 9;
        if (_floor - 6 < 1) _floor = 6;

        PT.Case("WALL-W1", c =>
        {
            // 清区（比放墙范围大一圈，对照墙那一格也要先清）+ 放墙（玩家出生点旁）。
            for (var i = _baseX; i <= _baseX + 8; i++)
                for (var j = _floor - 6; j < _floor; j++)
                    if (Main.tile[i, j].wall != 0) WorldGen.KillWall(i, j, false);

            for (var i = _baseX; i < _baseX + 4; i++)
                for (var j = _floor - 3; j < _floor; j++)
                    WorldGen.PlaceWall(i, j, (ushort)type, true);

            WorldGen.PlaceWall(_baseX + 6, _floor - 1, (ushort)plainType, true);

            var tile = Main.tile[_baseX + 1, _floor - 2];
            c.True(tile.wall == type, $"放墙 wall={tile.wall}（期望 {type}）");
            c.True(Main.tile[_baseX + 6, _floor - 1].wall == plainType, "对照墙也放上了");
        });

        PT.Case("WALL-W2", c =>
        {
            // 边框：Framing.WallFrame 走 WallFrame 钩子（作者改 frameNumber 的入口）。
            var calls = Content.TestWall.FrameCalls;
            Framing.WallFrame(_baseX + 1, _floor - 2, true);
            c.True(Content.TestWall.FrameCalls > calls, $"WallFrame 调用={Content.TestWall.FrameCalls - calls}");
        });

        PT.Case("WALL-W3", c =>
        {
            // 挖墙链 A：失败的一击（只出尘土）——KillWall 记数、NumDust 2 粒、声音一次、墙还在。
            var x = _baseX + 2;
            var y = _floor - 2;
            var kills = Content.TestWall.Kills;
            var dusts = Content.TestWall.Dusts;
            var sounds = Content.TestWall.Sounds;
            WorldGen.KillWall(x, y, true);
            c.True(Content.TestWall.Kills == kills + 1, $"KillWall={Content.TestWall.Kills - kills}");
            c.True(Content.TestWall.LastFail, "fail 标记");
            c.True(Content.TestWall.Dusts == dusts + 2, $"失败尘土={Content.TestWall.Dusts - dusts}（NumDust 给 2）");
            c.True(Content.TestWall.Sounds == sounds + 1, $"KillSound={Content.TestWall.Sounds - sounds}");
            c.True(Main.tile[x, y].wall == type, "墙还在");
        });

        PT.Case("WALL-W4", c =>
        {
            // 挖墙链 B：真挖掉 —— 掉放置物品、5 粒尘土、墙没了。
            var x = _baseX + 2;
            var y = _floor - 2;
            ClearItems();
            var dusts = Content.TestWall.Dusts;
            var drops = Content.TestWall.Drops;
            WorldGen.KillWall(x, y, false);
            var dropped = DroppedNear(x, y);
            c.True(Main.tile[x, y].wall == 0, "墙已挖掉");
            c.True(Content.TestWall.Drops == drops + 1, $"Drop 调用={Content.TestWall.Drops - drops}");
            c.Eq(Content.TestWall.LastDrop, ModContent.ItemType<Content.TestWallItem>(), "掉落 = 放置物品");
            c.Eq(dropped, Content.TestWall.LastDrop, "地上有掉落");
            c.True(Content.TestWall.Dusts >= dusts + 5, $"尘土={Content.TestWall.Dusts - dusts}（NumDust 给 5）");
            ClearItems();

            // 对照墙：ItemDrop 属性决定掉石头。
            WorldGen.KillWall(_baseX + 6, _floor - 1, false);
            c.Eq(DroppedNear(_baseX + 6, _floor - 1), ItemID.StoneBlock, "对照墙掉石头");
            ClearItems();
        });
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

    private static int DroppedNear(int x, int y)
    {
        var items = Main.item;
        if (items is null) return 0;
        for (var k = 0; k < items.Length; k++)
        {
            var item = items[k];
            if (item is not null && item.active &&
                System.Math.Abs(item.position.X / 16 - x) < 4 && System.Math.Abs(item.position.Y / 16 - y) < 4)
                return item.type;
        }
        return 0;
    }
}
