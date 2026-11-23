using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes;
using Pylon.ModLoader.Logging;

namespace Pylon.ModLoader.Core;
/// <summary>
/// 原生对象克隆：走 il2cpp 的 MemberwiseClone 拿一份全新原生实例（浅拷贝，字段值含子对象引用一起拷），
/// 失败时退回原生 Copy 方法，再退回逐属性拷贝。用于克隆原版 UI 的布局模板——我们要改它 Location/Scale
/// 等字段，不能动原版正在用的那份。
/// </summary>
internal static class NativeClone
{
    private static readonly Dictionary<Type, PropertyInfo[]> Properties = new();

    internal static T Of<T>(T source) where T : Il2CppObjectBase, new()
    {
        try
        {
            var result = ((Il2CppSystem.Object)(object)source).MemberwiseClone();
            if (result is T typed) return typed;
            var ctor = typeof(T).GetConstructor([typeof(IntPtr)]);
            if (ctor != null) return (T)ctor.Invoke(new object[] { ((Il2CppObjectBase)result).Pointer });
        }
        catch (Exception e)
        {
            Safe.Warn("原生 MemberwiseClone 失败（退回属性拷贝）：" + typeof(T).Name + " " + Safe.Describe(e));
        }
        return Copy(source);
    }

    private static T Copy<T>(T source) where T : Il2CppObjectBase, new()
    {
        var clone = new T();
        var copy = typeof(T).GetMethod("Copy", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null, [typeof(T)], null);
        if (copy != null)
        {
            try { copy.Invoke(clone, parameters: [source]); return clone; }
            catch (Exception e)
            {
                throw new InvalidOperationException($"原生 Copy 失败：{typeof(T).Name}（{e.Message}）", e);
            }
        }
        CopyProperties(source, clone);
        return clone;
    }

    private static void CopyProperties<T>(T from, T to)
    {
        var type = typeof(T);
        if (!Properties.TryGetValue(type, out var list))
        {
            Properties[type] = list = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        }
        foreach (var property in list)
        {
            if (!property.CanRead || !property.CanWrite) continue;
            if (property.GetIndexParameters().Length > 0) continue;
            try
            {
                property.SetValue(to, property.GetValue(from));
            }
            catch (Exception e)
            {
                throw new InvalidOperationException(
                    $"MemberwiseClone 等价拷贝失败：{type.Name}.{property.Name}（{e.Message}）", e);
            }
        }
    }
}
