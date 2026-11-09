namespace Pylon.ModLoader.Textures;

/// <summary>一个原生类（可带两层嵌套）的定位描述：命名空间 + 类名 + 嵌套名 + 字段黑名单。</summary>
internal sealed class TableClass(string ns, string name, string? nested = null, string? nested2 = null, string[]? skip = null)
{
    internal string Ns { get; } = ns ?? "";
    internal string Name { get; } = name;
    internal string? Nested { get; } = nested;
    internal string? Nested2 { get; } = nested2;
    internal string[]? Skip { get; } = skip;

    public override string ToString() =>
        (Ns.Length == 0 ? "" : Ns + ".") + Name +
        (string.IsNullOrEmpty(Nested) ? "" : "." + Nested) +
        (string.IsNullOrEmpty(Nested2) ? "" : "." + Nested2);
}
