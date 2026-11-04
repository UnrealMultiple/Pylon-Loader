namespace Pylon.ModLoader;

/// <summary>GlobalEmoteBubble 的注册表与分派：按基类精确判定覆写，没覆写的方法不进表。</summary>
internal static class GlobalEmoteBubbleRegistry
{
    private static readonly GlobalRegistry<GlobalEmoteBubble> Registry = new(typeof(GlobalEmoteBubble));

    internal static void Register(GlobalEmoteBubble global) => Registry.Register(global);

    internal static IReadOnlyList<GlobalEmoteBubble> All => Registry.All;

    internal static List<GlobalEmoteBubble> For(HookId hook) => Registry.For(hook);
}
