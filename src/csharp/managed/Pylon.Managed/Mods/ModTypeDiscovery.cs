using System.Reflection;
using Pylon.Mod;

namespace Pylon.Managed.Mods;

/// <summary>在 Mod 程序集里找唯一的 IPylonMod 实现。</summary>
internal static class ModTypeDiscovery
{
    /// <returns>唯一实现；没有实现时返回 null；多于一个实现时抛 <see cref="InvalidOperationException"/>。</returns>
    public static Type? Find(Assembly assembly)
    {
        var candidates = AllTypes(assembly)
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IPylonMod).IsAssignableFrom(type))
            .ToArray();

        return candidates.Length switch
        {
            1 => candidates[0],
            0 => ReportMissing(),
            _ => throw new InvalidOperationException(
                $"程序集里有 {candidates.Length} 个 IPylonMod 实现，无法判断加载哪个：" +
                string.Join(", ", candidates.Select(type => type.FullName))),
        };
    }

    private static Type? ReportMissing()
    {
        HostLog.Write("程序集里没有 IPylonMod 的实现（Mod 必须实现 Pylon.Mod.IPylonMod）");
        return null;
    }

    /// <summary>GetTypes 的容错版：依赖缺失时 ReflectionTypeLoadException 会带走全部类型。</summary>
    private static IEnumerable<Type> AllTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            HostLog.Write($"类型枚举不完整（依赖缺失？）：{HostLog.Describe(e)}");
            return e.Types.OfType<Type>();
        }
    }
}
