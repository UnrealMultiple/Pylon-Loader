using System.Globalization;

namespace Pylon.ModLoader;

/// <summary>配置值（object）到强类型的窄口转换。</summary>
internal static class ValueConverter
{
    /// <summary>转成 <paramref name="target"/> 类型；转不动返回 null。</summary>
    public static object? Convert(object? value, Type target)
    {
        if (value is null) return null;
        if (target.IsInstanceOfType(value)) return value;

        var underlying = Nullable.GetUnderlyingType(target);
        if (underlying is not null) return Convert(value, underlying);

        try
        {
            if (target.IsEnum) return Enum.ToObject(target, System.Convert.ToInt64(value, CultureInfo.InvariantCulture));
            if (value is string text && target == typeof(bool)) return bool.Parse(text);

            var source = value.GetType().IsEnum
                ? System.Convert.ToInt64(value, CultureInfo.InvariantCulture)
                : value;
            return System.Convert.ChangeType(source, target, CultureInfo.InvariantCulture);
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or OverflowException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>泛型版：转不动（或 null）时返回 fallback。</summary>
    public static T Convert<T>(object? value, T fallback = default!)
    {
        if (value is T typed) return typed;
        return Convert(value, typeof(T)) is T result ? result : fallback;
    }
}
