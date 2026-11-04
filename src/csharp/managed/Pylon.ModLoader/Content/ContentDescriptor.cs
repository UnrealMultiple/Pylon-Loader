namespace Pylon.ModLoader;

/// <summary>一条已注册内容的描述符。按全局序号存成数组，查询走 ref，不装箱。</summary>
internal readonly struct ContentDescriptor
{
    public readonly ContentKind Kind;
    public readonly int Type;
    public readonly int ModIndex;
    public readonly int Index;

    public ContentDescriptor(ContentKind kind, int type, int modIndex, int index)
    {
        Kind = kind;
        Type = type;
        ModIndex = modIndex;
        Index = index;
    }
}
