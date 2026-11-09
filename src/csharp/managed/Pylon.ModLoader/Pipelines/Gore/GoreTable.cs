using Pylon.ModLoader.Textures;

namespace Pylon.ModLoader;

/// <summary>
/// 血腥粒子侧表：类型号 = 原版表长 + 注册序；位集按类型号偏移密集索引。
/// 起点只能运行期读（GoreID 没有编译期 Count），所以 Base 由 <see cref="GoreTables.Vanilla"/> 统一捕获。
/// </summary>
internal static class GoreTable
{
    private static ModGore?[] _gores = new ModGore?[8];
    private static HookMask[] _masks = new HookMask[8];
    private static readonly Dictionary<string, ModGore> ByKey = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, ModGore?> ByName = new(StringComparer.Ordinal);
    private static int _next;

    /// <summary>原版表长（自定义类型号的起点）。</summary>
    internal static int Base => GoreTables.Vanilla;

    /// <summary>已注册的 mod gore 数量。</summary>
    internal static int Count => _next;

    /// <summary>扩表要的目标长度。</summary>
    internal static int Total => Base + _next;

    internal static ModGore? Of(int type)
    {
        var index = type - Base;
        return (uint)index < (uint)_gores.Length ? _gores[index] : null;
    }

    internal static HookMask MasksOf(int type)
    {
        var index = type - Base;
        return (uint)index < (uint)_masks.Length ? _masks[index] : HookMask.Empty;
    }

    /// <summary>类型号 → 存档 / 日志用的键。</summary>
    internal static string KeyOf(int type) => Of(type) is { } gore ? gore.Mod.Name + "/" + gore.Name : "";

    /// <summary>
    /// 按名字取编号：先按 <c>Mod名/类名</c> 精确找，再按裸类名找唯一的那一个；重名直接当没有。
    /// </summary>
    internal static int Find(string? name)
    {
        if (string.IsNullOrEmpty(name)) return 0;

        if (name.IndexOf('/') >= 0) return ByKey.TryGetValue(name, out var exact) ? exact.Type : 0;
        return ByName.TryGetValue(name, out var loose) && loose is not null ? loose.Type : 0;
    }

    /// <summary>发号并算位集；幂等，每次 Mod 注册后跑一次（原版表还没建好时先不发号）。</summary>
    internal static void Assign()
    {
        if (Base <= 0) return;

        foreach (var gore in ModContent.GetContent<ModGore>())
        {
            if (gore.Registered) continue;

            var index = _next;
            Ensure(index + 1);
            _gores[index] = gore;

            // 注册管线算的是相对 ModType 的位集，这里按 ModGore 为基类重算，继承来的空实现不算覆写。
            var mask = HookScan.Of(gore.GetType(), typeof(ModGore));
            _masks[index] = mask;
            gore.Plan = mask;
            gore.Type = Base + index;
            gore.Registered = true;

            ByKey[gore.Mod.Name + "/" + gore.Name] = gore;
            // 裸类名只保留"唯一"的：第二个同名的把这一格清成 null，之后按名字查一律当没有。
            if (ByName.ContainsKey(gore.Name)) ByName[gore.Name] = null;
            else ByName[gore.Name] = gore;

            _next = index + 1;
        }
    }

    private static void Ensure(int size)
    {
        if (size <= _gores.Length) return;

        var capacity = Math.Max(size, _gores.Length * 2);

        var grownGores = new ModGore?[capacity];
        Array.Copy(_gores, grownGores, _gores.Length);
        _gores = grownGores;

        var grownMasks = new HookMask[capacity];
        Array.Copy(_masks, grownMasks, _masks.Length);
        _masks = grownMasks;
    }
}
