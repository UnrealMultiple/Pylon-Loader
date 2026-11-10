using System.Numerics;
using System.Reflection;

namespace Pylon.ModLoader;

/// <summary>把"这个类相对某个基类覆写了哪些方法"算成位集；按 (类型, 基类) 缓存。</summary>
internal static class HookScan
{
    private static readonly Dictionary<string, HookId> ByName = BuildNameTable();
    private static readonly Dictionary<(Type Type, Type Base), HookMask> Cache = [];
    private static readonly Dictionary<Type, HookMask> RelativeCache = [];
    private static readonly object Gate = new();

    /// <summary>
    /// 不指定内容基类:凡是"基定义落在 Mod 程序集之外"的虚方法都算作者覆写。
    /// 内容基类(ModItem / ModProjectile / GlobalItem / GlobalBuff ...)数量多且会随版本增加,
    /// 逐个硬编码容易漏;用"基定义来自框架"这一条即可同时覆盖它们,又能排除 Mod 内部同名 helper
    /// 与 new 隐藏(它们的基定义就是自己,落在 Mod 程序集内)。
    /// </summary>
    public static HookMask Of(Type type)
    {
        lock (Gate)
        {
            if (RelativeCache.TryGetValue(type, out var cached)) return cached;
        }

        var builder = new HookMask.Builder();
        var assembly = type.Assembly;

        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (method.IsAbstract || method.IsStatic || !method.IsVirtual) continue;

            var definition = method.GetBaseDefinition();
            var declared = definition.DeclaringType;
            // 声明即基定义 = 没有覆写;基定义也在 Mod 程序集里 = 作者自己的中间基类,不是框架虚槽。
            if (declared is null || declared == method.DeclaringType || declared.Assembly == assembly) continue;

            var name = method.Name;
            if (name.StartsWith("get_", StringComparison.Ordinal)) name = name.Substring(4);
            else if (name.StartsWith("Hook_", StringComparison.Ordinal)) name = name.Substring(5);
            if (ByName.TryGetValue(name, out var id)) builder.Add(id);
        }

        var mask = builder.Build();
        lock (Gate) RelativeCache[type] = mask;
        return mask;
    }

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
            if (method.IsAbstract || method.IsStatic || !method.IsVirtual) continue;
            if (method.DeclaringType == baseType) continue;
            // 同名 helper/new 隐藏方法不是作者回调；只接受目标基类虚槽的真实覆写。
            if (method.GetBaseDefinition().DeclaringType != baseType) continue;

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
