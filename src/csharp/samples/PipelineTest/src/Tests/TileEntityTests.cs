using System;
using Pylon.ModLoader;
using Terraria;
using Terraria.DataStructures;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Contracts;
using Pylon.ModLoader.SaveData;

namespace PipelineTest.Tests;

/// <summary>
/// 方块实体管线（World）：注册面、实体建摘（作者自理，直调 PlaceTile 不派发 PlaceInWorld）、每帧回调（含前置/后置）、手动放置/摘除 API。
/// </summary>
internal static class TileEntityTests
{
    public static void Register() => PT.Group("tileentity", Phase.World, Run);

    private static void Run()
    {
        var player = Main.player[Main.myPlayer];
        if (player is null || !player.active)
        {
            PT.SkipCase("TE-1", "玩家还没就绪");
            return;
        }

        var tileType = ModContent.TileType<Content.TestEntityTile>();
        var entity = ModContent.GetInstance<Content.TestTileEntity>();
        var x = (int)(player.Center.X / 16) + 4;
        var y = (int)(player.Center.Y / 16) + 2;

        PT.Case("TE-1", c =>
        {
            c.True(tileType > 0, $"方块类型号={tileType}");
            c.NotNull(entity, "TestTileEntity 实例");
            if (entity is null) return;

            c.True(entity.Type >= 0, $"实体类型号={entity.Type}");
            c.True(entity.RequiresUpdates, "Update 被认成钩子（覆写过才进更新循环）");
        });

        PT.Case("TE-2", c =>
        {
            if (entity is null) { c.Fail("没有 TestTileEntity 实例"); return; }

            // 清格后放方块，实体由作者 API 建（直调 PlaceTile 不派发 PlaceInWorld，两边同款固有语义）。
            WorldGen.KillTile(x, y, false, false, false);
            var before = ModTileEntity.CountInWorld();
            WorldGen.PlaceTile(x, y, tileType, true, false, -1, 0);

            c.True(Main.tile[x, y].type == tileType, $"方块已放下 type={Main.tile[x, y].type}");
            var id = entity.Place(x, y);
            c.True(id >= 0, $"实体已建 id={id}");
            c.True(ModTileEntity.CountInWorld() == before + 1, $"世界里实体数={ModTileEntity.CountInWorld()}（之前 {before}）");
            c.True(ModTileEntity.ByPosition.ContainsKey(new Point16(x, y)), "按格索引里有");
            c.True(id >= 0 && ModTileEntity.ByID.ContainsKey(id), "按 ID 索引里有");
        });

        PT.Case("TE-3", c =>
        {
            _ = c;
            if (entity is null) return;

            var updates = Content.TestTileEntity.Updates;
            var pres = Content.TestTileEntity.PreGlobals;
            var posts = Content.TestTileEntity.PostGlobals;

            PT.Later("TE-3", 15, cc =>
            {
                cc.True(Content.TestTileEntity.Updates > updates, $"Update 次数={Content.TestTileEntity.Updates - updates}");
                cc.True(Content.TestTileEntity.PreGlobals > pres, $"PreGlobalUpdate={Content.TestTileEntity.PreGlobals - pres}");
                cc.True(Content.TestTileEntity.PostGlobals > posts, $"PostGlobalUpdate={Content.TestTileEntity.PostGlobals - posts}");

                var id = entity.Find(x, y);
                var live = id >= 0 && ModTileEntity.ByID.TryGetValue(id, out var found)
                    ? found as Content.TestTileEntity
                    : null;
                cc.NotNull(live, "按格取回的实体");
                cc.True(live is not null && live.Ticks > 0, $"每实体状态 Ticks={live?.Ticks}");
            });
        });

        PT.Case("TE-4", c =>
        {
            _ = c;
            if (entity is null) return;

            var kills = Content.TestTileEntity.KillCalls;
            PT.Later("TE-4", 25, cc =>
            {
                WorldGen.KillTile(x, y, false, false, false);
                cc.True(Main.tile[x, y].type == 0, $"方块已拆 type={Main.tile[x, y].type}");
                cc.True(entity.Find(x, y) < 0, $"实体已摘 id={entity.Find(x, y)}");
                cc.True(Content.TestTileEntity.KillCalls == kills + 1, $"OnKill={Content.TestTileEntity.KillCalls - kills}");
                cc.True(!ModTileEntity.ByPosition.ContainsKey(new Point16(x, y)), "按格索引里已没有");
            });
        });

        PT.Case("TE-5", c =>
        {
            _ = c;
            if (entity is null) return;

            PT.Later("TE-5", 40, cc =>
            {
                // 手动放置 / 摘除 API（作者自己管实体时用）。
                WorldGen.PlaceTile(x, y, tileType, true, false, -1, 0);
                var id = entity.Place(x, y);
                cc.True(id >= 0, $"放方块后实体 id={id}");

                var kills = Content.TestTileEntity.KillCalls;
                entity.Kill(x, y);
                cc.True(entity.Find(x, y) < 0, "Kill 之后按格查不到");
                cc.True(Content.TestTileEntity.KillCalls == kills + 1, "Kill 走 OnKill");

                var again = entity.Place(x, y);
                cc.True(again >= 0, $"Place 再建一次 id={again}");
                cc.True(entity.Find(x, y) == again, $"Find={entity.Find(x, y)} 与 Place 返回一致");
                entity.Kill(x, y);
            });
        });

        PT.Case("TE-6", c =>
        {
            _ = c;
            if (entity is null) return;

            PT.Later("TE-6", 55, cc =>
            {
                // 存档 / 同步的数据往返：SaveData → LoadData、NetSend → NetReceive。
                WorldGen.PlaceTile(x + 2, y, tileType, true, false, -1, 0);
                cc.True(Main.tile[x + 2, y].type == tileType, "第二块方块已放下");

                var id = entity.Place(x + 2, y);
                cc.True(id >= 0, $"第二块上也有实体 id={id}");
                if (id < 0) return;

                var live = ModTileEntity.ByID[id];
                var mine = live as Content.TestTileEntity;
                cc.NotNull(mine, "实体类型对得上");
                if (mine is null) return;

                mine.Ticks = 7;

                var tag = new TagCompound();
                mine.SaveData(tag);
                cc.True(tag.GetInt("ticks") == 7, $"SaveData 写入 ticks={tag.GetInt("ticks")}");

                var copy = ModContent.GetInstance<Content.TestTileEntity>()!;
                copy.LoadData(tag);
                cc.True(copy.Ticks == 7, $"LoadData 读回 Ticks={copy.Ticks}");

                var writer = new NetWriter();
                mine.NetSend(writer);
                var reader = new NetReader([.. writer.Values]);
                copy.Ticks = 0;
                copy.NetReceive(reader);
                cc.True(copy.Ticks == 7, $"NetSend/NetReceive 往返 Ticks={copy.Ticks}");
            });
        });

        PT.Case("TE-7", c =>
        {
            _ = c;

            PT.Later("TE-7", 70, cc =>
            {
                var basePath = Main.worldPathName;
                if (basePath.EndsWith(".wld", StringComparison.OrdinalIgnoreCase)) basePath = basePath[..^4];
                var path = basePath + ".wld.pylon";
                Terraria.IO.WorldFile.SaveWorld();
                cc.True(System.IO.File.Exists(path), $"世界侧车已写出：{path}（实体段随世界头/数据一起进二进制旁车，读回由 TE-1..6 的世界重进恢复覆盖）");
            });
        });

        PT.Case("TE-8", c =>
        {
            _ = c;

            PT.Later("TE-8", 85, cc =>
            {
                WorldGen.PlaceTile(x, y, tileType, true, true, -1, 0);
                var id = entity.Place(x, y);
                cc.True(id >= 0, $"实体已放 id={id}");
                if (id < 0) return;

                cc.True(TileEntityTml.TryGet<Content.TestTileEntity>(x, y, out var found), "TryGet 取回实体");
                cc.True(found is not null && found.ID == id, $"TryGet 的 ID={found?.ID} 与 Find 一致");
                entity.Kill(x, y);
            });
        });
    }
}
