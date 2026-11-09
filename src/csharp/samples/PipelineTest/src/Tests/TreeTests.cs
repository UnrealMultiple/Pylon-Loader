using System;
using Pylon.ModLoader;
using Terraria;
using Terraria.ID;
using Terraria.ObjectData;

namespace PipelineTest.Tests;

/// <summary>
/// 管线 22：Tree。
/// Ready：风格号、三张贴图表扩长、地面映射、贴图写槽。
/// World：铺测试地面 → 橡果种成自己的苗 → 长大成树（树顶贴图用 mod 的）→ 摇树 → 砍树掉木头与尘土 → 再种再长。
/// </summary>
internal static class TreeTests
{
    private static int _x, _floor, _style;
    private static bool _prepared;

    public static void Register()
    {
        PT.Group("tree", Phase.Ready, RunReady);
        PT.Group("tree-w", Phase.World, RunWorld);
    }

    private static void RunReady()
    {
        PT.Case("TREE-R1", c =>
        {
            var tree = ModContent.GetInstance<Content.TestTree>();
            c.NotNull(tree, "TestTree 实例");
            c.True(Content.TestTree.Setups >= 1, $"SetStaticDefaults 调用={Content.TestTree.Setups}");
            _style = tree.Type;
            c.True(_style >= 32, $"风格号={_style}（应为 32 起）");
            c.True(WorldGen.IsTileTypeFitForTree((ushort)ModContent.TileType<Content.TestTreeGround>()), "mod 地面可长树");
        });

        PT.Case("TREE-T1", c =>
        {
            c.True(_style >= 32, $"已发号={_style}");

            var tops = Terraria.GameContent.TextureAssets.TreeTop;
            var branches = Terraria.GameContent.TextureAssets.TreeBranch;
            var wood = Terraria.GameContent.TextureAssets.Wood;
            // 原版：树顶 32、树枝 32、树干 7；每 mod 树各占 1 槽。
            c.True(tops is { Length: >= 33 }, $"TreeTop 表长={tops?.Length}（应 >= 33）");
            c.True(branches is { Length: >= 33 }, $"TreeBranch 表长={branches?.Length}（应 >= 33）");
            c.True(wood is { Length: >= 8 }, $"Tree 表长={wood?.Length}（应 >= 8）");

            // mod 风格的槽被写过（非 null 即算，贴图内容靠真机肉眼/后续绘制验证）。
            // 树贴图表没有公开计数；表长本身 > 原版风格数就是扩表成功的证据。
            c.True(_style < tops!.Length, $"mod 树顶槽={_style}（表长 {tops.Length}）");
            c.True(_style - 32 + 7 < wood!.Length, "mod 树干槽");
        });
    }

