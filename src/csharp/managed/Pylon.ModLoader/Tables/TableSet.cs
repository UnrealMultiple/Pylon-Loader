using System;

namespace Pylon.ModLoader.Tables;

/// <summary>一类跟着内容编号变长的原生表：原版长度 + 要一起变长的类。</summary>
internal sealed class TableSet
{
    private readonly Func<int> _vanilla;

    internal TableSet(string name, Func<int> vanilla, Type[] types, bool fillWithMode = false, string[]? skip = null,
        string[]? include = null)
    {
        Name = name;
        _vanilla = vanilla;
        Types = types;
        FillWithMode = fillWithMode;
        Skip = skip;
        Include = include;
    }

    /// <summary>日志里用的中文名。</summary>
    internal string Name { get; }

    /// <summary>原版表长（要在扩表之前读；0 = 表还没建）。</summary>
    internal int VanillaCount => _vanilla();

    /// <summary>要扫静态数组字段的类。</summary>
    internal Type[] Types { get; }

    /// <summary>尾部填原数组的众数（方块/墙壁：填 0 会变成实心泥土）。</summary>
    internal bool FillWithMode { get; }

    /// <summary>这些类里不跟着变长的字段名。</summary>
    internal string[]? Skip { get; }

    /// <summary>白名单：非空时只认这些字段名（表长太普通、黑名单拦不全时用）。</summary>
    internal string[]? Include { get; }
}
