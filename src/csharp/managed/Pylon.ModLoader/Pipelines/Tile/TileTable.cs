using Terraria;
using Terraria.ObjectData;

namespace Pylon.ModLoader;

/// <summary>一个方块类型的全部数据。按类型密集索引：判定路径上取 ref，不复制、不装箱。</summary>
internal struct TileMeta
{
    public HookMask Masks;
    public int DropItem;
}

/// <summary>
/// 方块侧表：每类型的位集与掉落数据（密集 struct 数组），安装期建一次。
/// 只有"指定样式的掉落"这种少数情况才进字典。
/// </summary>
internal static class TileTable
{
    private static TileMeta[] _meta = new TileMeta[1024];
    private static ModTile?[] _tiles = new ModTile?[1024];
    private static readonly Dictionary<(int Type, int Style), int> Styled = [];
    private static int _count;

    /// <summary>已登记的 mod 方块数量（诊断用）。</summary>
    internal static int Count => _count;

    /// <summary>按类型索引的方块表（循环用；长度可能大于实际内容，空位是 null）。</summary>
    internal static ModTile?[] All => _tiles;

    /// <summary>安装期建表：位集、掉落（登记表 + 物品反查）。</summary>
    internal static void Build(IModLog log)
    {
        // TileObjectData 在游戏启动时就被锁成只读；mod 方块的形状/锚点要等安装期才写，
        // 所以先把 _data 补到 mod 类型数，再临时解锁、跑完 SetStaticDefaults，最后原样锁回去。
        var tileTotal = VanillaCounts.Tile + ModContent.Count(ContentKind.Tile);
        var data = TileObjectData._data;
        if (data is not null && data.Count < tileTotal)
        {
            try
            {
                while (data.Count < tileTotal) data.Add(TileObjectData.Style1x1);
            }
            catch (Exception e)
            {
                log.Warn($"[tile] TileObjectData._data 预扩失败：{Safe.Describe(e)}");
            }
        }

        var readOnly = TileObjectData.readOnlyData;
        TileObjectData.readOnlyData = false;

        foreach (var tile in ModContent.GetContent<ModTile>())
        {
            var type = tile.Type;
            if (type <= 0) continue;

            // TileObjectData 的形状/锚点、Main.tileFrameImportant 这些都在 SetStaticDefaults 里写；
            // 不跑这一步，WorldGen.PlaceObject 找不到这个方块的形状（banner 之类 1x3 方块放不下去）。
            try
            {
                tile.SetStaticDefaults();
                tile.PostSetDefaults();
            }
            catch (Exception e)
            {
                log.Warn($"{tile.Name} 的静态默认值失败：{Safe.Describe(e)}");
            }

            Ensure(type + 1);
            _tiles[type] = tile;
            ref var meta = ref _meta[type];

            meta.Masks = HookScan.Of(tile.GetType(), typeof(ModTile));
            tile.Hooks = meta.Masks;

            if (tile.ItemDrop > 0) meta.DropItem = tile.ItemDrop;

            // 登记表：样式 -1 通配，其余按样式。
            var drops = tile.ItemDrops;
            for (var i = 0; i < drops.Count; i++)
            {
                var entry = drops[i];
                if (entry.ItemType <= 0) continue;

                if (entry.Style < 0) meta.DropItem = entry.ItemType;
                else Styled[(type, entry.Style)] = entry.ItemType;
            }
        }

        TileObjectData.readOnlyData = readOnly;

        // 物品反查：放置这块的那个物品就是默认掉落（只在作者没指定时兜底）。
        var samples = Terraria.ID.ContentSamples.ItemsByType;
        if (samples is not null)
        {
            foreach (var pair in samples)
            {
                var item = pair.Value;
                if (item is null || item.createTile <= 0) continue;

                var type = item.createTile;
                if ((uint)type >= (uint)_meta.Length || _meta[type].Masks.IsEmpty) continue;
                if (_meta[type].DropItem != 0) continue;

                _meta[type].DropItem = item.type;
            }
        }

        log.Info($"[tile] 建表：mod 方块 {_count} 个（指定样式掉落 {Styled.Count} 条）");
    }

    private static void Ensure(int size)
    {
        // _count 是"已登记的最大类型 + 1"，与是否要扩数组无关；初始 1024 容量内也必须更新诊断值。
        if (size > _count) _count = size;

        if (size <= _meta.Length) return;

        var grown = new TileMeta[Math.Max(size, _meta.Length * 2)];
        Array.Copy(_meta, grown, _meta.Length);
        _meta = grown;

        var grownTiles = new ModTile?[grown.Length];
        Array.Copy(_tiles, grownTiles, _tiles.Length);
        _tiles = grownTiles;
    }

    /// <summary>有 mod 数据吗；没有就走原版快路径。</summary>
    internal static bool Has(int type) => (uint)type < (uint)_meta.Length && !_meta[type].Masks.IsEmpty;

    internal static ref TileMeta Meta(int type) => ref _meta[type];

    /// <summary>这块掉什么：指定样式优先，然后是默认掉落；0 = 交给原版。</summary>
    internal static int DropOf(int type, int style)
    {
        if (!Has(type)) return 0;

        if (Styled.Count > 0 && Styled.TryGetValue((type, style), out var item)) return item;

        return _meta[type].DropItem;
    }

    /// <summary>按缓存类型取模板；属性覆写不一定有回调位集，因此不经过 Has 门控。</summary>
    internal static ModTile? Of(int type) => (uint)type < (uint)_tiles.Length ? _tiles[type] : null;

    /// <summary>按格子取方块；不是 mod 方块返回 null。</summary>
    internal static ModTile? At(int i, int j)
    {
        if (i < 0 || j < 0 || i >= Main.maxTilesX || j >= Main.maxTilesY) return null;

        var tiles = Main.tile;
        if (tiles is null) return null;

        var type = tiles[i, j].type;
        return Has(type) ? _tiles[type] : null;
    }
}
