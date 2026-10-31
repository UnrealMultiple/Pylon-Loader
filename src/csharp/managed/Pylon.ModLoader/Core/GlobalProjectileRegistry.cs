using Terraria;

namespace Pylon.ModLoader;

/// <summary>GlobalProjectile 的注册表与分派。弹幕同样有"迟到实例化"，Attach 在每次 SetDefaults 时重建实体级列表。</summary>
internal static class GlobalProjectileRegistry
{
    private static readonly EntityGlobalRegistry<GlobalProjectile, Projectile> Registry = new(
        isPerEntity: static global => global.InstancePerEntity,
        appliesTo: static (global, projectile, late) => global.AppliesToEntity(projectile, late),
        createInstance: static (global, projectile) => global.NewInstance(projectile),
        address: ProjectileEntities.Address,
        defaultLateInstantiation: true);

    internal static void Register(GlobalProjectile global) => Registry.Register(global);

    internal static IReadOnlyList<GlobalProjectile> All => Registry.All;

    internal static List<GlobalProjectile> TemplatesFor(string method) => Registry.TemplatesFor(method);

    internal static List<GlobalProjectile> For(Projectile? entity, string method) => Registry.For(entity, method);

    internal static bool AnyWith(params string[] methods) => Registry.AnyWith(methods);

    internal static bool AnyFor(Projectile? entity, params string[] methods) => Registry.AnyFor(entity, methods);

    internal static List<GlobalProjectile> Of(Projectile? entity) => Registry.Of(entity);

    internal static void Attach(Projectile? entity, bool lateInstantiation) => Registry.Attach(entity, lateInstantiation);
}
