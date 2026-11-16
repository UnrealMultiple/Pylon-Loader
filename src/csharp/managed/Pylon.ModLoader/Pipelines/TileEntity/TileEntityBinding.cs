using Terraria;
using Terraria.DataStructures;

namespace Pylon.ModLoader;

/// <summary>世界里的方块实体实例：ID / 位置 → 实例；ID 从 0x40000000 起，与游戏自己的 ID 空间不重叠。</summary>
internal static class TileEntityBinding
{
    private const int FirstId = 0x40000000;

    private static readonly Dictionary<int, ModTileEntity> ById = [];
    private static readonly Dictionary<Point16, ModTileEntity> ByPosition = [];
    private static readonly List<ModTileEntity> Order = [];

    private static int _next = FirstId;

    internal static int Count => ById.Count;

    internal static IReadOnlyList<ModTileEntity> All => Order;

    internal static void Clear()
    {
        ById.Clear();
        ByPosition.Clear();
        Order.Clear();
        _next = FirstId;
    }

    internal static IReadOnlyDictionary<int, ModTileEntity> ByIdView => ById;

    internal static IReadOnlyDictionary<Point16, ModTileEntity> ByPositionView => ByPosition;

    internal static ModTileEntity? At(int x, int y) =>
        ByPosition.TryGetValue(new Point16(x, y), out var entity) ? entity : null;

    internal static ModTileEntity? ByID(int id) => ById.TryGetValue(id, out var entity) ? entity : null;

    internal static ModTileEntity? ByName(string mod, string name) =>
        TileEntityTable.ByName(mod + "/" + name);

    internal static int Find(ModTileEntity template, int x, int y)
    {
        var entity = At(x, y);
        return entity is not null && entity.Type == template.Type ? entity.ID : -1;
    }

    internal static int Place(ModTileEntity template, int x, int y)
    {
        if (!WorldGen.InWorld(x, y)) return -1;
        if (At(x, y) is not null) return -1;

        var entity = template.NewInstance();
        entity.ID = _next++;
        entity.Type = template.Type;
        entity.Position = new Point16(x, y);

        ById[entity.ID] = entity;
        ByPosition[new Point16(x, y)] = entity;
        Order.Add(entity);
        return entity.ID;
    }

    internal static ModTileEntity? PlaceWithId(ModTileEntity template, int x, int y, int id)
    {
        if (!WorldGen.InWorld(x, y)) return null;

        if (At(x, y) is { } existing && existing.ID != id) Remove(existing);

        var entity = template.NewInstance();
        entity.ID = id;
        entity.Type = template.Type;
        entity.Position = new Point16(x, y);

        ById[id] = entity;
        ByPosition[new Point16(x, y)] = entity;
        Order.Add(entity);

        if (id >= _next) _next = id + 1;
        return entity;
    }

    internal static void Remove(ModTileEntity entity)
    {
        if (!ById.TryGetValue(entity.ID, out var current) || !ReferenceEquals(current, entity)) return;

        ById.Remove(entity.ID);

        var key = new Point16(entity.Position.X, entity.Position.Y);
        if (ByPosition.TryGetValue(key, out var here) && ReferenceEquals(here, entity)) ByPosition.Remove(key);

        Order.Remove(entity);
    }

    internal static bool IsLive(ModTileEntity entity) =>
        ById.TryGetValue(entity.ID, out var current) && ReferenceEquals(current, entity);


}
