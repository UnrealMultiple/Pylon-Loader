using System;
using Il2CppMicrosoft.Xna.Framework;
using PipelineTest.Content;
using Pylon.ModLoader;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;

namespace PipelineTest.Tests;

internal static class TileInteractionTests
{
    public static void Register() => PT.Group("tile-interaction-w", Phase.World, Run);

    private static void Run()
    {
        var player = Main.player[Main.myPlayer];
        if (player is null || !player.active)
        {
            PT.SkipCase("TILE-I1", "玩家还没就绪");
            return;
        }
        var x = (int)(player.Center.X / 16f);
        var y = (int)(player.Center.Y / 16f);
        var type = ModContent.TileType<TestTileInteraction>();
        var saved = new (ushort Type, short Header, short X, short Y, byte B1, byte B2, byte B3)[4];
        for (var i = 0; i < 2; i++)
        for (var j = 0; j < 2; j++)
        {
            var t = Main.tile[x + i, y + j];
            saved[i * 2 + j] = (t.type, t.sTileHeader, t.frameX, t.frameY, t.bTileHeader, t.bTileHeader2, t.bTileHeader3);
        }
        TileInteractionProbe.Active = true;
        try
        {
            void Prepare()
            {
                for (var i = 0; i < 2; i++)
                for (var j = 0; j < 2; j++)
                {
                    var t = Main.tile[x + i, y + j];
                    t.type = (ushort)type;
                    t.active(true);
                    t.frameX = (short)(i * 18);
                    t.frameY = (short)(j * 18);
                }
            }

            PT.Case("TILE-I1", c =>
            {
                TileInteractionProbe.Reset();
                Prepare();
                c.True(!Chest.IsLocked(x, y), "自定义初始锁状态");
                c.True(Chest.Lock(x, y), "作者允许上锁");
                c.Eq((int)Main.tile[x, y].frameX, 18, "ref 帧偏移写回");
                c.Eq((int)Main.tile[x + 1, y + 1].frameX, 36, "默认动作更新同类型2x2");
                c.True(Chest.IsLocked(x, y), "自定义锁状态");
                c.True(Chest.Unlock(x, y), "作者允许开锁");
                c.Eq((int)Main.tile[x, y].frameX, 0, "开锁ref偏移");
                c.Eq(TileInteractionProbe.Locks, 1, "Lock只调用一次");
                c.Eq(TileInteractionProbe.Unlocks, 1, "Unlock只调用一次");
            });
            PT.Case("TILE-I2", c =>
            {
                TileInteractionProbe.Reset();
                Prepare();
                TileInteractionProbe.Allow = false;
                c.True(!Chest.Lock(x, y), "上锁否决");
                c.Eq((int)Main.tile[x, y].frameX, 0, "否决不搬帧");
                TileInteractionProbe.Allow = true;
                TileInteractionProbe.Manual = true;
                c.True(Chest.Lock(x, y), "手动上锁");
                c.Eq((int)Main.tile[x, y].frameX, 123, "manual保留作者动作不二次搬帧");
                c.True(Chest.Unlock(x, y), "手动开锁");
                c.Eq((int)Main.tile[x, y].frameX, 0, "manual开锁写入");
                TileInteractionProbe.Throw = true;
                c.True(!Chest.Lock(x, y), "异常上锁不放行");
                c.True(!Chest.Unlock(x, y), "异常开锁不放行");
                c.True(!Chest.IsLocked(x, y), "锁状态异常返回false");
            });
            PT.Case("TILE-I3", c =>
            {
                TileInteractionProbe.Reset();
                Prepare();
                TileInteractionProbe.Allow = false;
                c.True(!WorldGen.SlopeTile(x, y, 1, true, true), "Mod锤坡否决");
                c.Eq(TileInteractionProbe.Order, "M", "Mod否决短路Global");
                TileInteractionProbe.Reset();
                TileInteractionProbe.GlobalVeto = true;
                c.True(!WorldGen.PoundTile(x, y), "Global半砖否决");
                c.Eq(TileInteractionProbe.Order, "MG", "Mod先Global后");
                TileInteractionProbe.Reset();
                TileInteractionProbe.Throw = true;
                TileInteractionProbe.GlobalVeto = true;
                c.True(!WorldGen.SlopeTile(x, y, 1, true, true), "Mod异常仍能Global否决");
                c.Eq(TileInteractionProbe.Order, "MG", "逐作者异常隔离");
                TileInteractionProbe.Reset();
                var vanilla = Main.tile[x, y];
                vanilla.type = TileID.Stone;
                TileInteractionProbe.GlobalVeto = true;
                c.True(!WorldGen.PoundTile(x, y), "Global也否决原版方块");
                c.Eq(TileInteractionProbe.Order, "G", "原版不调用Mod回调");
            });
            PT.Case("TILE-I4", c =>
            {
                TileInteractionProbe.Reset();
                Prepare();
                var sitting = PlayerSittingHelper.GetSittingTargetInfo(player, x, y,
                    out var direction, out var position, out var seatOffset, out var extra);
                c.True(sitting, "原版允许自定义座位");
                if (sitting)
                {
                    c.Eq(TileInteractionProbe.Sitting, 1, "坐下作者回调");
                    c.Eq(direction, -1, "坐下方向输出");
                    c.Eq(position, new Vector2(169, 341), "锚点/朝向偏移/最终偏移写回");
                    c.Eq(seatOffset, new Vector2(6, 7), "座位可视偏移");
                    c.True(extra.IsAToilet, "额外座位信息写回");
                }
                var sleeping = PlayerSleepingHelper.GetSleepingTargetInfo(x, y,
                    out direction, out var anchor, out var visualOffset);
                c.True(sleeping, "原版允许自定义睡眠目标");
                if (sleeping)
                {
                    c.Eq(TileInteractionProbe.Sleeping, 1, "睡眠作者回调");
                    c.Eq(direction, -1, "睡眠方向输出");
                    c.Eq(anchor, new Vector2(169, 357), "睡眠多一行锚点写回");
                    c.Eq(visualOffset, new Vector2(6, 7), "睡眠可视偏移");
                }
            });
        }
        finally
        {
            TileInteractionProbe.Active = false;
            TileInteractionProbe.Reset();
            for (var i = 0; i < 2; i++)
            for (var j = 0; j < 2; j++)
            {
                var t = Main.tile[x + i, y + j];
                var s = saved[i * 2 + j];
                t.type = s.Type;
                t.sTileHeader = s.Header;
                t.frameX = s.X;
                t.frameY = s.Y;
                t.bTileHeader = s.B1;
                t.bTileHeader2 = s.B2;
                t.bTileHeader3 = s.B3;
            }
        }
    }
}
