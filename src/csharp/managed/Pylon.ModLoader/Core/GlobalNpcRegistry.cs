using Terraria;

namespace Pylon.ModLoader;

/// <summary>GlobalNpc 的注册表与分派。NPC 有"迟到实例化"：SetDefaults 之后类型才定，所以 Attach 每次重建实体级列表。</summary>
internal static class GlobalNpcRegistry
{
    private static readonly EntityGlobalRegistry<GlobalNpc, NPC> Registry = new(
        isPerEntity: static global => global.InstancePerEntity,
        appliesTo: static (global, npc, late) => global.AppliesToEntity(npc, late),
        createInstance: static (global, npc) => global.NewInstance(npc),
        address: NpcEntities.Address,
        defaultLateInstantiation: true,
        baseType: typeof(GlobalNpc));

    internal static void Register(GlobalNpc global) => Registry.Register(global);

    internal static IReadOnlyList<GlobalNpc> All => Registry.All;

    internal static List<GlobalNpc> Of(NPC? entity) => Registry.Of(entity);

    internal static List<GlobalNpc> For(NPC? entity, string method) => Registry.For(entity, method);

    internal static bool AnyWith(params string[] methods) => Registry.AnyWith(methods);

    internal static void Attach(NPC? entity, bool lateInstantiation) => Registry.Attach(entity, lateInstantiation);
}
