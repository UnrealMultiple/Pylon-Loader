using System.Reflection;

namespace Pylon.ModLoader;

/// <summary>
/// 内容管线的安装入口：反射发现 <see cref="ILoaderModule"/> 实现，用首个 Mod 的日志构造它们，按 Order 排序。
/// "装过没有"只记在这里；各管线只回答"这次能不能装"（false = 内容还没出现，下次再试）。
/// </summary>
internal static class LoaderBootstrap
{
    private static readonly HashSet<ILoaderModule> Installed = [];

    private static ILoaderModule[]? _modules;

    public static void Install(ModLogger logger)
    {
        var modules = _modules ??= Discover(logger);
        if (modules.Length == 0)
        {
            logger.Warn("[loader] 没有发现任何内容管线");
            return;
        }

        foreach (var module in modules)
        {
            if (!Installed.Contains(module))
            {
                if (Safe.Run("loader." + module.Name, module.Install)) Installed.Add(module);
            }

            // 每次 Mod 注册后刷新：例如 NpcLoader 要重新算"有没有 Global 覆写这套钩子"。
            if (module is ILoaderModuleRefresh refresh)
            {
                Safe.Run("loader." + module.Name + ".refresh", refresh.Refresh);
            }
        }
    }

    /// <summary>扫描实现类并用日志构造；顺序由 Order 决定，反射顺序不参与。</summary>
    private static ILoaderModule[] Discover(ModLogger logger)
    {
        var modules = new List<ILoaderModule>();
        foreach (var type in AllTypes())
        {
            if (!type.IsClass || type.IsAbstract) continue;
            if (!typeof(ILoaderModule).IsAssignableFrom(type)) continue;

            if (Activator.CreateInstance(type, logger) is ILoaderModule instance) modules.Add(instance);
        }

        modules.Sort(static (left, right) => left.Order != right.Order
            ? left.Order.CompareTo(right.Order)
            : string.CompareOrdinal(left.Name, right.Name));

        return [.. modules];
    }

    /// <summary>GetTypes 的容错版：缺代理程序集时只丢掉加载不出来的类型，不让整个安装表消失。</summary>
    private static IEnumerable<Type> AllTypes()
    {
        try
        {
            return typeof(LoaderBootstrap).Assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            Safe.Warn("[loader] 类型枚举不完整：" + Safe.Describe(e));
            return e.Types.OfType<Type>();
        }
    }
}
