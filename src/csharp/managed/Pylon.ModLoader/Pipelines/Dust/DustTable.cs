using Pylon.ModLoader.Content;
using Pylon.ModLoader.Pipeline;

namespace Pylon.ModLoader.Pipelines;
/// <summary>尘土表：mod 尘土自己发号（不进游戏的图集，所以号段和原版无关）。</summary>
internal static class DustTable
{
    internal const int Base = 10000;

    private static readonly List<ModDust> Dusts = [];

    internal static int Count => Dusts.Count;

    internal static IReadOnlyList<ModDust> All => Dusts;

    internal static ModDust? Of(int type)
    {
        var index = type - Base;
        return (uint)index < (uint)Dusts.Count ? Dusts[index] : null;
    }

    internal static void Assign()
    {
        foreach (var dust in ModContent.GetContent<ModDust>())
        {
            if (dust.Registered) continue;

            dust.Type = Base + Dusts.Count;
            dust.Registered = true;
            dust.Hooks = HookScan.Of(dust.GetType(), typeof(ModDust));
            Dusts.Add(dust);
        }
    }
}
