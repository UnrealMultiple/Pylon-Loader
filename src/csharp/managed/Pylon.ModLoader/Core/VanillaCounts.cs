namespace Pylon.ModLoader;

/// <summary>原版数量 = 自定义 ID 起点（游戏里是编译期常量，只能写死；Terraria 1.4.5.8.6）。</summary>
internal static class VanillaCounts
{
    public const int Buff = 401;
    public const int Npc = 697;
    public const int Projectile = 1136;
    public const int Item = 6196;
    public const int Tile = 754;
    public const int Wall = 367;

    // 坐骑没有编译期常量：MountID.Count 在运行时读（MountLoader.BaseCount 捕获首次值）。
}
