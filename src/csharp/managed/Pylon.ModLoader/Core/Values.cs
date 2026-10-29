// 层：框架 / 值转换 —— 配置值（object）与布局字段（强类型）之间的窄口。
// 叫 API 而不叫 Convert，是不跟 System.Convert 同作用域打架（编译器只认最内层的那个）。
using System;
using System.Globalization;

namespace Pylon.ModLoader
{
    /// <summary>值转换的窄口：配置值是 object，布局字段/枚举是强类型，中间就靠这几个。</summary>
    internal static class API
    {
        /// <summary>把任意值转成 target 类型；转不动返回 null。</summary>
        public static object Convert(object value, Type target)
        {
            if (value == null) return null;
            if (target.IsInstanceOfType(value)) return value;

            var nullable = Nullable.GetUnderlyingType(target);
            if (nullable != null) return Convert(value, nullable);

            try
            {
                if (target.IsEnum)
                    return Enum.ToObject(target, System.Convert.ToInt64(value, CultureInfo.InvariantCulture));
                if (value is string text && target == typeof(bool)) return bool.Parse(text);
                if (value.GetType().IsEnum && target.IsPrimitive)
                    return System.Convert.ChangeType(System.Convert.ToInt64(value, CultureInfo.InvariantCulture),
                        target, CultureInfo.InvariantCulture);
                return System.Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
            }
            catch { return null; }
        }

        /// <summary>泛型版：转不动（或 null）给 fallback。</summary>
        public static T Convert<T>(object value, T fallback = default)
        {
            if (value is T typed) return typed;
            var converted = Convert(value, typeof(T));
            return converted is T result ? result : fallback;
        }
    }
}
