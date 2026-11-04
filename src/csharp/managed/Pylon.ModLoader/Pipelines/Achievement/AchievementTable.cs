namespace Pylon.ModLoader;

/// <summary>成就表：按注册序；键是 <c>&lt;mod&gt;/&lt;类名&gt;</c>（进游戏成就文件的就是这个键）。</summary>
internal static class AchievementTable
{
    private static readonly List<ModAchievement> All = [];
    private static readonly Dictionary<string, ModAchievement> ByKey = new(StringComparer.Ordinal);

    internal static int Count => All.Count;

    internal static IReadOnlyList<ModAchievement> Entries => All;

    internal static string KeyOf(ModAchievement achievement) => achievement.Mod.Name + "/" + achievement.Name;

    internal static ModAchievement? Of(string? key) =>
        key is not null && ByKey.TryGetValue(key, out var achievement) ? achievement : null;

    /// <summary>收表；幂等。</summary>
    internal static void Rebuild()
    {
        All.Clear();
        ByKey.Clear();

        foreach (var achievement in ModContent.GetContent<ModAchievement>())
        {
            var key = KeyOf(achievement);
            All.Add(achievement);
            ByKey[key] = achievement;
        }
    }
}
