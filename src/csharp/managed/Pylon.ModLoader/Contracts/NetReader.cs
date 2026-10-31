using System.Collections;
using System.Globalization;
using Il2CppMicrosoft.Xna.Framework;

namespace Pylon.ModLoader;

/// <summary>Mod 侧网络读取器：构造时抓住接收到的值数组，按写入顺序读回（写侧是 NetWriter；读超界抛异常）。</summary>
public class NetReader
{
    /// <summary>抓住值数组；null 当空数组（参考：不是数组就当 []）。</summary>
    public NetReader(object[] values)
    {
        Values = values ?? [];
        Index = 0;
    }

    /// <summary>接收到的值数组；元素是数字/文本/布尔/布尔数组/Vector2（{X, Y} 由管线还原成 Vector2）。</summary>
    public object[] Values { get; }

    /// <summary>下一个要读的下标（Read 自增）。</summary>
    public int Index { get; private set; }

    /// <summary>还有没有没读的值（index &lt; values.length）。</summary>
    public bool HasMore => Index < Values.Length;

    /// <summary>读下一个值（原样）；读超界抛异常（参考里是 RangeError，C# 用最接近的 InvalidOperationException）。</summary>
    public object Read()
    {
        if (Index >= Values.Length)
        {
            throw new InvalidOperationException("NetReader: 读得比另一端写的还多（" + Values.Length + " 个值）");
        }
        return Values[Index++];
    }

    /// <summary>下一个值折算成 int32（JS 的 Number()|0：截断，NaN/无穷/解析不动是 0）。</summary>
    public int ReadInt32() => ToInt32(Read());

    /// <summary>下一个值折算成 float（JS 的 Number()）。</summary>
    public float ReadSingle() => ToFloat(Read());

    /// <summary>下一个值按 JS 真值折算成 bool（!!）。</summary>
    public bool ReadBoolean() => Truthy(Read());

    /// <summary>下一个值按 JS 的 String() 折算成文本。</summary>
    public string ReadString() => ToStringValue(Read());

    /// <summary>下一个值当布尔数组读（NetWriter.WriteFlags 写的那几个布尔）；不是数组就是 null。</summary>
    public bool[] ReadFlags()
    {
        var v = Read();
        if (v is bool[] flags) return flags;
        if (v is IEnumerable enumerable && v is not string)
        {
            var result = new List<bool>();
            foreach (var item in enumerable) result.Add(Truthy(item));
            return result.ToArray();
        }
        return null!;
    }

    /// <summary>下一个值当 Vector2 读（写入的 {X, Y}；管线把它还原成 Vector2 存进 values）。</summary>
    public Vector2 ReadVector2()
    {
        var v = (Vector2)Read();
        return new Vector2(v.X, v.Y);
    }

    /// <summary>下一个值折算成 byte（参考里是 ReadInt32 的别名，返回值类型照名字给）。</summary>
    public byte ReadByte() => unchecked((byte)ToInt32(Read()));

    /// <summary>下一个值折算成 sbyte（参考里是 ReadInt32 的别名，返回值类型照名字给）。</summary>
    public sbyte ReadSByte() => unchecked((sbyte)ToInt32(Read()));

    /// <summary>下一个值折算成 short（参考里是 ReadInt32 的别名，返回值类型照名字给）。</summary>
    public short ReadInt16() => unchecked((short)ToInt32(Read()));

    /// <summary>下一个值折算成 ushort（参考里是 ReadInt32 的别名，返回值类型照名字给）。</summary>
    public ushort ReadUInt16() => unchecked((ushort)ToInt32(Read()));

    /// <summary>下一个值折算成 uint（参考里是 ReadInt32 的别名，返回值类型照名字给）。</summary>
    public uint ReadUInt32() => unchecked((uint)ToInt32(Read()));

    /// <summary>下一个值折算成 int32（参考里是 ReadInt32 的别名，返回值类型照名字给）。</summary>
    public int Read7BitEncodedInt() => ToInt32(Read());

    /// <summary>下一个值折算成 long（参考里是 ReadSingle 的别名，返回值类型照名字给 long）。</summary>
    public long ReadInt64() => ToInt64(Read());

    /// <summary>下一个值折算成 double（参考里是 ReadSingle 的别名，返回值类型照名字给 double）。</summary>
    public double ReadDouble() => ToDouble(Read());

    // —— 以下转换内核与 TagCompound 同批同一套 JS 折算（照抄，语义一致） ——

