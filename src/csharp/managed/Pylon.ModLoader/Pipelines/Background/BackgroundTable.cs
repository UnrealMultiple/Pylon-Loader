using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;

namespace Pylon.ModLoader.Pipelines;

internal static class BackgroundTable
{
    /// <summary>表层背景的起始编号（游戏 <c>SurfaceBackgroundID</c> 有 16 个）。</summary>
    internal const int Base = 17;

    private static readonly List<ModSurfaceBackgroundStyle> Styles = [];
    private static readonly Dictionary<int, ModSurfaceBackgroundStyle> BySlot = [];

    internal static int Count => Styles.Count;

    internal static IReadOnlyList<ModSurfaceBackgroundStyle> All => Styles;

    internal static int TotalCount => Base + Styles.Count;

    internal static ModSurfaceBackgroundStyle? Of(int slot) => BySlot.TryGetValue(slot, out var style) ? style : null;

    internal static ModSurfaceBackgroundStyle? Get(int slot) => Of(slot);

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

    internal static void ModifyFarFades(int style, float[] fades, float transitionSpeed)
    {
        Of(style)?.ModifyFarFades(fades, transitionSpeed);
        foreach (var global in Globals) global.ModifyFarSurfaceFades(style, fades, transitionSpeed);
    }

    private static readonly List<GlobalBackgroundStyle> Globals = [];

    internal static IReadOnlyList<GlobalBackgroundStyle> GlobalStyles => Globals;

    internal static void AddGlobal(GlobalBackgroundStyle style) => Globals.Add(style);

    /// <summary>地下背景的起始编号（游戏有 22 个地下风格）。</summary>
    internal const int UndergroundBase = 22;

    private static readonly List<ModUndergroundBackgroundStyle> Underground = [];
    private static readonly Dictionary<int, ModUndergroundBackgroundStyle> UndergroundBySlot = [];

    internal static int UndergroundCount => Underground.Count;

    internal static IReadOnlyList<ModUndergroundBackgroundStyle> UndergroundStyles => Underground;

    internal static int UndergroundTotalCount => UndergroundBase + Underground.Count;

    internal static ModUndergroundBackgroundStyle? OfUnderground(int slot) =>
        UndergroundBySlot.TryGetValue(slot, out var style) ? style : null;

    internal static void FillUndergroundTextureArray(int style, int[] textureSlots)
    {
        OfUnderground(style)?.FillTextureArray(textureSlots);
        foreach (var global in Globals) global.FillUndergroundTextureArray(style, textureSlots);
    }

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

        foreach (var global in ModContent.GetContent<GlobalBackgroundStyle>())
        {
            if (global.Registered) continue;
            global.Registered = true;
            Globals.Add(global);
        }
    }
}

/// <summary>
/// 表层风格编号相关的两张原版布尔表（<c>SurfaceBackgroundID.Sets.IsForest</c> / <c>IsDesertVariant</c>）：
/// 原版按当前 <c>Main.bgStyle</c> 读它们判「这套背景算不算森林系 / 沙漠变体」，mod 槽位落在表长之外就是越界读。
/// 扩到表层风格总数、尾部清零：这两张表的语义是「默认皆非、作者显式标 true」，
/// 作者在 <c>ModSurfaceBackgroundStyle.SetStaticDefaults</c> 里按 <see cref="ModSurfaceBackgroundStyle.Slot"/> 写 true，
/// 管线保证那一刻已扩容（Build / Refresh 都先扩再发号，发号之后才跑 SetStaticDefaults）。
/// 不走 TableGrower 的通用扩表器：它给尾部填元素 0 或众数，而 IsForest 的元素 0 是 true，
/// 会把 mod 风格默认标成森林系；这里只有两张表、游戏建表后不重建，直接用
/// <see cref="Pylon.ModLoader.Tables.ArrayGrow.GrowZeroed"/> 扩一次即到位（互操作代理的静态构造自带运行期类初始化，读属性即建表）。
/// </summary>
internal static class BackgroundTables
{
    internal static void Grow()
    {
        try
        {
            var target = BackgroundTable.Base + ModContent.GetContent<ModSurfaceBackgroundStyle>().Count;
            if (target <= BackgroundTable.Base) return;

            var forest = Terraria.ID.SurfaceBackgroundID.Sets.IsForest;
            if (forest is not null && forest.Length < target)
            {
                var grown = Tables.ArrayGrow.GrowZeroed(forest.Pointer, target);
                if (grown != IntPtr.Zero) Terraria.ID.SurfaceBackgroundID.Sets.IsForest = new Il2CppStructArray<bool>(grown);
            }

            var desert = Terraria.ID.SurfaceBackgroundID.Sets.IsDesertVariant;
            if (desert is not null && desert.Length < target)
            {
                var grown = Tables.ArrayGrow.GrowZeroed(desert.Pointer, target);
                if (grown != IntPtr.Zero) Terraria.ID.SurfaceBackgroundID.Sets.IsDesertVariant = new Il2CppStructArray<bool>(grown);
            }
        }
        catch (Exception e)
        {
            ContentErrors.Line("[background] SurfaceBackgroundID.Sets 扩容异常：" + Safe.Dump(e));
        }
    }
}