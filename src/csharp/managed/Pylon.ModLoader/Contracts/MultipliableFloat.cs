namespace Pylon.ModLoader;

/// <summary>可乘叠的浮点修饰量（命中盒/伤害倍率链用）；默认 1。</summary>
public readonly struct MultipliableFloat
{
    /// <summary>不改变任何值的一倍量。</summary>
    public static MultipliableFloat One = new(1f);

    /// <summary>实际倍率。</summary>
    public float Value { get; } = 1f;

    public MultipliableFloat() { }

    private MultipliableFloat(float f)
    {
        Value = f;
    }

    public static MultipliableFloat operator *(MultipliableFloat f1, MultipliableFloat f2) => new(f1.Value * f2.Value);
    public static MultipliableFloat operator *(MultipliableFloat f1, float f2) => new(f1.Value * f2);
    public static MultipliableFloat operator /(MultipliableFloat f1, float f2) => new(f1.Value / f2);
}
