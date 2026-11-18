using Pylon.ModLoader.Pipelines;

namespace Pylon.ModLoader.Content;
/// <summary>前缀静态查询：原版数量、总数、按号反查、按分类列表。</summary>
public static class PrefixLoader
{
    public static int VanillaCount => PrefixTable.VanillaCount;

    public static int PrefixCount => PrefixTable.Count;

    public static ModPrefix? GetPrefix(int type) => PrefixTable.Of(type);

    public static IReadOnlyList<ModPrefix> GetPrefixesInCategory(PrefixCategory category) =>
        PrefixTable.InCategory(category).ToArray();
}
