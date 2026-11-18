using Pylon.ModLoader.Pipelines;

namespace Pylon.ModLoader.Content;
/// <summary>稀有度静态查询：原版数量、总数、按号反查。</summary>
public static class RarityLoader
{
    public const int VanillaCount = RarityTable.VanillaCount;

    public static int RarityCount => RarityTable.Count;

    public static ModRarity? GetRarity(int type) => RarityTable.Of(type);
}
