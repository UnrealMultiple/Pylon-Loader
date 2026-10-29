using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace Pylon.ModLoader
{
    /// <summary>存档数据容器（世界/人物/物品的侧车存档）：字符串键的弱类型字典，落盘是 JSON。</summary>
    public class TagCompound : Dictionary<string, object>
    {
        /// <summary>存了几个键（Object.keys(this).length）。</summary>
        public new int Count => base.Count;

        /// <summary>这个键有没有存过（hasOwnProperty 语义）。</summary>
        public new bool ContainsKey(string key) => base.ContainsKey(key);

        /// <summary>键的值；没存过就返回 fallback（存过 null 就返回 null）。</summary>
        public object Get(string key, object fallback) => ContainsKey(key) ? base[key] : fallback;

        /// <summary>写一个键，已有就覆盖。</summary>
        public void Set(string key, object value) => base[key] = value;

        /// <summary>和 Set 一样（参考里就是转给 Set），重复键覆盖而不是抛异常。</summary>
        public new void Add(string key, object value) => Set(key, value);

        /// <summary>删掉这个键；没有也不报错（和参考一样不返回值）。</summary>
        public new void Remove(string key) => base.Remove(key);

        /// <summary>布尔读取；缺省 false，存的数字/字符串按 JS 真值折算。</summary>
        public bool GetBool(string key) => Truthy(Get(key, false));

        /// <summary>整数读取；缺省 0，字符串试着解析、解析不动就是 0。</summary>
        public int GetInt(string key) => ToInt32(Get(key, 0));

        /// <summary>浮点读取；缺省 0，字符串试着解析、解析不动就是 0。</summary>
        public float GetFloat(string key) => ToFloat(Get(key, 0));

        /// <summary>字符串读取；缺省 ''，数字/布尔/null 按 JS 的 String() 折算。</summary>
        public string GetString(string key) => ToStringValue(Get(key, ""));

        /// <summary>列表读取；缺省返回新的空表，存的是 List&lt;object&gt; 就原样返回。</summary>
        public List<object> GetList(string key)
        {
            if (!TryGetValue(key, out var value)) return new List<object>();
            if (value is List<object> list) return list;
            if (value is IEnumerable enumerable && value is not string && value is not IDictionary)
            {
                var result = new List<object>();
                foreach (var item in enumerable) result.Add(item);
                return result;
            }
            return new List<object>();
        }

        /// <summary>子存档读取；存的是字典/TagCompound 就抄一份，不是或没有就是空的。</summary>
        public TagCompound GetCompound(string key) => From(TryGetValue(key, out var value) ? value : null);

        /// <summary>从一个字典/TagCompound 抄一份键值（浅拷贝）；null 或不是字典就是空的。</summary>
        public static TagCompound From(object data)
        {
            var tag = new TagCompound();
            if (data is IDictionary<string, object> dictionary)
            {
                foreach (var pair in dictionary) tag.Set(pair.Key, pair.Value);
            }
            return tag;
        }

        /// <summary>JS 的真值：false/0/NaN/''/null 是假，其余为真。</summary>
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
                default: return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }
    }
}