    /// <summary>JS 的真值（ReadBoolean 用）：false/0/NaN/''/null 是假，其余为真。</summary>
    private static bool Truthy(object value)
    {
        switch (value)
        {
            case null: return false;
            case bool b: return b;
            case string s: return s.Length > 0;
            case float f: return !float.IsNaN(f) && f != 0f;
            case double d: return !double.IsNaN(d) && d != 0d;
            case decimal m: return m != 0m;
            case long n: return n != 0L;
            case ulong n: return n != 0UL;
            case int n: return n != 0;
            case uint n: return n != 0u;
            case short n: return n != 0;
            case ushort n: return n != 0;
            case byte n: return n != 0;
            case sbyte n: return n != 0;
            default: return true;
        }
    }

    /// <summary>按 JS 的 Number()|0 折算成 int32：截断，NaN/无穷/解析不动是 0。</summary>
    private static int ToInt32(object value)
    {
        switch (value)
        {
            case null: return 0;
            case bool b: return b ? 1 : 0;
            case string s: return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? ToInt32(d) : 0;
            case byte n: return n;
            case sbyte n: return n;
            case short n: return n;
            case ushort n: return n;
            case int n: return n;
            case uint n: return unchecked((int)n);
            case long n: return unchecked((int)n);
            case ulong n: return unchecked((int)n);
            case float n: return ToInt32(n);
            case double n: return ToInt32(n);
            case decimal n: return ToInt32((double)n);
            default: return 0;
        }
    }

    /// <summary>double 折算成 int32：截断；NaN/无穷是 0。</summary>
    private static int ToInt32(double value) =>
        double.IsNaN(value) || double.IsInfinity(value) ? 0 : unchecked((int)Math.Truncate(value));

    /// <summary>按 JS 的 Number() 折算成 float；解析不动是 0。</summary>
    private static float ToFloat(object value)
    {
        switch (value)
        {
            case null: return 0f;
            case bool b: return b ? 1f : 0f;
            case string s: return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0f;
            case byte n: return n;
            case sbyte n: return n;
            case short n: return n;
            case ushort n: return n;
            case int n: return n;
            case uint n: return n;
            case long n: return n;
            case ulong n: return n;
            case float n: return n;
            case double n: return (float)n;
            case decimal n: return (float)n;
            default: return 0f;
        }
    }

    /// <summary>按 JS 的 Number() 折算成 double；解析不动是 0。</summary>
    private static double ToDouble(object value)
    {
        switch (value)
        {
            case null: return 0d;
            case bool b: return b ? 1d : 0d;
            case string s: return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0d;
            case byte n: return n;
            case sbyte n: return n;
            case short n: return n;
            case ushort n: return n;
            case int n: return n;
            case uint n: return n;
            case long n: return n;
            case ulong n: return n;
            case float n: return n;
            case double n: return n;
            case decimal n: return (double)n;
            default: return 0d;
        }
    }

    /// <summary>double 折算成 long：截断；NaN/无穷是 0。</summary>
    private static long ToInt64(object value)
    {
        var d = ToDouble(value);
        return double.IsNaN(d) || double.IsInfinity(d) ? 0L : unchecked((long)Math.Truncate(d));
    }

    /// <summary>按 JS 的 String() 折算：true/false、数字不变式文本、null 是 "null"。</summary>
    private static string ToStringValue(object value)
    {
        switch (value)
        {
            case null: return "null";
            case string s: return s;
            case bool b: return b ? "true" : "false";
            case byte n: return n.ToString(CultureInfo.InvariantCulture);
            case sbyte n: return n.ToString(CultureInfo.InvariantCulture);
            case short n: return n.ToString(CultureInfo.InvariantCulture);
            case ushort n: return n.ToString(CultureInfo.InvariantCulture);
            case int n: return n.ToString(CultureInfo.InvariantCulture);
            case uint n: return n.ToString(CultureInfo.InvariantCulture);
            case long n: return n.ToString(CultureInfo.InvariantCulture);
            case ulong n: return n.ToString(CultureInfo.InvariantCulture);
            case float n: return n.ToString(CultureInfo.InvariantCulture);
            case double n: return n.ToString(CultureInfo.InvariantCulture);
            case decimal n: return n.ToString(CultureInfo.InvariantCulture);
            default: return Convert.ToString(value, CultureInfo.InvariantCulture)!;
        }
    }
}
