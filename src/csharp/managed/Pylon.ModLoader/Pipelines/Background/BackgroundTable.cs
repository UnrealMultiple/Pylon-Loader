namespace Pylon.ModLoader;

/// <summary>背景风格表：表层风格编号按契约从 17 起，按注册序。</summary>
internal static class BackgroundTable
{
    /// <summary>表层背景的起始编号（游戏 <c>SurfaceBackgroundID</c> 有 16 个）。</summary>
    internal const int Base = 17;

    private static readonly List<ModSurfaceBackgroundStyle> Styles = [];
    private static readonly Dictionary<int, ModSurfaceBackgroundStyle> BySlot = [];

    internal static int Count => Styles.Count;

    internal static IReadOnlyList<ModSurfaceBackgroundStyle> All => Styles;

    internal static ModSurfaceBackgroundStyle? Of(int slot) => BySlot.TryGetValue(slot, out var style) ? style : null;

    internal static ModSurfaceBackgroundStyle? Find(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;

        for (var index = 0; index < Styles.Count; index++)
        {
            var style = Styles[index];
            if (string.Equals(style.Name, name, StringComparison.Ordinal)) return style;
        }

        return null;
    }

    /// <summary>地下背景的起始编号（游戏有 22 个地下风格）。</summary>
    internal const int UndergroundBase = 22;

    private static readonly List<ModUndergroundBackgroundStyle> Underground = [];
    private static readonly Dictionary<int, ModUndergroundBackgroundStyle> UndergroundBySlot = [];

    internal static int UndergroundCount => Underground.Count;

    internal static IReadOnlyList<ModUndergroundBackgroundStyle> UndergroundStyles => Underground;

    internal static ModUndergroundBackgroundStyle? OfUnderground(int slot) =>
        UndergroundBySlot.TryGetValue(slot, out var style) ? style : null;

    /// <summary>给地下风格发号（22 之后）；幂等。</summary>
    internal static void AssignUnderground()
    {
        foreach (var style in ModContent.GetContent<ModUndergroundBackgroundStyle>())
        {
            if (style.Slot >= 0) continue;

            var slot = UndergroundBase + Underground.Count;
            style.Slot = slot;
            style.Type = slot;

            Underground.Add(style);
            UndergroundBySlot[slot] = style;
        }
    }

    /// <summary>发号；幂等。风格号与 <c>Type</c> 用同一个值（背景不占内容表）。</summary>
    internal static void Assign()
    {
        foreach (var style in ModContent.GetContent<ModSurfaceBackgroundStyle>())
        {
            if (style.Registered) continue;

            var slot = Base + Styles.Count;
            style.Slot = slot;
            style.Type = slot;
            style.Registered = true;

            Styles.Add(style);
            BySlot[slot] = style;
        }
    }
}
