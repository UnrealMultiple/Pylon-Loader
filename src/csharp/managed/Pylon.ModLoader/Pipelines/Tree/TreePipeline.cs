using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Textures;
using ReLogic.Content;
using Terraria;
using Terraria.Enums;

namespace Pylon.ModLoader;

/// <summary>
/// 树管线：发号（32 起）+ 扩 <c>TreeTop</c> / <c>TreeBranch</c> / <c>Wood</c> 三张表 + 写作者贴图，
/// 并按 <c>GrowsOnTileId</c> 建"地面方块 → 树"的映射。世界生成时让 mod 树长大另说，见文档。
/// </summary>
internal sealed class TreePipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static TreePipeline Instance { get; } = new();

    public string Name => "tree";

    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        On.Terraria.WorldGen.ShakeTree += ShakeTree;
        On.Terraria.WorldGen.KillTile_GetItemDrops += KillTileDrops;
        On.Terraria.WorldGen.GetTreeLeaf += TreeLeafHook;
        On.Terraria.WorldGen.KillTile_GetTreeDrops += TreeDropsHook;
        On.Terraria.WorldGen.KillTile_MakeTileDust += TreeDustHook;
        On.Terraria.WorldGen.AttemptToGrowTreeFromSapling += GrowSaplingHook;
        On.Terraria.WorldGen.GetTreeType_1 += TreeTypeOfTile;
        On.Terraria.WorldGen.IsTileTypeFitForTree += FitForTree;
        On.Terraria.WorldGen.GrowTree += GrowTreeHook;
        On.Terraria.WorldGen.TileFrame += TileFrameHook;
        On.Terraria.TileObject.CanPlace += SaplingPlaceHook;

        Log.Info("[tree] 已挂（风格号 / 贴图表 / 摇树 / 掉落与橡果 / 叶子 / 尘土 / 种苗 / 可种判据 / 长大即换外观）");
        return true;
    }

    /// <summary>摇树：这一列是 mod 树就交给作者（ref createLeaves 能关掉原版掉落）。</summary>
    private static void ShakeTree(On.Terraria.WorldGen.orig_ShakeTree orig, int i, int j)
    {
        if (TreeTable.Count == 0)
        {
            orig(i, j);
            return;
        }

        var tree = TreeAt(i, j);
        if (tree is null)
        {
            orig(i, j);
            return;
        }

        var createLeaves = true;
        bool vanilla;
        try
        {
            vanilla = tree.Shake(i, j, ref createLeaves);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[tree] {tree.Name}.Shake 异常：{Safe.Describe(e)}");
            vanilla = true;
        }

        if (vanilla) orig(i, j);
    }

    /// <summary>砍树掉木头：这一列是 mod 树就换成作者声明的木头（原版只认自己的风格号）。</summary>
    private static void KillTileDrops(On.Terraria.WorldGen.orig_KillTile_GetItemDrops orig, int x, int y, Terraria.Tile tileCache,
        ref int dropItem, ref int dropItemStack, ref int secondaryItem, ref int secondaryItemStack, ref bool noPrefix, bool includeLargeObjectDrops)
    {
        orig(x, y, tileCache, ref dropItem, ref dropItemStack, ref secondaryItem, ref secondaryItemStack, ref noPrefix, includeLargeObjectDrops);

        if (dropItem != Terraria.ID.ItemID.Wood || TreeTable.Count == 0) return;
        if (TreeAt(x, y) is not { } tree) return;

        try
        {
            var wood = tree.DropWood();
            if (wood > 0 && wood != Terraria.ID.ItemID.Wood) dropItem = wood;
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[tree] {tree.Name}.DropWood 异常：{Safe.Describe(e)}");
        }
    }

    /// <summary>树顶叶子样式：作者给了非负的 TreeLeaf 就把 pass 换成它（负数 = 让原版自己判）。</summary>
    private static void TreeLeafHook(On.Terraria.WorldGen.orig_GetTreeLeaf orig, int x, Terraria.Tile topTile,
                                     Terraria.Tile groundTile, ref int treeHeight, ref int treeFrame, ref int passStyle)
    {
        orig(x, topTile, groundTile, ref treeHeight, ref treeFrame, ref passStyle);

        if (TreeTable.ForGround(groundTile.type) is not { } tree) return;

        try
        {
            var leaf = tree.TreeLeaf();
            if (leaf >= 0) passStyle = leaf;
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[tree] {tree.Name}.TreeLeaf 异常：{Safe.Describe(e)}");
        }
    }

    /// <summary>砍树的第二掉落：橡果给不给、掉不掉，由作者说了算（原版逻辑照跑，只修我们树的两格）。</summary>
    private static void TreeDropsHook(On.Terraria.WorldGen.orig_KillTile_GetTreeDrops orig, int i, int j,
                                      Terraria.Tile tileCache, ref bool dropIsWood, ref bool bonusWood,
                                      ref int dropItem, ref int secondaryItem)
    {
        orig(i, j, tileCache, ref dropIsWood, ref bonusWood, ref dropItem, ref secondaryItem);

        if (TreeAt(i, j) is not { } tree) return;

        try
        {
            if (dropIsWood)
            {
                var wood = tree.DropWood();
                if (wood > 0) dropItem = wood;
            }

            if (!tree.CanDropAcorn())
            {
                if (secondaryItem == Terraria.ID.ItemID.Acorn) secondaryItem = 0;
            }
            else if (secondaryItem == 0 && tileCache.frameX >= 22 && tileCache.frameY >= 198
                     && Main.netMode != 1 && WorldGen.genRand.Next(2) == 0)
            {
                secondaryItem = Terraria.ID.ItemID.Acorn;
            }
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[tree] {tree.Name} 掉落判定异常：{Safe.Describe(e)}");
        }
    }

    /// <summary>砍树打出的尘土：作者给了非默认的 CreateDust 就换他的（-1 = 不出尘）。</summary>
    private static int TreeDustHook(On.Terraria.WorldGen.orig_KillTile_MakeTileDust orig, int i, int j,
                                    Terraria.Tile tileCache)
    {
        if (tileCache.type == Terraria.ID.TileID.Trees && TreeAt(i, j) is { } tree)
        {
            try
            {
                var dust = tree.CreateDust();
                if (dust >= 0)
                {
                    Terraria.Dust.NewDust(new Il2CppMicrosoft.Xna.Framework.Vector2(i * 16, j * 16), 16, 16, dust, 0, 0, 0,
                        default, 1);
                    return 6000;
                }
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[tree] {tree.Name}.CreateDust 异常：{Safe.Describe(e)}");
            }
        }

        return orig(i, j, tileCache);
    }

    /// <summary>
    /// 树苗长大：苗是 mod 方块就交给 <see cref="ModTile.GrowSapling"/>，原版的生长分支不认识 mod 的苗。
    /// 本构建没有 <c>TileID.Sets.TreeSapling</c>（那是 tML 加的集），判据用"苗位置上是 mod 方块"。
    /// </summary>
    private static bool GrowSaplingHook(On.Terraria.WorldGen.orig_AttemptToGrowTreeFromSapling orig, int x, int y,
                                        bool underground, int treeHeightAddon, bool ignoreWalls)
    {
        var tile = Main.tile[x, y];
        if (tile is { } t && t.active()
            && t.type != Terraria.ID.TileID.Saplings
            && ModContent.Find(ContentKind.Tile, t.type) is ModTile sapling)
        {
            // 多人客户端不替别人长树（服务器与世界生成负责）。
            if (Main.netMode == 1) return false;

            try
            {
                var grew = sapling.GrowSapling(x, y, underground, treeHeightAddon, ignoreWalls);
                if (grew) StyleColumn(x, y);
                return grew;
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[tree] {sapling.Name}.GrowSapling 异常：{Safe.Describe(e)}");
                return false;
            }
        }

        return orig(x, y, underground, treeHeightAddon, ignoreWalls);
    }

    /// <summary>
    /// 种树苗：玩家把橡果种在 mod 树的地面方块上时，游戏只认识 <c>TileID.Saplings</c> 原版苗，
    /// 作者声明了 <see cref="ModTree.SaplingGrowthType"/> 就换成它自己的苗（含 style）；
    /// 没声明的 mod 树保持原版苗——放行原版逻辑。
    /// </summary>
    private static bool SaplingPlaceHook(On.Terraria.TileObject.orig_CanPlace orig, int x, int y, int type, int style,
                                         int dir, ref TileObject objectData, bool onlyCheck,
                                         Il2CppSystem.Nullable<int> forcedRandom)
    {
        if (type == Terraria.ID.TileID.Saplings && TreeTable.Count > 0)
        {
            var below = Main.tile[x, y + 1];
            if (below is { } ground && ground.active() && TreeTable.ForGround(ground.type) is { } tree)
            {
                try
                {
                    var sapling = style;
                    var replacement = tree.SaplingGrowthType(ref sapling);
                    // 作者基类默认实现返回原版苗且把 style 归零——那不是作者的苗，别替换，
                    // 保持游戏自己按地形挑好的 style，否则原版苗在错 style 下长成怪树。
                    if (replacement != Terraria.ID.TileID.Saplings || sapling != 0)
                    {
                        type = replacement;
                        style = sapling;
                    }
                }
                catch (Exception e)
                {
                    ContentErrors.Report($"[tree] {tree.Name}.SaplingGrowthType 异常：{Safe.Describe(e)}");
                }
            }
        }

        return orig(x, y, type, style, dir, ref objectData, onlyCheck, forcedRandom);
    }

    /// <summary>
    /// (i, j) 所在的树：先按地面方块查——两端按方块算得出同一棵树，不依赖本机 <c>Main.treeStyle</c>（那是原生写的视觉状态）；
    /// 查不到再退回作者自己 SetStyle 过的列。
    /// </summary>
    private static ModTree? TreeAt(int i, int j)
    {
        if (TreeTable.Count == 0) return null;

        try
        {
            WorldGen.GetTreeBottom(i, j, out var x, out var bottom);
            if ((uint)x < (uint)Main.maxTilesX && (uint)bottom < (uint)Main.maxTilesY)
            {
                var ground = Main.tile[x, bottom];
                if (ground.active() && TreeTable.ForGround(ground.type) is { } byGround) return byGround;
            }
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[tree] GetTreeBottom 失败：{Safe.Describe(e)}");
        }

        var styles = Main.treeStyle;
        if (styles is null || i < 0 || i >= styles.Length) return null;

        return TreeTable.Of(styles[i]);
    }

    // ===================== 世界生成 / 客户端外观 =====================

    /// <summary>
    /// mod 地面算哪种原版树：世界的种树、砍树后的重新造型都问这条。
    /// 作者给一个最接近的原版树型（<see cref="ModTree.CountsAsTreeType"/>），生成算法照旧，不用重写。
    /// </summary>
    private static TreeTypes TreeTypeOfTile(On.Terraria.WorldGen.orig_GetTreeType_1 orig, int tileType)
    {
        if (TreeTable.ForGround(tileType) is { } tree) return tree.CountsAsTreeType;

        return orig(tileType);
    }

    /// <summary>mod 地面是可以长树的地：原版按草地类型列名单，mod 方块不在里面。</summary>
    private static bool FitForTree(On.Terraria.WorldGen.orig_IsTileTypeFitForTree orig, ushort type) =>
        TreeTable.ForGround(type) is not null || orig(type);

    /// <summary>
    /// 世界生成（以及任何直接调 <c>GrowTree</c> 的路径）把树种好之后：地面是 mod 方块就把这一列换成 mod 外观。
    /// 不接管生长算法——树已经长好了，只补风格号。
    /// </summary>
    private static bool GrowTreeHook(On.Terraria.WorldGen.orig_GrowTree orig, int i, int y, int treeHeightAddon,
                                     bool ignoreWalls)
    {
        var grew = orig(i, y, treeHeightAddon, ignoreWalls);
        if (grew) StyleColumn(i, y);

        return grew;
    }

    /// <summary>
    /// 客户端收到别人的树（或世界加载逐格刷帧）时 <c>Main.treeStyle</c> 由原生按原版规则写，
    /// mod 地面会落到一个原版风格上；这里在方块帧更新之后按地面补一次。只对树方块生效，别的方块只多一次读。
    /// </summary>
    private static void TileFrameHook(On.Terraria.WorldGen.orig_TileFrame orig, int i, int j, bool resetFrame,
                                      bool noBreak)
    {
        orig(i, j, resetFrame, noBreak);

        if (TreeTable.Count == 0) return;

        var tile = Main.tile[i, j];
        if (!tile.active() || tile.type != Terraria.ID.TileID.Trees) return;

        StyleColumn(i, j);
    }

    /// <summary>把 (i, j) 这一列的树换成它地面声明的 mod 外观；不是 mod 地面就什么都不做。</summary>
    private static void StyleColumn(int i, int j)
    {
        if (TreeTable.Count == 0) return;

        try
        {
            WorldGen.GetTreeBottom(i, j, out var x, out var bottom);
            if ((uint)x >= (uint)Main.maxTilesX || (uint)bottom >= (uint)Main.maxTilesY) return;

            var ground = Main.tile[x, bottom];
            if (!ground.active()) return;
            if (TreeTable.ForGround(ground.type) is not { } tree) return;

            SetStyle(x, tree);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[tree] 换外观失败：{Safe.Describe(e)}");
        }
    }

    public void Refresh() => TreeTable.Assign();

    /// <summary>安装期一次：发号 → 扩表 → 写贴图。</summary>
    internal void Build()
    {
        TreeTable.Assign();

        var count = TreeTable.Count;
        if (count > 0)
        {
            // 每个风格：树顶 3 张、树枝 2 张、树干 1 张。
            new TypeTables.TableGrower(TypeTables.TableKind.TreeTop, count * 3).NextBatch(count * 3, Log.Info);
            new TypeTables.TableGrower(TypeTables.TableKind.TreeBranch, count * 2).NextBatch(count * 2, Log.Info);
            new TypeTables.TableGrower(TypeTables.TableKind.TreeWood, count).NextBatch(count, Log.Info);
        }

        var written = 0;
        foreach (var tree in TreeTable.All)
        {
            written += Write(tree);

            try
            {
                tree.SetStaticDefaults();
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[tree] {tree.Name}.SetStaticDefaults 异常：{Safe.Describe(e)}");
            }
        }

        if (count > 0) Log.Info($"[tree] 安装：{count} 棵树（写槽 {written}）");
    }

    private static int Write(ModTree tree)
    {
        var style = tree.Type;
        var wood = ReadAsset(tree, () => tree.GetTexture());
        if (wood is not null && TextureSlots.TryWrite(TextureTable.TreeWood, style, wood)) { }

        var tops = ReadArray(tree, () => tree.GetTopTextures());
        if (tops is not null)
        {
            for (var index = 0; index < tops.Length && index < 3; index++)
            {
                if (tops[index] is { } top) TextureSlots.TryWrite(TextureTable.TreeTop, style * 3 + index, top);
            }
        }

        var branches = ReadArray(tree, () => tree.GetBranchTextures());
        if (branches is not null)
        {
            for (var index = 0; index < branches.Length && index < 2; index++)
            {
                if (branches[index] is { } branch) TextureSlots.TryWrite(TextureTable.TreeBranch, style * 2 + index, branch);
            }
        }

        return 1;
    }

    private static Asset<Texture2D>? ReadAsset(ModTree tree, Func<Asset<Texture2D>> read)
    {
        try
        {
            return read();
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[tree] {tree.Name}.GetTexture 异常：{Safe.Describe(e)}");
            return null;
        }
    }

    private static Asset<Texture2D>[]? ReadArray(ModTree tree, Func<Asset<Texture2D>[]> read)
    {
        try
        {
            return read();
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[tree] {tree.Name}.GetTopTextures/GetBranchTextures 异常：{Safe.Describe(e)}");
            return null;
        }
    }

    /// <summary>把这一列树的风格改成 mod 的（作者在自己的树长好后调）。</summary>
    public static bool SetStyle(int x, ModTree tree)
    {
        if (tree is null || !tree.Registered) return false;

        var styles = Main.treeStyle;
        if (styles is null || x < 0 || x >= styles.Length) return false;

        styles[x] = tree.Type;
        return true;
    }
}
