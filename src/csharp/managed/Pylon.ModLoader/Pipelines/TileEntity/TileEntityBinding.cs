using Terraria;
using Terraria.DataStructures;

namespace Pylon.ModLoader;

/// <summary>
/// 世界里的方块实体实例：ID / 位置 → 实例。
/// ID 从 0x40000000 起，与游戏自己的 ID 空间不重叠（游戏的 TileEntity 是闭集，放不进 mod 类型）。
/// </summary>
internal static class TileEntityBinding
{
    private const int FirstId = 0x40000000;

    private static readonly Dictionary<int, ModTileEntity> ById = [];
    private static readonly Dictionary<Point16, ModTileEntity> ByPosition = [];
    private static readonly List<ModTileEntity> Order = [];

    private static int _next = FirstId;

    internal static int Count => ById.Count;

    /// <summary>更新顺序（字典枚举顺序在热路径上不可靠，也不允许每帧快照）。</summary>
    internal static IReadOnlyList<ModTileEntity> All => Order;

    internal static void Clear()
    {
        ById.Clear();
        ByPosition.Clear();
        Order.Clear();
        _next = FirstId;
    }

    /// <summary>ID → 实体（给作者的只读视图；内部表随放置/拆除维护）。</summary>
    internal static IReadOnlyDictionary<int, ModTileEntity> ByIdView => ById;

    /// <summary>左上角格 → 实体（给作者的只读视图）。</summary>
    internal static IReadOnlyDictionary<Point16, ModTileEntity> ByPositionView => ByPosition;

    internal static ModTileEntity? At(int x, int y) =>
        ByPosition.TryGetValue(new Point16(x, y), out var entity) ? entity : null;

    internal static ModTileEntity? ByID(int id) => ById.TryGetValue(id, out var entity) ? entity : null;

    /// <summary>按 "类名" 找本 Mod 的模板（<see cref="ModTile.TileEntity"/> 的声明写的就是同类名）。</summary>
    internal static ModTileEntity? ByName(string mod, string name) =>
        TileEntityTable.ByName(mod + "/" + name);

    /// <summary>(i, j) 上这个类型的实体 ID；没有 = -1。</summary>
    internal static int Find(ModTileEntity template, int x, int y)
    {
        var entity = At(x, y);
        return entity is not null && entity.Type == template.Type ? entity.ID : -1;
    }

    /// <summary>
    /// 放进世界：模板克隆一份，写 ID 与位置。越界、这一格已有 mod 实体时返回 -1（不覆盖别人的实体）。
    /// </summary>
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

    /// <summary>
    /// 按对端给的 ID 建实例（客户端收包用）：ID 是服务器分配的，本地计数器只保证之后新发的号不会撞。
    /// </summary>
    internal static ModTileEntity? PlaceWithId(ModTileEntity template, int x, int y, int id)
    {
        if (!WorldGen.InWorld(x, y)) return null;

        // 同一格换了实体：旧的那个已经不存在了，让位给新的（否则位置表里留下孤儿）。
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

    /// <summary>还在表里吗：作者可能在 Update 里把自己摘了。</summary>
    internal static bool IsLive(ModTileEntity entity) =>
        ById.TryGetValue(entity.ID, out var current) && ReferenceEquals(current, entity);


}
