namespace Pylon.ModLoader;

/// <summary>GlobalBuff 的注册表与分派：buff 没有实体，钩子自己按 type 挑。</summary>
internal static class GlobalBuffRegistry
{
    private static readonly GlobalRegistry<GlobalBuff> Registry = new();

    internal static void Register(GlobalBuff global) => Registry.Register(global);

    internal static IReadOnlyList<GlobalBuff> All => Registry.All;

    internal static List<GlobalBuff> For(string method) => Registry.For(method);
}
