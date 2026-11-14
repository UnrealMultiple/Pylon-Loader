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
    private static TileObjectData?[] _shapes = [];
    private static readonly Dictionary<(int Type, int Style), int> Styled = [];
    private static int _count;

    internal static int Count => _count;

    internal static ModTile?[] All => _tiles;

    internal static TileObjectData?[] Shapes => _shapes;

    internal readonly record struct DrawShape(
        bool Ok, int Width, int YOffset, int Padding, int Rows, int FullHeight, int[] Heights);

    private static DrawShape[] _drawShapes = [];

    internal static DrawShape[] DrawShapes => _drawShapes;

    internal static void Build(IModLog log)
    {
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

        SnapshotShapes(tileTotal);

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

    private static void SnapshotShapes(int tileTotal)
    {
        var data = TileObjectData._data;
        if (data is null) return;

        var shapes = new TileObjectData?[tileTotal];
        var drawShapes = new DrawShape[tileTotal];
        var vanilla = TileObjectData.Style1x1;
        var vanillaPointer = vanilla?.Pointer ?? IntPtr.Zero;
        var shaped = 0;

        foreach (var tile in ModContent.GetContent<ModTile>())
        {
            var type = tile.Type;
            if (type <= 0 || type >= tileTotal) continue;

            var shape = data[type];
            if (shape is null || shape.Pointer == vanillaPointer) continue;

            shapes[type] = shape;
            shaped++;

            var heights = shape.CoordinateHeights;
            var rows = shape.Height;
            if (heights is not null && heights.Length > 0 && rows > 0)
            {
                var list = new int[heights.Length];
                for (var k = 0; k < heights.Length; k++) list[k] = heights[k];
                if (list.Length < rows)
                {
                    var filled = new int[rows];
                    Array.Copy(list, filled, list.Length);
                    for (var k = list.Length; k < rows; k++) filled[k] = filled[list.Length - 1];
                    list = filled;
                }

                drawShapes[type] = new DrawShape(true, shape.CoordinateWidth, shape.DrawYOffset,
                    shape.CoordinatePadding, rows, shape.CoordinateFullHeight, list);
            }
        }

        _shapes = shapes;
        _drawShapes = drawShapes;
        if (shaped > 0) _shapedCount = shaped;
    }

    private static int _shapedCount;

    internal static int ShapedCount => _shapedCount;

    private static void Ensure(int size)
    {
        if (size > _count) _count = size;

        if (size <= _meta.Length) return;

        var grown = new TileMeta[Math.Max(size, _meta.Length * 2)];
        Array.Copy(_meta, grown, _meta.Length);
        _meta = grown;

        var grownTiles = new ModTile?[grown.Length];
        Array.Copy(_tiles, grownTiles, _tiles.Length);
        _tiles = grownTiles;
    }

    internal static bool Has(int type) => (uint)type < (uint)_meta.Length && !_meta[type].Masks.IsEmpty;

    internal static ref TileMeta Meta(int type) => ref _meta[type];

    internal static int DropOf(int type, int style)
    {
        if (!Has(type)) return 0;

        if (Styled.Count > 0 && Styled.TryGetValue((type, style), out var item)) return item;

        return _meta[type].DropItem;
    }

    internal static ModTile? Of(int type) => (uint)type < (uint)_tiles.Length ? _tiles[type] : null;

    internal static ModTile? At(int i, int j)
    {
        if (i < 0 || j < 0 || i >= Main.maxTilesX || j >= Main.maxTilesY) return null;

        var tiles = Main.tile;
        if (tiles is null) return null;

        var type = tiles[i, j].type;
        return Has(type) ? _tiles[type] : null;
    }
}
