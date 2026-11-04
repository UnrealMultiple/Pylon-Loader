using Terraria.GameContent;

namespace Pylon.ModLoader;

/// <summary>
/// 晶塔类型表：类型号从 <see cref="TeleportPylonType.Count"/>（11）起，按注册序排列。
/// `TeleportPylonType` 是 byte 底层枚举，所以 mod 晶塔最多到 255。
/// </summary>
internal static class PylonTable
{
    internal const int Base = (int)TeleportPylonType.Count;

    private const int Limit = 255;

    private static readonly List<ModPylon> Pylons = [];
    private static readonly Dictionary<int, ModPylon> ByType = [];
    private static readonly Dictionary<string, ModPylon> ByKey = new(StringComparer.Ordinal);

    internal static int Count => Pylons.Count;

    internal static ModPylon? Of(int type) => ByType.TryGetValue(type, out var pylon) ? pylon : null;

    internal static ModPylon? Find(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        return ByKey.TryGetValue(name, out var pylon) ? pylon : null;
    }

    /// <summary>发号；幂等，每次 Mod 注册后跑一次。冲突与越界直接抛。</summary>
    internal static void Assign()
    {
        foreach (var pylon in ModContent.GetContent<ModPylon>())
        {
            if (pylon.Registered) continue;

            var type = Base + Pylons.Count;

            if (type > Limit)
                throw new InvalidOperationException(
                    $"{pylon.Mod.Name}/{pylon.Name}: 晶塔类型号 {type} 超过 255（TeleportPylonType 是 byte 枚举）");

            if (ByType.TryGetValue(type, out var other))
                throw new InvalidOperationException(
                    $"{pylon.Mod.Name}/{pylon.Name}: 晶塔类型号 {type} 已经被 {other.Mod.Name}/{other.Name} 占了");

            pylon.PylonType = type;
            pylon.Registered = true;

            Pylons.Add(pylon);
            ByType[type] = pylon;
            ByKey[pylon.Mod.Name + "/" + pylon.Name] = pylon;
        }
    }
}
