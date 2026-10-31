using Terraria;

namespace Pylon.ModLoader;

/// <summary>
/// ModProjectile 实体实例的映射与创建。槽位换内容时 SetDefaults 会再调且同类型复用同一地址，
/// 只按地址查表会拿到上一发遗留状态，所以 SetDefaults 走 Rebind（每次新建），hook 走 InstanceOf（复用绑定）。
/// </summary>
internal static class ProjectileEntities
{
    private static readonly EntityBinding<Projectile, ModProjectile> Binding = new();

    internal static ModProjectile? InstanceOf(Projectile? entity)
    {
        if (entity is null) return null;
        if (ModContent.Find(ContentKind.Projectile, entity.type) is not ModProjectile template) return null;

        var current = Binding.Get(entity);
        if (current is not null && current.Type == entity.type) return current;

        return Bind(template, entity);
    }

    /// <summary>SetDefaults 时强制换一份新实例。</summary>
    internal static ModProjectile? Rebind(Projectile? entity)
    {
        if (entity is null) return null;
        return ModContent.Find(ContentKind.Projectile, entity.type) is ModProjectile template
            ? Bind(template, entity)
            : null;
    }

    internal static long Address(Projectile? entity) => EntityBinding<Projectile, ModProjectile>.Address(entity);

    private static ModProjectile Bind(ModProjectile template, Projectile entity)
    {
        var instance = (ModProjectile)Activator.CreateInstance(template.GetType())!;
        instance.Type = template.Type;
        instance.Registered = template.Registered;
        instance.Mod = template.Mod;
        instance.Plan = template.Plan;
        instance.Template = template.Template;
        instance.Texture = template.Texture;
        instance.Spawned = false;
        instance.BoundEntity = entity;
        return Binding.Bind(entity, instance);
    }
}
