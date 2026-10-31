using System.Collections.Frozen;
using System.Reflection;

namespace Pylon.ModLoader;

/// <summary>注册期算好的"这个类覆写了哪些钩子"，分派时只查集合。</summary>
internal sealed class OverridePlan
{
    private readonly FrozenSet<string> _overridden;

    private OverridePlan(FrozenSet<string> overridden) => _overridden = overridden;

    public int Count => _overridden.Count;

    /// <summary>覆写的全部方法名（Global 注册表按它建方法索引）。</summary>
    public IEnumerable<string> Names => _overridden;

    public bool Has(string hook) => _overridden.Contains(hook);

    /// <summary>声明类型不是基类即视为覆写；继承来的空实现不算。</summary>
    public static OverridePlan Build(Type type, Type baseType)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (method.IsAbstract || method.IsStatic) continue;
            if (method.DeclaringType != baseType) names.Add(method.Name);
        }
        return new OverridePlan(names.ToFrozenSet(StringComparer.Ordinal));
    }
}
