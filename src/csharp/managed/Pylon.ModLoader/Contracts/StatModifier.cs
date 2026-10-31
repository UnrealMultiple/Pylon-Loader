namespace Pylon.ModLoader;

/// <summary>一份伤害/击退修正（同 tModLoader）：最终值 = (原值 + Base) * Additive * Multiplicative + Flat；对象可变，直接改字段（damage.Additive += 0.1）。</summary>
public class StatModifier
{
    /// <summary>加法倍率（+10% 伤害 = 1.1）。</summary>
    public float Additive;

    /// <summary>乘法倍率（+12% 击退 = 1.12）。</summary>
    public float Multiplicative;

    /// <summary>最后加上的平值。</summary>
    public float Flat;

    /// <summary>先加进基数里的平值。</summary>
    public float Base;

    /// <summary>默认 1/1/0/0，不传参数就是 Default。</summary>
    public StatModifier(float additive = 1f, float multiplicative = 1f, float flat = 0f, float @base = 0f)
    {
        Additive = additive;
        Multiplicative = multiplicative;
        Flat = flat;
        Base = @base;
    }

    /// <summary>什么加成都没有的默认修正。</summary>
    public static StatModifier Default => new();

    /// <summary>套到基数上：(baseValue + Base) * Additive * Multiplicative + Flat。</summary>
    public float ApplyTo(float baseValue) => (baseValue + Base) * Additive * Multiplicative + Flat;

    /// <summary>把两个修正先后叠加成一份。</summary>
    public StatModifier CombineWith(StatModifier m) => new(
        Additive + m.Additive - 1, Multiplicative * m.Multiplicative, Flat + m.Flat, Base + m.Base);

    /// <summary>只按 scale 比例生效（倍率差值缩放，平值同比例）。</summary>
    public StatModifier Scale(float scale) => new(
        1 + (Additive - 1) * scale, 1 + (Multiplicative - 1) * scale, Flat * scale, Base * scale);

    /// <summary>ApplyTo 的逆运算：已有结果反推基数。</summary>
    public float Undo(float currentValue) => (currentValue - Flat) / (Multiplicative * Additive) - Base;

    /// <summary>复制一份（改副本不碰原物）。</summary>
    public StatModifier Clone() => new(Additive, Multiplicative, Flat, Base);

    /// <summary>四个字段全等才 true；null 为 false。</summary>
    public bool Equals(StatModifier? m) => m != null && Additive == m.Additive && Multiplicative == m.Multiplicative && Flat == m.Flat && Base == m.Base;
}

// 未移植 ForValue（JS 专用桥接：valueOf 变数字，支持 "return damage * 2"，C# 无值语义等效物）与
// Resolve（hook 返回分发：数字优先否则 ApplyTo，属管线内部逻辑）。
