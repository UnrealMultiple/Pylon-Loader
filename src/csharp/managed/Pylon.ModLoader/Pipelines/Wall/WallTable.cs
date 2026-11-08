using Terraria;

namespace Pylon.ModLoader;

/// <summary>一面墙的数据。按类型密集索引：判定路径取 ref，不复制、不装箱。</summary>
internal struct WallMeta
{
    public HookMask Masks;
    public int DropItem;
}

/// <summary>墙壁侧表：每类型的位集与默认掉落（密集 struct 数组），安装期建一次。</summary>
internal static class WallTable
{
    private static WallMeta[] _meta = new WallMeta[256];
    private static ModWall?[] _walls = new ModWall?[256];
    private static int _count;

    internal static int Count => _count;

    internal static void Build(IModLog log)
    {
        foreach (var wall in ModContent.GetContent<ModWall>())
        {
            var type = wall.Type;
            if (type <= 0) continue;

            // 墙的静态默认值（Main.wallHouse、地图项、以及作者自己写的平行表）和方块侧一样
            // 在安装期跑一次；不跑这一步，墙不会被算作房屋墙，地图项也为空。
            try
            {
                wall.SetStaticDefaults();
            }
            catch (Exception e)
            {
                log.Warn($"{wall.Name} 的静态默认值失败：{Safe.Describe(e)}");
            }

            Ensure(type + 1);
            _walls[type] = wall;

            ref var meta = ref _meta[type];
            meta.Masks = HookScan.Of(wall.GetType(), typeof(ModWall));
            wall.Hooks = meta.Masks;
            if (wall.ItemDrop > 0) meta.DropItem = wall.ItemDrop;
        }

        // 反查：放置这面墙的物品就是默认掉落，先到先得（保证确定性）。
        var samples = Terraria.ID.ContentSamples.ItemsByType;
        if (samples is not null)
        {
            foreach (var pair in samples)
            {
                var item = pair.Value;
                if (item is null || item.createWall <= 0) continue;

                var type = item.createWall;
                if (!Has(type)) continue;
                if (_meta[type].DropItem != 0) continue;

                _meta[type].DropItem = item.type;
            }
        }

        log.Info($"[wall] 建表：mod 墙 {_count} 面");
    }

    private static void Ensure(int size)
    {
        if (size <= _meta.Length) return;

        var grown = new WallMeta[Math.Max(size, _meta.Length * 2)];
        Array.Copy(_meta, grown, _meta.Length);
        _meta = grown;

        var grownWalls = new ModWall?[grown.Length];
        Array.Copy(_walls, grownWalls, _walls.Length);
        _walls = grownWalls;

        if (size > _count) _count = size;
    }

    /// <summary>
    /// 是不是 mod 墙。判据是"这个类型登记过"而不是"覆写了钩子"——
    /// 只写静态数据（ItemDrop / 地图项）的墙一个钩子都不覆写，用位集判会把它当成原版墙。
    /// </summary>
    internal static bool Has(int type) => (uint)type < (uint)_walls.Length && _walls[type] is not null;

    internal static ref WallMeta Meta(int type) => ref _meta[type];

    internal static int DropOf(int type) => Has(type) ? _meta[type].DropItem : 0;

    /// <summary>按类型取墙；不是 mod 墙返回 null。</summary>
    internal static ModWall? Of(int type) => Has(type) ? _walls[type] : null;

    /// <summary>按格子取墙；不是 mod 墙返回 null。</summary>
    internal static ModWall? At(int i, int j)
    {
        if (i < 0 || j < 0 || i >= Main.maxTilesX || j >= Main.maxTilesY) return null;

        var type = Main.tile[i, j].wall;
        return Has(type) ? _walls[type] : null;
    }
}
