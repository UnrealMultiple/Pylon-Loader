using Terraria;

namespace Pylon.ModLoader;

/// <summary>GlobalItem 的注册表与分派。方法名 → 覆写了它的模板由注册期算好，分派时只查缓存。</summary>
internal static class GlobalItemRegistry
{
    private static readonly EntityGlobalRegistry<GlobalItem, Item> Registry = new(
        isPerEntity: static global => global.InstancePerEntity,
        appliesTo: static (global, item, late) => global.AppliesToEntity(item, late),
        createInstance: static (global, item) => global.NewInstance(item),
        address: ItemBinding.AddressOf,
        defaultLateInstantiation: false);

    internal static void Register(GlobalItem global) => Registry.Register(global);

    internal static void Attach(Item? entity, bool lateInstantiation) => Registry.Attach(entity, lateInstantiation);

    internal static IReadOnlyList<GlobalItem> All => Registry.All;

    internal static List<GlobalItem> Of(Item? entity) => Registry.Of(entity);

    internal static List<GlobalItem> For(Item? entity, HookId hook) => Registry.For(entity, hook);

    internal static bool AnyWith(params ReadOnlySpan<HookId> hooks) => Registry.AnyWith(hooks);
}
