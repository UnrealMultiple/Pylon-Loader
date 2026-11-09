using System.Numerics;
using System.Reflection;

namespace Pylon.ModLoader;

/// <summary>把"这个类相对某个基类覆写了哪些方法"算成位集；按 (类型, 基类) 缓存。</summary>
internal static class HookScan
{
    private static readonly Dictionary<string, HookId> ByName = BuildNameTable();
    private static readonly Dictionary<(Type Type, Type Base), HookMask> Cache = [];
    private static readonly object Gate = new();

    /// <summary>声明类型不是基类即视为覆写（与旧 OverridePlan 语义一致）。</summary>
    public static HookMask Of(Type type, Type baseType)
    {
        var key = (type, baseType);
        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var cached)) return cached;
        }

        var builder = new HookMask.Builder();
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (method.IsAbstract || method.IsStatic) continue;
            if (method.DeclaringType == baseType) continue;

            // 属性钩子（如 ModTile.AdjTiles）编译成 get_AdjTiles，位集里存的是属性名；
            // 放置钩子叫 Hook_AfterPlacement，枚举里是 AfterPlacement。
            var name = method.Name;
            if (name.StartsWith("get_", StringComparison.Ordinal)) name = name.Substring(4);
            else if (name.StartsWith("Hook_", StringComparison.Ordinal)) name = name.Substring(5);
            if (ByName.TryGetValue(name, out var id)) builder.Add(id);
        }

        var mask = builder.Build();
        lock (Gate) Cache[key] = mask;
        return mask;
    }

    private static Dictionary<string, HookId> BuildNameTable()
    {
        var table = new Dictionary<string, HookId>(StringComparer.Ordinal);
        foreach (var value in Enum.GetValues<HookId>()) table[value.ToString()] = value;
        return table;
    }
}
