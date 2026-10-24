using Pylon.ModLoader.Core;
using Pylon.ModLoader.Pipelines;
using Terraria;

namespace Pylon.ModLoader.Content;
/// <summary>
/// TileID.TML.cs 的等价扩展成员：晶塔基座 / 有效出生点两个集（tML 语义，Bunny 同形状）。
/// <see cref="CountsAsPylon"/> 是类型号列表（预填 TeleportationPylon，追加自己的类型号）；
/// <see cref="IsValidSpawnPoint"/> 按类型号索引（预填 Beds，首次访问惰性扩到当前方块总数）。
/// 消费点：Pylon 管线的附近晶塔判定（<c>TeleportPylonsSystem.IsPlayerNearAPylon</c>）与
/// 方块交互管线的出生点判定（<c>Player.CheckSpawn</c>）。
/// </summary>
public static class TileIdTml
{
    private static readonly object Gate = new();
    private static int[] _countsAsPylon = [Terraria.ID.TileID.TeleportationPylon];
    private static bool[] _isValidSpawnPoint = [];
    private static bool _hasCustomPylons;

    extension(Terraria.ID.TileID.Sets)
    {
        /// <summary>
        /// 算作晶塔基座的方块类型号（tML <c>TileID.Sets.CountsAsPylon</c>）。把自己的类型号追加上去：
        /// <c>TileID.Sets.CountsAsPylon = [.. TileID.Sets.CountsAsPylon, Type];</c>
        /// 追加后 Pylon 管线在附近晶塔判定里就认这个方块（在交互范围内即视为附近有晶塔）。
        /// </summary>
        public static int[] CountsAsPylon
        {
            get { lock (Gate) return _countsAsPylon; }
            set
            {
                lock (Gate)
                {
                    _countsAsPylon = value ?? throw new ArgumentNullException(nameof(value), "CountsAsPylon 不能为 null");
                    _hasCustomPylons = Array.Exists(_countsAsPylon, t => t != Terraria.ID.TileID.TeleportationPylon);
                }
            }
        }

        /// <summary>
        /// 这个方块是不是有效出生点（tML <c>TileID.Sets.IsValidSpawnPoint</c>；预填 Beds）。
        /// mod 方块标 true 后可以当床用：<c>TileID.Sets.IsValidSpawnPoint[Type] = true;</c>
        /// </summary>
        public static bool[] IsValidSpawnPoint
        {
            get { lock (Gate) return GrowIsValidSpawnPoint(); }
        }
    }

    /// <summary>有没有 mod 类型号进 CountsAsPylon（决定 Pylon 管线的附近晶塔判定要不要接管）。</summary>
    internal static bool HasCustomPylons => _hasCustomPylons;

    /// <summary>供管线读的 CountsAsPylon（保证已初始化）。</summary>
    internal static int[] CountsAsPylonSet
    {
        get { lock (Gate) return _countsAsPylon; }
    }

    /// <summary>这个方块类型是否被标成有效出生点（越界 / 未标返回 false）。</summary>
    internal static bool IsSpawnPoint(int type)
    {
        if (type <= 0) return false;
        lock (Gate)
        {
            var set = GrowIsValidSpawnPoint();
            return (uint)type < (uint)set.Length && set[type];
        }
    }

    /// <summary>
    /// 有作者把 mod 方块标进 CountsAsPylon 后，确保 Pylon 管线的附近晶塔判定已挂
    /// （SetStaticDefaults 在管线 Build 期才跑，晚于门控评估，用 Trigger 补装）。
    /// </summary>
    internal static void EnsurePylonHook()
    {
        if (!_hasCustomPylons) return;
        GateHub<PylonPipeline>.Trigger<PylonPipeline.Gates.Teleport>();
    }

    private static bool[] GrowIsValidSpawnPoint()
    {
        var count = TileCount();
        if (_isValidSpawnPoint.Length >= count) return _isValidSpawnPoint;

        var first = _isValidSpawnPoint.Length == 0;
        var grown = new bool[count];
        Array.Copy(_isValidSpawnPoint, grown, _isValidSpawnPoint.Length);
        if (first && (uint)Terraria.ID.TileID.Beds < (uint)count) grown[Terraria.ID.TileID.Beds] = true;
        _isValidSpawnPoint = grown;
        return grown;
    }

    private static int TileCount()
    {
        var total = VanillaCounts.Tile + ModContent.Count(ContentKind.Tile);
        var frameImportant = Main.tileFrameImportant;
        return frameImportant is null ? total : Math.Max(total, frameImportant.Length);
    }
}
