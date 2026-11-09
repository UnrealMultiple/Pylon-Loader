namespace Pylon.ModLoader.Textures;

/// <summary>
/// 一类"编号跟着内容走"的原生表：原版长度 + 需要一起变长的静态数组所在类清单。
/// 由各个管线自己声明（谁用到谁扩表），核心只负责按这份描述去扩。
/// </summary>
internal sealed class TableSet
{
    private readonly Func<int> _vanilla;
    private readonly Func<TableClass[]> _classes;

    internal TableSet(string name, Func<int> vanilla, Func<TableClass[]> classes, bool fillWithMode = false)
    {
        Name = name;
        _vanilla = vanilla;
        _classes = classes;
        FillWithMode = fillWithMode;
    }

    /// <summary>日志里用的中文名。</summary>
    internal string Name { get; }

    /// <summary>原版表长（要在扩表之前读，读到 0 表示表还没建）。</summary>
    internal int VanillaCount => _vanilla();

    /// <summary>要扫描"静态 T[] 字段"的类清单。</summary>
    internal TableClass[] Classes => _classes();

    /// <summary>尾部填"原数组里出现最多的值"（方块/墙壁：填 0 会变成实心泥土）。</summary>
    internal bool FillWithMode { get; }
}
