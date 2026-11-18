using System.Collections;

namespace Pylon.ModLoader.SaveData;
/// <summary>侧车存档的数据容器：字符串键的弱类型字典，写入走 Normalize 归一化，读取走 TagConvert 泛型折算。</summary>
public sealed class TagCompound : IEnumerable<KeyValuePair<string, object?>>
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> Keys => _values.Keys;

    public IReadOnlyCollection<object?> Values => _values.Values;

    public int Count => _values.Count;

    public object? this[string key]
    {
        get => _values[key];
        set => Set(key, value);
    }

    public bool ContainsKey(string key) => _values.ContainsKey(key);

    public bool TryGetValue(string key, out object? value) => _values.TryGetValue(key, out value);

    /// <summary>写一个键：null 等价移除，其余值先归一化（bool→byte、enum→int、IList→List&lt;object&gt;），已有就覆盖。</summary>
    public void Set(string key, object? value)
    {
        if (value is null)
        {
            _values.Remove(key);
            return;
        }
        _values[key] = TagConvert.Normalize(value);
    }

    /// <summary>与 <see cref="Set"/> 等价：重复键覆盖，不抛异常。</summary>
    public void Add(string key, object? value) => Set(key, value);

    public bool Remove(string key) => _values.Remove(key);

    public void Clear() => _values.Clear();

    /// <summary>键的值；没存过返回 fallback（存过 null 则返回 null）。</summary>
    public object? Get(string key, object? fallback = null) =>
        _values.TryGetValue(key, out var value) ? value : fallback;

    /// <summary>泛型读取：同型直取，数值走 IConvertible 折算，列表逐项转换；转不动抛 InvalidCastException。</summary>
    public T Get<T>(string key)
    {
        _values.TryGetValue(key, out var raw);
        try
        {
            return TagConvert.To<T>(raw);
        }
        catch (Exception e) when (e is InvalidCastException or FormatException or OverflowException)
        {
            throw new InvalidCastException($"TagCompound: '{key}' 是 {raw?.GetType().Name ?? "缺省"}，不是 {typeof(T).Name}", e);
        }
    }

    /// <summary>泛型读取的容错版：转不动返回 false 与 default。</summary>
    public bool TryGet<T>(string key, out T value)
    {
        if (_values.TryGetValue(key, out var raw))
        {
            try
            {
                value = TagConvert.To<T>(raw);
                return true;
            }
            catch (Exception e) when (e is InvalidCastException or FormatException or OverflowException)
            {
            }
        }
        value = default!;
        return false;
    }

    public bool GetBool(string key) => Get<bool>(key);

    public byte GetByte(string key) => Get<byte>(key);

    public short GetShort(string key) => Get<short>(key);

    public int GetInt(string key) => Get<int>(key);

    public long GetLong(string key) => Get<long>(key);

    public float GetFloat(string key) => Get<float>(key);

    public double GetDouble(string key) => Get<double>(key);

    public string GetString(string key) => Get<string>(key) ?? "";

    public byte[] GetByteArray(string key) => Get<byte[]>(key);

    public int[] GetIntArray(string key) => Get<int[]>(key);

    /// <summary>泛型列表读取；缺省返回空表。</summary>
    public IList<T> GetList<T>(string key) => Get<List<T>>(key);

    /// <summary>子存档读取；不是字典就返回空。</summary>
    public TagCompound GetCompound(string key) => Get<TagCompound>(key) ?? new TagCompound();

    /// <summary>浅拷贝一个字典/TagCompound；null 或不是字典时返回空。</summary>
    public static TagCompound From(object? data)
    {
        var tag = new TagCompound();
        if (data is TagCompound compound)
        {
            foreach (var (key, value) in compound) tag.Set(key, value);
        }
        else if (data is IDictionary<string, object?> dictionary)
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

    /// <summary>深拷贝：数组与嵌套容器逐份复制，其余值按引用共享。</summary>
    public TagCompound Clone()
    {
        var copy = new TagCompound();
        foreach (var (key, value) in _values) copy._values[key] = TagConvert.Copy(value!);
        return copy;
    }
}