    private static void RunWorld()
    {
        var player = Main.player[Main.myPlayer];
        if (player is null || !player.active)
        {
            PT.SkipCase("TREE-W1", "玩家还没就绪");
            return;
        }

        if (!_prepared)
        {
            _x = (int)(player.Center.X / 16f) + 12;
            _floor = FindFloor(_x);
            Prepare(_x, _floor);
            _prepared = true;
        }

        var ground = ModContent.TileType<Content.TestTreeGround>();
        var sapling = ModContent.TileType<Content.TestTreeSapling>();

        PT.Case("TREE-W1", c =>
        {
            // 橡果种上去会被换成 mod 的苗（SaplingGrowthType）。
            var none = new Il2CppSystem.Nullable<int>();
            var can = TileObject.CanPlace(_x, _floor - 1, TileID.Saplings, 0, 1, out var data, false, none);
            var placed = can && TileObject.Place(data);
            var tile = Main.tile[_x, _floor - 1];
            c.True(placed && tile.active() && tile.type == sapling,
                $"橡果换苗 placed={placed} type={tile.type}（期望 {sapling}）");
        });

        PT.Case("TREE-W2", c =>
        {
            // 反复尝试长大：苗没了（上次尝试失败被破坏）就重种；树干底部应变成 Trees。
            var saplingType = ModContent.TileType<Content.TestTreeSapling>();
            var grew = false;
            for (var attempt = 0; attempt < 20 && !grew; attempt++)
            {
                var t = Main.tile[_x, _floor - 1];
                if (!t.active() || t.type != saplingType)
                {
                    var none = new Il2CppSystem.Nullable<int>();
                    if (TileObject.CanPlace(_x, _floor - 1, TileID.Saplings, 0, 1, out var data, false, none))
                        TileObject.Place(data);
                }
                grew = WorldGen.AttemptToGrowTreeFromSapling(_x, _floor - 1, false, 4, true);
            }

            var b = Main.tile[_x, _floor - 1];
            c.True(grew, $"长大 grew={grew} grows={Content.TestTreeSapling.Grows}");
            c.True(b.active() && b.type == TileID.Trees, $"树干底部 type={b.type}");
            if (grew) PT.Info($"树已长成（{_x},{_floor} 顶部），肉眼可见 mod 树皮与树冠");
        });

        PT.Later("TREE-W3", 30, c =>
        {
            // 树顶贴图走 mod 的：GetCommonTreeFoliageData 报告的风格应是 mod 风格（>= 原版 32）。
            var top = TreeTop(_x);
            var frame = 0;
            var style = 0;
            var found = WorldGen.GetCommonTreeFoliageData(_x, top, 0, ref frame, ref style, out var floorY, out _, out _);
            c.True(found, $"树顶可解析 top={top}");
            c.True(style >= 32, $"树顶风格={style}（应 >= 32 即 mod 风格）");
            c.Eq(floorY, _floor, "树顶解析出的地面行");
        });

        PT.Later("TREE-W4", 45, c =>
        {
            // 摇树：作者 Shake 被调；同一棵树两次摇只生效一次（shake 标记 +1）。
            var calls = Content.TestTree.Shakes;
            var marked = WorldGen.numTreeShakes;
            WorldGen.ShakeTree(_x, _floor - 2);
            WorldGen.ShakeTree(_x, _floor - 2);
            c.Eq(Content.TestTree.Shakes - calls, 1, "Shake 调用次数");
            c.True(WorldGen.numTreeShakes - marked == 1, $"摇树标记={WorldGen.numTreeShakes - marked}");
        });

        PT.Later("TREE-W5", 60, c =>
        {
            // 砍树：掉 Gel（DropWood）+ 石头尘土（CreateDust）。
            var woods = Content.TestTree.Woods;
            var dusts = Content.TestTree.Dusts;
            WorldGen.KillTile(_x, _floor - 1, false, false, false);

            var gel = 0;
            for (var k = 0; k < Main.item.Length; k++)
            {
                var item = Main.item[k];
                if (item.active && item.type == ItemID.Gel) gel += item.stack;
            }

            c.True(Content.TestTree.Woods > woods, $"DropWood 调用={Content.TestTree.Woods - woods}");
            c.True(Content.TestTree.Dusts > dusts, $"CreateDust 调用={Content.TestTree.Dusts - dusts}");
            c.True(gel > 0, $"掉落 Gel={gel}");

            // 挖完树干，原版重新造型不会把 mod 地面认成非树地（GetTreeType 走 mod）。
            c.Eq((int)WorldGen.GetTreeType((ushort)ground), (int)Terraria.Enums.TreeTypes.Forest, "mod 地算 Forest 树型");
        });

        PT.Later("TREE-W6", 90, c =>
        {
            // 再种再长：验证 IsTileTypeFitForTree 放行 mod 地面。
            var none = new Il2CppSystem.Nullable<int>();
            var can = TileObject.CanPlace(_x, _floor - 1, TileID.Saplings, 0, 1, out var data, false, none);
            var placed = can && TileObject.Place(data);
            var grew = false;
            for (var attempt = 0; attempt < 10 && !grew; attempt++)
                grew = WorldGen.AttemptToGrowTreeFromSapling(_x, _floor - 1, false, 4, true);
            c.True(placed && grew, $"再种再长 placed={placed} grew={grew}");
            PT.Info($"树已再长（{_x},{_floor}），肉眼可见 mod 树皮与树冠；摇树/砍树计数见上文");
        });
    }

    private static int FindFloor(int column)
    {
        for (var y = 50; y < Main.maxTilesY - 10; y++)
        {
            var t = Main.tile[column, y];
            if (t.active() && Main.tileSolid[t.type]) return y;
        }
        return Main.spawnTileY;
    }

    /// <summary>清出一片 7 宽的空地，铺上测试地面。</summary>
    private static void Prepare(int x, int floor)
    {
        var ground = ModContent.TileType<Content.TestTreeGround>();
        for (var i = x - 3; i <= x + 3; i++)
        {
            for (var j = floor - 30; j < floor; j++)
                WorldGen.KillTile(i, j, false, false, true);
            WorldGen.KillTile(i, floor, false, false, true);
            WorldGen.PlaceTile(i, floor, ground, true, true, -1, 0);
        }

        for (var k = 0; k < Main.item.Length; k++)
        {
            var item = Main.item[k];
            if (item.active) item.TurnToAir();
        }

        PT.Info($"树测试场地已铺好（x={x} 地面 y={floor} ground={ground}）");
    }

    private static int TreeTop(int x)
    {
        var y = _floor - 1;
        while (y > 10)
        {
            var t = Main.tile[x, y - 1];
            if (!t.active() || t.type != TileID.Trees) break;
            y--;
        }
        return y;
    }
}
