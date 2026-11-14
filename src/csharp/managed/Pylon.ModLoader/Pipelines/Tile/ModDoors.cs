using System.Collections.Generic;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;

namespace Pylon.ModLoader;

/// <summary>
/// mod 门伪装：游戏只会开"关门/开门"(10/11)，进开关门逻辑（玩家贴身 <c>DoorOpeningHelper</c>、
/// 会开关门的居民 NPC AI）前把邻域里的 mod 门临时 SetType 成 10/11，返回后按"游戏动了哪格"还原；
/// 破门（KillTile 命中伪装格）先还原成 mod 门再重走完整钩链，让尘土/掉落/作者钩子见到真类型。
/// 门对声明见 <see cref="ModTile.DoorOpenType"/>。
/// </summary>
/// <remarks>
/// 每帧热路径：邻域格进静态密集暂存（栈式切片），作用域用 struct 令牌，读格走裸下标，全程零托管分配；
/// 写入必须走 <see cref="TileData.SetType"/>——tile 内容共享"定义"，直写类型会改到全世界相同的方块。
/// </remarks>
internal static class ModDoors
{
    private const int ActiveBit = 0x20; // TileData.TileSHeader 的 active 位

    private static int[] _partner = [];   // type → 门对的另一态；0 = 不是门
    private static bool[] _isOpen = [];   // 该类型是开门态（11）还是关门态（10）

    // 在途伪装格的密集暂存：嵌套伪装把各自的切片叠在同一个数组里，退栈时把 _cellsTop 收回切片起点。
    private static Cell[] _cells = new Cell[64];
    private static int _cellsTop;

    // 伪装栈：破门 hook 只对栈顶（正在运行的开关门逻辑）生效。
    private static (int Start, int Count, int Width)[] _scopes = new (int, int, int)[8];
    private static int _scopeDepth;

    private static bool _installed;
    private static bool _inKill;

    private static IModLog Log => LoaderHost.Current.Log;

    /// <summary>TileTable.Build 之后调用：收门对声明；有门对才挂四个钩子（没门对零开销）。</summary>
    internal static void Build(IModLog log)
    {
        var declared = 0;
        foreach (var tile in TileTable.All)
        {
            if (tile is null) continue;

            if (tile.DoorOpenType > 0) declared += Register(tile.Type, tile.DoorOpenType, log);
            if (tile.DoorClosedType > 0) declared += Register(tile.DoorClosedType, tile.Type, log);
        }

        if (declared <= 0) return;

        Install();
        log.Info($"[door] 门伪装已挂（门对声明 {declared} 条）");
    }

    /// <summary>登记一对门。mod 方块之外（作者误写原版 ID）或未注册的忽略。</summary>
    private static int Register(int closedType, int openType, IModLog log)
    {
        var total = VanillaCounts.Tile + ModContent.Count(ContentKind.Tile);
        if (closedType < VanillaCounts.Tile || openType < VanillaCounts.Tile ||
            closedType >= total || openType >= total || closedType == openType ||
            TileTable.Of(closedType) is null || TileTable.Of(openType) is null)
        {
            log.Warn($"[door] 门对 {closedType}↔{openType} 无效（两侧都得是已注册的 mod 方块），忽略");
            return 0;
        }

        Ensure(ref _partner, total);
        Ensure(ref _isOpen, total);
        if ((_partner[closedType] != 0 && _partner[closedType] != openType) ||
            (_partner[openType] != 0 && _partner[openType] != closedType))
        {
            log.Warn($"[door] 方块 {closedType}↔{openType} 与已有门对（{_partner[closedType]}↔{closedType}）冲突，忽略新声明");
            return 0;
        }

        _isOpen[closedType] = false;
        _isOpen[openType] = true;
        _partner[closedType] = openType;
        _partner[openType] = closedType;
        return 1;
    }

    private static void Ensure<T>(ref T[] array, int size)
    {
        if (array.Length >= size) return;
        Array.Resize(ref array, size);
    }

    /// <summary>不是门返回 0；越界（负数/超长）也返回 0。</summary>
    private static int PartnerOf(int type) =>
        (uint)type < (uint)_partner.Length ? _partner[type] : 0;

    /// <summary>mod 门假装成的原版门。</summary>
    private static int VanillaOf(int modDoor) => _isOpen[modDoor] ? TileID.OpenDoor : TileID.ClosedDoor;

