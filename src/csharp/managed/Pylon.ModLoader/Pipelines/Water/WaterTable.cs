using Pylon.ModLoader.Textures;

namespace Pylon.ModLoader;

/// <summary>
/// 水样式侧表：编号 = 原版表长 + 注册序。原版总数只在运行期读（`WaterStyleID` 没有 Count），
/// 由 <see cref="WaterTables.Vanilla"/> 在扩表之前捕获一次。
/// </summary>
internal static class WaterTable
{
    private static ModWaterStyle?[] _styles = new ModWaterStyle?[8];
    private static readonly Dictionary<string, ModWaterStyle> ByKey = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, ModWaterStyle?> ByName = new(StringComparer.Ordinal);
    private static int _next;

    /// <summary>原版水样式表长（自定义编号的起点）。</summary>
    internal static int Base => WaterTables.Vanilla;

    internal static int Count => _next;

    /// <summary>扩表要的目标长度。</summary>
    internal static int Total => Base + _next;

    internal static ModWaterStyle? Of(int type)
    {
        var index = type - Base;
        return (uint)index < (uint)_styles.Length ? _styles[index] : null;
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

        foreach (var style in ModContent.GetContent<ModWaterStyle>())
        {
            if (style.Registered) continue;

            if (_next == _styles.Length) Array.Resize(ref _styles, _styles.Length * 2);

            style.Type = Base + _next;
            style.Registered = true;
            _styles[_next++] = style;

            ByKey[style.Mod.Name + "/" + style.Name] = style;
            ByName[style.Name] = ByName.ContainsKey(style.Name) ? null : style;
        }
    }
}
