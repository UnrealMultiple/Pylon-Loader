namespace Pylon.ModLoader;

/// <summary>每种内容在原版里的起点（自定义 ID 从这里往后接）。</summary>
internal static class ContentKindTable
{
    public static int BaseCount(ContentKind kind) => kind switch
    {
        ContentKind.Buff => VanillaCounts.Buff,
        ContentKind.Mount => MountTable.VanillaCount,
        ContentKind.Prefix => PrefixTable.VanillaCount,
        ContentKind.Rarity => RarityTable.VanillaCount,
        ContentKind.Npc => VanillaCounts.Npc,
        ContentKind.Projectile => VanillaCounts.Projectile,
        ContentKind.Item => VanillaCounts.Item,
        ContentKind.Tile => VanillaCounts.Tile,
        ContentKind.Wall => VanillaCounts.Wall,
        _ => 0,
    };

    /// <summary>这个种类是否占游戏 ID（Global / Player 不占）。</summary>
    public static bool HasTypeId(ContentKind kind) => kind is not (ContentKind.Global or ContentKind.Player);
}
