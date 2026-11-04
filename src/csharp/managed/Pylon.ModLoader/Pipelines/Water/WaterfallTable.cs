using Pylon.ModLoader.Textures;

namespace Pylon.ModLoader;

/// <summary>
/// 瀑布样式侧表：编号 = 原版表长 + 注册序。原版总数从 <c>WaterfallManager.waterfallTexture</c> 的表长捕获（无编译期常量）。
/// </summary>
internal static class WaterfallTable
{
    private static ModWaterfallStyle?[] _styles = new ModWaterfallStyle?[8];
    private static readonly Dictionary<string, ModWaterfallStyle> ByKey = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, ModWaterfallStyle?> ByName = new(StringComparer.Ordinal);
    private static int _next;

    /// <summary>原版瀑布表长（自定义编号的起点）；0 = 还没捕获到。</summary>
    internal static int Base => TypeTables.WaterfallVanilla;

    internal static int Count => _next;

    /// <summary>扩表要的目标长度。</summary>
    internal static int Total => Base + _next;

    internal static ModWaterfallStyle? Of(int type)
    {
        var index = type - Base;
        return (uint)index < (uint)_styles.Length ? _styles[index] : null;
    }

    /// <summary>发号；幂等。Base 还是 0（表没建）时先不发号，等下一轮。</summary>
    internal static void Assign()
    {
        if (Base <= 0) return;

        foreach (var style in ModContent.GetContent<ModWaterfallStyle>())
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
