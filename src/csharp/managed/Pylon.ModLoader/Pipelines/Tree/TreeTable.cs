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

    /// <summary>发号；已在册的树只补建地面映射（作者在 SetStaticDefaults 里才写 GrowsOnTileId）。幂等。</summary>
    internal static void Assign()
    {
        foreach (var tree in ModContent.GetContent<ModTree>())
        {
            if (!tree.Registered)
            {
                tree.Type = Base + Trees.Count;
                tree.Registered = true;
                Trees.Add(tree);
                ByStyle[tree.Type] = tree;
            }

            // 地面映射每次都重建：GrowsOnTileId 在发号之后的 SetStaticDefaults 里才就位。
            var grounds = tree.GrowsOnTileId;
            if (grounds is null) continue;

            for (var index = 0; index < grounds.Length; index++)
            {
                var ground = grounds[index];
                if (ground <= 0) continue;
                if (ByGround.TryGetValue(ground, out var other) && other != tree) continue;

                ByGround[ground] = tree;
            }
        }
    }
}
