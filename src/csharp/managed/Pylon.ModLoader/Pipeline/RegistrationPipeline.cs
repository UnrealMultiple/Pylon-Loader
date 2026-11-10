using System.Reflection;

namespace Pylon.ModLoader;

/// <summary>
/// 注册管线：把一个 Mod 程序集里的类型变成"已占号的内容"。
/// 不碰游戏对象、不调用作者的内容代码，只做发现、实例化、发号与索引。
/// </summary>
internal static class RegistrationPipeline
{
    public static List<ModType> Register(LoaderHost host, Mod mod)
    {
        var types = new List<ModType>();
        foreach (var type in SortedTypes(mod.GetType().Assembly))
        {
            if (type is { IsClass: true, IsAbstract: false } && typeof(ModType).IsAssignableFrom(type))
            {
                var content = Create<ModType>(mod, type);
                if (content is null) continue;

                types.Add(content);
                RegisterGlobal(content);
                continue;
            }

            if (type is { IsClass: true, IsAbstract: false } && typeof(ModPlayer).IsAssignableFrom(type))
            {
                var player = Create<ModPlayer>(mod, type);
                if (player is not null) PlayerPipeline.Instance.Register(player);
            }
        }

        host.Content.Register(mod, types);
        return types;
    }

    private static T? Create<T>(Mod mod, Type type) where T : class
    {
        if (type.GetConstructor(Type.EmptyTypes) is null)
        {
            mod.Logger.Warn($"{type.FullName} 没有公开无参构造函数，跳过");
            return null;
        }

        try
        {
            var instance = (T)Activator.CreateInstance(type)!;
            switch (instance)
            {
                case ModType content:
                    content.Mod = mod;
                    // 按"相对框架内容基类"扫描:ModItem / ModProjectile / GlobalItem 等
                    // 各自的虚槽都要点亮,不能只认 ModType 自己声明的那几个。
                    content.Plan = HookScan.Of(type);
                    break;
                case ModPlayer player:
                    player.Mod = mod;
                    break;
            }
            return instance;
        }
        catch (Exception e)
        {
            mod.Logger.Warn($"{type.FullName} 实例化失败：{Safe.Describe(e)}");
            return null;
        }
    }

    private static void RegisterGlobal(ModType content)
    {
        switch (content)
        {
            case GlobalItem global: GlobalItemRegistry.Register(global); break;
            case GlobalProjectile global: GlobalProjectileRegistry.Register(global); break;
            case GlobalNpc global: GlobalNpcRegistry.Register(global); break;
            case GlobalBuff global: GlobalBuffRegistry.Register(global); break;
            case GlobalTile global: GlobalTileRegistry.Register(global); break;
            case GlobalRecipe global: GlobalRecipeRegistry.Register(global); break;
            case GlobalEmoteBubble global: GlobalEmoteBubbleRegistry.Register(global); break;
        }
    }

    /// <summary>按全名序数排序：实例化顺序与全局注册顺序都必须跨设备一致。</summary>
    private static Type[] SortedTypes(Assembly assembly)
    {
        var types = AllTypes(assembly);
        Array.Sort(types, static (left, right) => string.CompareOrdinal(left.FullName, right.FullName));
        return types;
    }

    /// <summary>GetTypes 的容错版：依赖缺失时 ReflectionTypeLoadException 会带走全部类型。</summary>
    private static Type[] AllTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            var loaded = new List<Type>(e.Types.Length);
            foreach (var type in e.Types)
            {
                if (type is not null) loaded.Add(type);
            }
            Safe.Warn($"类型枚举不完整：{Safe.Describe(e)}");
            return [.. loaded];
        }
    }
}