    private static void Install()
    {
        if (_installed) return;
        _installed = true;

        On.Terraria.WorldGen.KillTile += Kill;
        On.Terraria.GameContent.DoorOpeningHelper.Update += HelperUpdate;
        On.Terraria.NPC.AI_007_TownEntities += TownAi;
        On.Terraria.NPC.AI_003_Fighters += FighterAi;
        On.Terraria.NPC.AI_107_ImprovedWalkers += WalkerAi;
    }

    /// <summary>Entity 中心的 tile 坐标。</summary>
    private static (int X, int Y) CenterOf(Entity entity) =>
        ((int)((entity.position.X + entity.width / 2f) / 16f),
         (int)((entity.position.Y + entity.height / 2f) / 16f));

    // 玩家贴身开关门：门在玩家中心 ±6×4 格内（离开门前一格游戏就要关门，所以横向半径更大）。
    private static void HelperUpdate(On.Terraria.GameContent.DoorOpeningHelper.orig_Update orig,
                                     DoorOpeningHelper self, Player player)
    {
        if (player is null)
        {
            orig(self, player);
            return;
        }

        var (cx, cy) = CenterOf(player);
        using var scope = Enter(cx, cy, 6, 4);
        orig(self, player);
    }

    // 会开/关门的 NPC AI：居民在距门 2-4 格时就先去关门，半径取 5。
    private static void TownAi(On.Terraria.NPC.orig_AI_007_TownEntities orig, NPC self)
    {
        using var scope = Around(self, 5, 5);
        orig(self);
    }

    private static void FighterAi(On.Terraria.NPC.orig_AI_003_Fighters orig, NPC self)
    {
        using var scope = Around(self, 5, 5);
        orig(self);
    }

    private static void WalkerAi(On.Terraria.NPC.orig_AI_107_ImprovedWalkers orig, NPC self)
    {
        using var scope = Around(self, 5, 5);
        orig(self);
    }

    private static Scope Around(NPC npc, int rx, int ry)
    {
        var (cx, cy) = CenterOf(npc);
        return Enter(cx, cy, rx, ry);
    }

    /// <summary>
    /// 破门（僵尸 / 哥布林等对门格的 KillTile）：命中栈顶伪装的本体格时先还原成 mod 门再重走完整钩链
    /// （本管线最先跑，TilePipeline 的作者分派在 orig 链里，见到的是真类型），回来再伪装上。
    /// </summary>
    private static void Kill(On.Terraria.WorldGen.orig_KillTile orig, int i, int j, bool fail, bool effectOnly,
                             bool noItem)
    {
        if (_scopeDepth <= 0 || _inKill || !Covers(i, j))
        {
            orig(i, j, fail, effectOnly, noItem);
            return;
        }

        _inKill = true;
        try
        {
            UndressTop();
            WorldGen.KillTile(i, j, fail, effectOnly, noItem);
        }
        finally
        {
            _inKill = false;
            DressTop();
        }
    }

    /// <summary>栈顶伪装是否覆盖 (x, y) 的门本体格。</summary>
    private static bool Covers(int x, int y)
    {
        var top = _scopes[_scopeDepth - 1];
        var end = top.Start + top.Count;
        for (var i = top.Start; i < end; i++)
        {
            var c = _cells[i];
            if (c.Own && c.X == x && c.Y == y) return true;
        }

        return false;
    }

    /// <summary>栈顶伪装里的门本体格还原成 mod 门（破门重入用，让 KillTile 见到真类型）。</summary>
    private static void UndressTop()
    {
        var top = _scopes[_scopeDepth - 1];
        var end = top.Start + top.Count;
        for (var i = top.Start; i < end; i++)
        {
            var c = _cells[i];
            if (!c.Own) continue;

            var asVanilla = VanillaOf(c.Owner);
            if (ReadType(top.Width, c.X, c.Y) == asVanilla)
                TileData.SetType(c.Y * top.Width + c.X, (ushort)c.Owner);
        }
    }

    /// <summary>栈顶伪装里的门本体格（仍是 mod 门模样的）重新伪装成原版门。</summary>
    private static void DressTop()
    {
        var top = _scopes[_scopeDepth - 1];
        var end = top.Start + top.Count;
        for (var i = top.Start; i < end; i++)
        {
            var c = _cells[i];
            if (!c.Own) continue;

            if (ReadType(top.Width, c.X, c.Y) == c.Owner)
                TileData.SetType(c.Y * top.Width + c.X, (ushort)VanillaOf(c.Owner));
        }
    }

