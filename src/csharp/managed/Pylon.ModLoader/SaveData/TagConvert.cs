using System.Collections;
using System.Globalization;

namespace Pylon.ModLoader.SaveData;
/// <summary>侧车数据容器：类型归一化与泛型折算；数值走 InvariantCulture，列表/数组逐项转换。</summary>
internal static class TagConvert
{
    internal static object Normalize(object value) => value switch
    {
        bool b => (byte)(b ? 1 : 0),
        byte or short or int or long or float or double or string or byte[] or int[] or TagCompound => value,
        sbyte sb => (byte)sb,
        ushort us => (short)us,
        uint ui => (int)ui,
        ulong ul => (long)ul,
        char c => (short)c,
        Enum e => Convert.ToInt32(e, CultureInfo.InvariantCulture),
        IList list => NormalizeList(list),
        _ => throw new ArgumentException($"TagCompound: {value.GetType().Name} 不能落盘（只收数字、bool、string、byte[]、int[]、列表与 TagCompound）"),
    };

    private static List<object> NormalizeList(IList list)
    {
        var normalized = new List<object>(list.Count);
        foreach (var element in list)
        {
            if (element is null) throw new ArgumentException("TagCompound: 列表里不能有 null");
            normalized.Add(Normalize(element));
        }
        return normalized;
    }

    internal static object Copy(object value) => value switch
    {
        byte[] bytes => bytes.Clone(),
        int[] ints => ints.Clone(),
        TagCompound tag => tag.Clone(),
        List<object> list => list.ConvertAll(Copy),
        _ => value,
    };

    internal static T To<T>(object? value) => (T)To(typeof(T), value)!;

    private static object? To(Type type, object? value)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        if (value == null) return Empty(type);
        if (target.IsInstanceOfType(value) && target != typeof(object)) return value;
        if (target == typeof(object)) return value;
        if (target == typeof(bool)) return Convert.ToDouble(value, CultureInfo.InvariantCulture) != 0;
        if (target.IsEnum) return Enum.ToObject(target, Convert.ToInt64(value, CultureInfo.InvariantCulture));
        if (target.IsPrimitive || target == typeof(decimal)) return Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
        if (target == typeof(string)) return Convert.ToString(value, CultureInfo.InvariantCulture);
        if (value is not IList source) throw new InvalidCastException();
        if (target.IsArray)
        {
            var element = target.GetElementType()!;
            var array = Array.CreateInstance(element, source.Count);
            for (var i = 0; i < source.Count; i++) array.SetValue(To(element, source[i]), i);
            return array;
        }
        if (target.IsGenericType && IsListType(target.GetGenericTypeDefinition()))
        {
            var element = target.GetGenericArguments()[0];
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(element))!;
            foreach (var item in source) list.Add(To(element, item));
            return list;
        }
        throw new InvalidCastException();
    }

    private static object? Empty(Type type)
    {
        if (type.IsArray) return Array.CreateInstance(type.GetElementType()!, 0);
        if (type.IsGenericType && IsListType(type.GetGenericTypeDefinition()))
            return Activator.CreateInstance(typeof(List<>).MakeGenericType(type.GetGenericArguments()[0]));
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }

    private static bool IsListType(Type definition) =>
        definition == typeof(List<>) || definition == typeof(IList<>) || definition == typeof(ICollection<>) ||
        definition == typeof(IEnumerable<>) || definition == typeof(IReadOnlyList<>) || definition == typeof(IReadOnlyCollection<>);
}
