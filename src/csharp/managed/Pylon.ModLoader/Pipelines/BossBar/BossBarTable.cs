namespace Pylon.ModLoader;

/// <summary>
/// 血条侧表：只用来"知道有哪些血条、都叫啥"和在安装期统一调一次 <c>SetStaticDefaults</c>。
/// 血条不需要编号——NPC 直接声明实例，分派时按 NPC 类型现查。
/// </summary>
internal static class BossBarTable
{
    private static readonly List<ModBossBar> Bars = [];
    private static readonly Dictionary<string, ModBossBar> ByKey = new(StringComparer.Ordinal);

    internal static int Count => Bars.Count;

    internal static ModBossBar At(int index) => Bars[index];

    /// <summary>按 <c>Mod名/类名</c> 取血条；找不到 = null。</summary>
    internal static ModBossBar? Find(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        return ByKey.TryGetValue(name, out var bar) ? bar : null;
    }

    /// <summary>重建表；幂等，每次 Mod 注册后跑一次。</summary>
    internal static void Rebuild()
    {
        Bars.Clear();
        ByKey.Clear();

        foreach (var bar in ModContent.GetContent<ModBossBar>())
        {
            Bars.Add(bar);
            ByKey[bar.Mod.Name + "/" + bar.Name] = bar;
        }
    }
}
