using System;

namespace Pylon.ModLoader.Tables;

/// <summary>一条扩表请求：哪张表、目标多长、要不要在内容装配前扩好。</summary>
internal readonly struct TableRequest(TableSet set, Func<int> target, bool early = false)
{
    internal TableSet Set { get; } = set;

    /// <summary>目标表长，每次重新算。</summary>
    internal Func<int> Target { get; } = target;

    /// <summary>true = 内容装配（发号 / 写槽）之前必须扩好。</summary>
    internal bool Early { get; } = early;
}
