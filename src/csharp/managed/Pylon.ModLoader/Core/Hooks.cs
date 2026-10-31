using System.Reflection;

namespace Pylon.ModLoader;

/// <summary>Hook 安装的幂等门，以及"这个类是否覆写了某个钩子"的注册期判定。</summary>
internal static class Hooks
{
    private static readonly HashSet<string> Installed = new(StringComparer.Ordinal);

    /// <summary>同一 key 的安装只执行一次。</summary>
    internal static void Once(string key, Action install)
    {
        if (!Installed.Add(key)) return;
        install();
    }

    /// <summary>这个方法是不是这个类自己声明的（继承来的空实现不算覆写）。</summary>
    internal static bool Overrides(Type type, Type baseType, string method) =>
        type.GetMethod(
            method,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy
        )?.DeclaringType != baseType;
}
