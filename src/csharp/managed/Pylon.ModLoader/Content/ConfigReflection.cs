using System.Reflection;

namespace Pylon.ModLoader;

/// <summary>配置选项的反射收集：public 字段/属性 + Shown 过滤，按声明深度与元数据序排定（缓存）。</summary>
internal static class ConfigReflection
{
    private static readonly Dictionary<Type, ConfigOption[]> Cache = [];
    private static readonly object Gate = new();

    internal static ConfigOption[] OptionsOf(Type type)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(type, out var cached)) return cached;

            var members = new List<MemberInfo>();
            members.AddRange(type.GetFields(BindingFlags.Public | BindingFlags.Instance));
            members.AddRange(type.GetProperties(BindingFlags.Public | BindingFlags.Instance));

            var found = members
                .Where(m => m.DeclaringType != typeof(ModConfig) && m.DeclaringType != typeof(ModType) &&
                            m.DeclaringType!.IsSubclassOf(typeof(ModConfig)) && ConfigOption.Shown(m))
                .OrderBy(m => Depth(m.DeclaringType!)).ThenBy(m => m.MetadataToken)
                .Select(m => new ConfigOption(m))
                .ToArray();

            Cache[type] = found;
            return found;
        }
    }

    private static int Depth(Type type)
    {
        int depth = 0;
        for (Type? t = type; t != null; t = t.BaseType)
            depth++;
        return depth;
    }
}
