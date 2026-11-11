using Pylon.ModLoader.Tables;
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

    public IReadOnlyList<TableRequest> Tables =>
    [
        new(TreeTables.Top, () => TreeTables.TopVanilla + TreeTable.Count),
        new(TreeTables.Branch, () => TreeTables.BranchVanilla + TreeTable.Count),
        new(TreeTables.Wood, () => TreeTables.WoodVanilla + TreeTable.Count),
    ];

    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        On.Terraria.WorldGen.ShakeTree += ShakeTree;
        On.Terraria.WorldGen.KillTile_GetItemDrops += KillTileDrops;
        On.Terraria.WorldGen.GetTreeLeaf += TreeLeafHook;
        On.Terraria.WorldGen.KillTile_GetTreeDrops += TreeDropsHook;
        On.Terraria.WorldGen.KillTile_MakeTileDust += TreeDustHook;
        On.Terraria.WorldGen.AttemptToGrowTreeFromSapling += GrowSaplingHook;
        On.Terraria.WorldGen.GetTreeType_int += TreeTypeOfTile;
        On.Terraria.WorldGen.IsTileTypeFitForTree += FitForTree;
        On.Terraria.WorldGen.GrowTree += GrowTreeHook;
        On.Terraria.WorldGen.TileFrame += TileFrameHook;
        On.Terraria.WorldGen.CheckTree += CheckTreeHook;
        On.Terraria.WorldGen.GetCommonTreeFoliageData += FoliageHook;
        On.Terraria.TileObject.CanPlace += SaplingPlaceHook;

        Log.Info("[tree] 已挂（风格号 / 贴图表 / 摇树 / 掉落与橡果 / 叶子 / 尘土 / 种苗 / 可种判据 / 长大即换外观）");
        return true;
    }

    /// <summary>
    /// 树冠/树枝绘制前，原版用这个函数从树顶格重新解析树的位置与风格；它不认 mod 地面，
    /// 会把 mod 树算成风格 0（森林树）或直接解析失败（树冠不画）。这里从 (i + xoffset, j)
    /// 顺树干往下找地面，是 mod 地面就直接报告 mod 风格与地面行。
    /// </summary>
    private static bool FoliageHook(On.Terraria.WorldGen.orig_GetCommonTreeFoliageData orig, int i, int j,
                                    int xoffset, ref int treeFrame, ref int treeStyle, out int floorY,
                                    out int topWidth, out int topHeight)
    {
        var x = i + xoffset;
        var y = j;
        // 顺树干往下走到地面（树干最多 300 格）。
        for (var n = 0; n < 300; n++)
        {
            var below = Main.tile[x, y + 1];
            if (below.active() && below.type == Terraria.ID.TileID.Trees) y++;
            else break;
        }

        var ground = Main.tile[x, y + 1];
        if (ground.active() && TreeTable.ForGround(ground.type) is { } tree)
        {
            treeStyle = tree.Type;
            floorY = y + 1;
            topWidth = 80;
            topHeight = 80;
            return true;
        }

        return orig(i, j, xoffset, ref treeFrame, ref treeStyle, out floorY, out topWidth, out topHeight);
    }

    /// <summary>
    /// CheckTree 只认原版地面（草地等），树下是 mod 方块就把树打断（长出即被砍）。
    /// 检查期间把 mod 地面临时伪装成草地，orig 完必须恢复（否则世界里的地形数据会被改坏）。
    /// </summary>
    private static void CheckTreeHook(On.Terraria.WorldGen.orig_CheckTree orig, int i, int j)
    {
        var below = Main.tile[i, j + 1];
        if (!below.active() || TreeTable.ForGround(below.type) is null)
        {
            orig(i, j);
            return;
        }

        var modType = below.type;
        var cell = Main.tile[i, j + 1];
        cell.type = Terraria.ID.TileID.Grass;
        orig(i, j);

        var after = Main.tile[i, j + 1];
        if (after.active() && after.type == Terraria.ID.TileID.Grass)
        {
            var restore = Main.tile[i, j + 1];
            restore.type = modType;
        }
    }

    /// <summary>摇树：这一列是 mod 树就交给作者（ref createLeaves 能关掉原版掉落）；作者接管时记进 shake 标记。</summary>
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

        // 同一棵树只摇一次：写 treeShakeX/Y + numTreeShakes，游戏据此去重。
        WorldGen.GetTreeBottom(i, j, out var x, out var bottom);
        var count = WorldGen.numTreeShakes;
        if (count >= WorldGen.maxTreeShakes) return;
        for (var k = 0; k < count; k++)
        {
            if (WorldGen.treeShakeX[k] == x && WorldGen.treeShakeY[k] == bottom) return;
        }

        var createLeaves = true;
        bool vanilla;
        try
        {
            vanilla = tree.Shake(x, bottom - 1, ref createLeaves);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[tree] {tree.Name}.Shake 异常：{Safe.Describe(e)}");
            vanilla = true;
        }

        if (vanilla)
        {
            orig(i, j);
            return;
        }

        WorldGen.treeShakeX[count] = x;
        WorldGen.treeShakeY[count] = bottom;
        WorldGen.numTreeShakes = count + 1;
    }

    /// <summary>砍树掉木头：这一列是 mod 树就换成作者声明的木头（原版只认自己的风格号）。</summary>
    private static void KillTileDrops(On.Terraria.WorldGen.orig_KillTile_GetItemDrops orig, int x, int y, Terraria.Tile tileCache,
        out int dropItem, out int dropItemStack, out int secondaryItem, out int secondaryItemStack, out bool noPrefix, bool includeLargeObjectDrops)
    {
        orig(x, y, tileCache, out dropItem, out dropItemStack, out secondaryItem, out secondaryItemStack, out noPrefix, includeLargeObjectDrops);

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
                                     Terraria.Tile groundTile, ref int treeHeight, out int treeFrame, out int passStyle)
    {
        orig(x, topTile, groundTile, ref treeHeight, out treeFrame, out passStyle);

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
                                         int dir, out TileObject objectData, bool onlyCheck,
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

        return orig(x, y, type, style, dir, out objectData, onlyCheck, forcedRandom);
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
    private static TreeTypes TreeTypeOfTile(On.Terraria.WorldGen.orig_GetTreeType_int orig, int tileType)
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
        // mod 苗不是原版 Saplings：原版生长算法不认识它，先临时换回原版苗再长，
        // 长不成再换回来。
        var type = Main.tile[i, y].type;
        if (type == Terraria.ID.TileID.Saplings || ModContent.Find(ContentKind.Tile, type) is not ModTile)
            return OriginalGrow(orig, i, y, treeHeightAddon, ignoreWalls);

        var cells = new List<int>();
        for (var k = y; Main.tile[i, k].active() && Main.tile[i, k].type == type; k++) cells.Add(k);
        for (var k = y - 1; Main.tile[i, k].active() && Main.tile[i, k].type == type; k--) cells.Add(k);

        foreach (var k in cells)
        {
            var cell = Main.tile[i, k];
            cell.type = Terraria.ID.TileID.Saplings;
        }
        var grew = orig(i, y, treeHeightAddon, ignoreWalls);
        if (!grew)
        {
            foreach (var k in cells)
            {
                var cell = Main.tile[i, k];
                cell.type = type;
            }
            return false;
        }

        StyleColumn(i, y);
        return true;
    }

    private static bool OriginalGrow(On.Terraria.WorldGen.orig_GrowTree orig, int i, int y, int treeHeightAddon,
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
        // 发号在前（作者的 SetStaticDefaults 可能用到 Type），作者声明 GrowsOnTileId 在后，
        // 所以 Assign 要跑两次：第一次发号，第二次补建"地面方块 → 树"映射（幂等）。
        TreeTable.Assign();

        foreach (var tree in TreeTable.All)
        {
            try
            {
                tree.SetStaticDefaults();
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[tree] {tree.Name}.SetStaticDefaults 异常：{Safe.Describe(e)}");
            }
        }

        TreeTable.Assign();

        var count = TreeTable.Count;
        if (count > 0)
        {
            // 每树各占 1 槽（树顶/树枝整条图、树干单张）；表已建（Samples 阶段），Ensure/GrowAll 一次到位。
            TableMaintenance.Ensure(TreeTables.Top, TreeTables.TopVanilla + count).GrowAll(Log.Info);
            TableMaintenance.Ensure(TreeTables.Branch, TreeTables.BranchVanilla + count).GrowAll(Log.Info);
            TableMaintenance.Ensure(TreeTables.Wood, TreeTables.WoodVanilla + count).GrowAll(Log.Info);
        }

        var written = 0;
        foreach (var tree in TreeTable.All)
        {
            written += Write(tree);
        }

        if (count > 0) Log.Info($"[tree] 安装：{count} 棵树（写槽 {written}）");
    }

    private static int Write(ModTree tree)
    {
        var style = tree.Type;
        var slot = style - TreeTable.Base;              // 树顶/树枝槽 = 原版表长起每树 1 槽（= style）
        var woodSlot = TreeTables.WoodVanilla + slot;   // 树干表基线 = 原版表长

        var wood = ReadAsset(tree, () => tree.GetTexture());
        if (wood is not null) TextureSlots.TryWrite(TextureTable.TreeWood, woodSlot, wood);

        // 树顶/树枝每树 1 槽整条图；作者给了多张就只认第 1 张。
        var tops = ReadArray(tree, () => tree.GetTopTextures());
        if (tops is { Length: > 0 } && tops[0] is { } top)
        {
            TextureSlots.TryWrite(TextureTable.TreeTop, style, top);
            if (tops.Length > 1)
                ContentErrors.Report($"[tree] {tree.Name}.GetTopTextures 给了 {tops.Length} 张，只认第 1 张（每树 1 槽整条图）");
        }

        var branches = ReadArray(tree, () => tree.GetBranchTextures());
        if (branches is { Length: > 0 } && branches[0] is { } branch)
        {
            TextureSlots.TryWrite(TextureTable.TreeBranch, style, branch);
            if (branches.Length > 1)
                ContentErrors.Report($"[tree] {tree.Name}.GetBranchTextures 给了 {branches.Length} 张，只认第 1 张");
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

/// <summary>树的三种贴图表（树顶 / 树枝 / 树干）：与原版树共用 TextureAssets 里的同名数组。</summary>
internal static class TreeTables
{
    private static readonly Type[] Types = [typeof(Terraria.GameContent.TextureAssets)];

    // 表长（32/7）在 TextureAssets 里太普通，黑名单拦不全（Gem/AccHandsOn 都撞过车），
    // 三个表各自用白名单点名自己的字段。
    internal static readonly TableSet Top = new("树顶贴图", () => TopVanilla, Types, include: ["TreeTop"]);
    internal static readonly TableSet Branch = new("树枝贴图", () => BranchVanilla, Types, include: ["TreeBranch"]);
    internal static readonly TableSet Wood = new("树干贴图", () => WoodVanilla, Types, include: ["Wood"]);

    // 树贴图没有 ID 平行表，只能按表长捕获原版长度。
    private static int _top, _branch, _wood;

    internal static int TopVanilla => Capture(ref _top, TextureTable.TreeTop);
    internal static int BranchVanilla => Capture(ref _branch, TextureTable.TreeBranch);
    internal static int WoodVanilla => Capture(ref _wood, TextureTable.TreeWood);

    private static int Capture(ref int cache, TextureTable table)
    {
        if (cache > 0) return cache;
        var length = TextureSlots.Length(table);
        if (length > 0) cache = length;
        return cache;
    }
}
