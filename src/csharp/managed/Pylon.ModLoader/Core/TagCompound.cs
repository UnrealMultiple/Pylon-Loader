using System.Collections;
using System.Globalization;

namespace Pylon.ModLoader;

/// <summary>侧车存档的数据容器：字符串键的弱类型字典，落盘是 JSON。</summary>
public sealed class TagCompound : IEnumerable<KeyValuePair<string, object?>>
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> Keys => _values.Keys;

    public IReadOnlyCollection<object?> Values => _values.Values;

    public int Count => _values.Count;

    public object? this[string key]
    {
        get => _values[key];
        set => _values[key] = value;
    }

    public bool ContainsKey(string key) => _values.ContainsKey(key);

    public bool TryGetValue(string key, out object? value) => _values.TryGetValue(key, out value);

    /// <summary>写一个键，已有就覆盖。</summary>
    public void Set(string key, object? value) => _values[key] = value;

    /// <summary>与 <see cref="Set"/> 等价：重复键覆盖，不抛异常。</summary>
    public void Add(string key, object? value) => _values[key] = value;

    public bool Remove(string key) => _values.Remove(key);

    public void Clear() => _values.Clear();

    /// <summary>键的值；没存过返回 fallback（存过 null 则返回 null）。</summary>
    public object? Get(string key, object? fallback = null) =>
        _values.TryGetValue(key, out var value) ? value : fallback;

    public bool GetBool(string key) => Truthy(Get(key, false));

    public int GetInt(string key) => ToInt32(Get(key, 0));

    public float GetFloat(string key) => ToFloat(Get(key, 0));

    public string GetString(string key) => ToStringValue(Get(key, ""));

    /// <summary>列表读取；缺省返回空表。</summary>
    public List<object?> GetList(string key)
    {
        if (!_values.TryGetValue(key, out var value)) return [];
        if (value is List<object?> list) return list;
        if (value is IEnumerable enumerable and not string)
        {
            var result = new List<object?>();
            foreach (var item in enumerable) result.Add(item);
            return result;
        }
        return [];
    }

    /// <summary>子存档读取；不是字典就返回空。</summary>
    public TagCompound GetCompound(string key) => From(Get(key));

    /// <summary>浅拷贝一个字典/TagCompound；null 或不是字典时返回空。</summary>
    public static TagCompound From(object? data)
    {
        var tag = new TagCompound();
        if (data is IDictionary<string, object?> dictionary)
        {
            foreach (var (key, value) in dictionary) tag.Set(key, value);
        }
        else if (data is IDictionary<string, object> legacy)
        {
            foreach (var (key, value) in legacy) tag.Set(key, value);
        }
        return tag;
    }

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => _values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>JS 真值：false / 0 / NaN / "" / null 为假。</summary>
    private static bool Truthy(object? value) => value switch
    {
        null => false,
        bool b => b,
        string s => s.Length > 0,
        float f => !float.IsNaN(f) && f != 0f,
        double d => !double.IsNaN(d) && d != 0d,
        decimal m => m != 0m,
        sbyte n => n != 0,
        byte n => n != 0,
        short n => n != 0,
        ushort n => n != 0,
        int n => n != 0,
        uint n => n != 0u,
        long n => n != 0L,
        ulong n => n != 0UL,
        _ => true,
    };

    /// <summary>按 Number()|0 折算成 int：截断，NaN / 无穷 / 解析不动为 0。</summary>
    private static int ToInt32(object? value)
    {
        switch (value)
        {
            case null: return 0;
            case bool b: return b ? 1 : 0;
            case string s:
                return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                    ? ToInt32(parsed)
                    : 0;
            case sbyte n: return n;
            case byte n: return n;
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

    private static int ToInt32(double value) =>
        double.IsNaN(value) || double.IsInfinity(value) ? 0 : unchecked((int)Math.Truncate(value));

    /// <summary>按 Number() 折算成 float；解析不动为 0。</summary>
    private static float ToFloat(object? value)
    {
        switch (value)
        {
            case null: return 0f;
            case bool b: return b ? 1f : 0f;
            case string s:
                return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : 0f;
            case sbyte n: return n;
            case byte n: return n;
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

    /// <summary>按 String() 折算：true/false、数字不变式文本、null 是 "null"。</summary>
    private static string ToStringValue(object? value) => value switch
    {
        null => "null",
        string s => s,
        bool b => b ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
