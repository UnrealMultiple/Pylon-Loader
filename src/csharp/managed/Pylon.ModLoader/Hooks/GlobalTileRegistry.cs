using Pylon.ModLoader.Globals;
using Pylon.ModLoader.Pipeline;

namespace Pylon.ModLoader.Hooks;
/// <summary>GlobalTile 的注册表与分派：按"这个类相对 GlobalTile 覆写了哪些钩子"建索引。</summary>
internal static class GlobalTileRegistry
{
    private static readonly GlobalRegistry<GlobalTile> Registry = new(baseType: typeof(GlobalTile));

    internal static void Register(GlobalTile global) => Registry.Register(global);

    internal static IReadOnlyList<GlobalTile> All => Registry.All;

    internal static List<GlobalTile> For(HookId hook) => Registry.For(hook);

    internal static bool AnyWith(params ReadOnlySpan<HookId> hooks) => Registry.AnyWith(hooks);
}
