using Pylon.ModLoader.Textures;

namespace Pylon.ModLoader;

/// <summary>
/// 发型侧表：编号 = 原版表长 + 注册序。原版表长只能运行期读（没有编译期常量），
/// 由 <see cref="TypeTables.HairVanilla"/> 在扩表之前捕获一次。
/// </summary>
internal static class HairTable
{
    private static ModHair?[] _hairs = new ModHair?[8];
    private static readonly Dictionary<string, ModHair> ByKey = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, ModHair?> ByName = new(StringComparer.Ordinal);
    private static int _next;

    /// <summary>原版发型表长（自定义编号的起点）。</summary>
    internal static int Base => TypeTables.HairVanilla;

    internal static int Count => _next;

    /// <summary>扩表要的目标长度。</summary>
    internal static int Total => Base + _next;

    internal static ModHair? Of(int type)
    {
        var index = type - Base;
        return (uint)index < (uint)_hairs.Length ? _hairs[index] : null;
    }

    /// <summary>按名字取编号：先 <c>Mod名/类名</c>，再裸类名（重名当没有）。</summary>
    internal static int Find(string? name)
    {
        if (string.IsNullOrEmpty(name)) return 0;

        if (name.IndexOf('/') >= 0) return ByKey.TryGetValue(name, out var exact) ? exact.Type : 0;
        return ByName.TryGetValue(name, out var loose) && loose is not null ? loose.Type : 0;
    }

    /// <summary>发号；幂等，每次 Mod 注册后跑一次（原版表还没建好时先不发号）。</summary>
    internal static void Assign()
    {
        if (Base <= 0) return;

        foreach (var hair in ModContent.GetContent<ModHair>())
        {
            if (hair.Registered) continue;

            if (_next == _hairs.Length) Array.Resize(ref _hairs, _hairs.Length * 2);

            hair.Type = Base + _next;
            hair.Registered = true;
            _hairs[_next++] = hair;

            ByKey[hair.Mod.Name + "/" + hair.Name] = hair;
            ByName[hair.Name] = ByName.ContainsKey(hair.Name) ? null : hair;
        }
    }
}
