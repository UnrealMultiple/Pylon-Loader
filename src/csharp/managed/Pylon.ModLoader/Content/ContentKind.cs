namespace Pylon.ModLoader;

/// <summary>内容种类。枚举顺序决定同一 Mod 内的发号次序，跨设备必须一致。</summary>
public enum ContentKind
{
    Buff = 0,
    Player,
    Mount,
    Prefix,
    Rarity,
    Npc,
    Projectile,
    Item,
    Tile,
    Wall,
    Global,
}
