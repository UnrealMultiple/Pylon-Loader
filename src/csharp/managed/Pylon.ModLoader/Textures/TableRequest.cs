namespace Pylon.ModLoader.Textures;

/// <summary>
/// 一条扩表请求：哪张表、目标多长、要不要在内容装配之前就扩好。
/// 由各管线在自己的模块 <c>Tables</c> 里声明，框架统一执行。
/// </summary>
internal readonly struct TableRequest(TableSet set, Func<int> target, bool early = false)
{
    internal TableSet Set { get; } = set;

    /// <summary>目标表长（每次都重新算：数量可能随安装进度变化）。</summary>
    internal Func<int> Target { get; } = target;

    /// <summary>内容装配（发号 / 写槽）之前必须扩好：物品、弹幕、NPC、Buff、方块、墙壁。</summary>
    internal bool Early { get; } = early;
}
