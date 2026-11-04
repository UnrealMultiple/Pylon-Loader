using Terraria.ID;

namespace Pylon.ModLoader;

/// <summary>树表：mod 树的风格号从游戏的 32 起；地面方块 → 树 的映射按作者写的 GrowsOnTileId。</summary>
internal static class TreeTable
{
    /// <summary>游戏的树风格数（<c>Main.numTreeStyles</c>）。</summary>
    internal const int Base = 32;

    private static readonly List<ModTree> Trees = [];
    private static readonly Dictionary<int, ModTree> ByStyle = [];
    private static readonly Dictionary<int, ModTree> ByGround = [];

    internal static int Count => Trees.Count;

    internal static IReadOnlyList<ModTree> All => Trees;

    internal static ModTree? Of(int style) => ByStyle.TryGetValue(style, out var tree) ? tree : null;

    /// <summary>这块地上长哪种树（作者没写 GrowsOnTileId 就是 null）。</summary>
    internal static ModTree? ForGround(int tileType) => ByGround.TryGetValue(tileType, out var tree) ? tree : null;

    /// <summary>发号 + 建地面映射；幂等。</summary>
    internal static void Assign()
    {
        foreach (var tree in ModContent.GetContent<ModTree>())
        {
            if (tree.Registered) continue;

            tree.Type = Base + Trees.Count;
            tree.Registered = true;
            Trees.Add(tree);
            ByStyle[tree.Type] = tree;

            var grounds = tree.GrowsOnTileId;
            if (grounds is null) continue;

            for (var index = 0; index < grounds.Length; index++)
            {
                var ground = grounds[index];
                if (ground <= 0) continue;
                if (ByGround.ContainsKey(ground)) continue;

                ByGround[ground] = tree;
            }
        }
    }
}