    private readonly record struct Cell(int X, int Y, int Prev, int Owner, bool Own);

    /// <summary>一次开关门逻辑期间的伪装范围；struct 令牌，Dispose 时按记录还原并退栈。</summary>
    private readonly struct Scope(int start, int count, int width) : IDisposable
    {
        public void Dispose() => Close(start, count, width);
    }

    /// <summary>扫邻域找 mod 门：记录 + 伪装。返回的作用域令牌离开 using 时还原（空邻域也照常压栈，保证配对退栈）。</summary>
    private static Scope Enter(int cx, int cy, int rx, int ry)
    {
        var start = _cellsTop;
        var width = 0;
        var tile = Main.tile;

        if (tile is not null)
        {
            width = tile._width;
            var x0 = Math.Max(cx - rx, 1);
            var x1 = Math.Min(cx + rx, width - 2);
            var y0 = Math.Max(cy - ry, 0);
            var y1 = Math.Min(cy + ry, tile._height - 1);

            if (x1 >= x0 && y1 >= y0)
            {
                for (var y = y0; y <= y1; y++)
                {
                    for (var x = x0; x <= x1; x++)
                    {
                        var type = ReadType(width, x, y);
                        if (type < 0 || PartnerOf(type) == 0) continue;

                        // 门本体加同行左右邻格：开门态会挪进其中一侧。
                        for (var dx = -1; dx <= 1; dx++) Push(new Cell(x + dx, y, ReadType(width, x + dx, y), type, dx == 0));
                    }
                }
            }
        }

        var count = _cellsTop - start;
        if (count > 0) Dress(start, count, width);

        if (_scopeDepth >= _scopes.Length) Array.Resize(ref _scopes, _scopeDepth * 2);
        _scopes[_scopeDepth++] = (start, count, width);
        return new Scope(start, count, width);
    }

    private static void Push(Cell cell)
    {
        if (_cellsTop >= _cells.Length) Array.Resize(ref _cells, _cellsTop * 2);
        _cells[_cellsTop++] = cell;
    }

    /// <summary>还原一个切片：没动的格子回 mod 门，被开/关的格子回门对，开进邻格的把邻格写成门对另一态。</summary>
    private static void Close(int start, int count, int width)
    {
        if (_scopeDepth > 0) _scopeDepth--;

        var end = start + count;
        for (var i = start; i < end; i++)
        {
            var c = _cells[i];
            var now = ReadType(width, c.X, c.Y);
            var other = PartnerOf(c.Owner);
            var asOwner = VanillaOf(c.Owner);
            var asOther = VanillaOf(other);
            int write;
            if (c.Own)
            {
                // 游戏没动这格 → 回 mod 门；游戏开关过（现在是伪装的另一态）→ 回门对。
                if (now == asOwner) write = c.Owner;
                else if (now == asOther) write = other;
                else continue;
            }
            else if (now == asOther && now != c.Prev && c.Prev != TileID.ClosedDoor && c.Prev != TileID.OpenDoor &&
                     PartnerOf(c.Prev) == 0)
            {
                // 邻格原先不是门，被开门挪进来的伪装门占了 → 写成门对的另一态。
                write = other;
            }
            else continue;

            TileData.SetType(c.Y * width + c.X, (ushort)write);
        }

        _cellsTop = start;
    }

    /// <summary>把切片里的门本体格（保持 mod 门模样的）SetType 成原版门。</summary>
    private static void Dress(int start, int count, int width)
    {
        var end = start + count;
        for (var i = start; i < end; i++)
        {
            var c = _cells[i];
            if (!c.Own) continue;

            var asVanilla = VanillaOf(c.Owner);
            if (ReadType(width, c.X, c.Y) == c.Owner) TileData.SetType(c.Y * width + c.X, (ushort)asVanilla);
        }
    }

    /// <summary>裸下标读一格：active 位（sHeader 0x20）+ 类型。</summary>
    private static int ReadType(int width, int x, int y)
    {
        var index = y * width + x;
        if ((TileData.GetSHeader(index) & ActiveBit) == 0) return -1;
        return TileData.GetType(index);
    }
}
