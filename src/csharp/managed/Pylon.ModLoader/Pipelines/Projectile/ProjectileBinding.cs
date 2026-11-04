using Terraria;

namespace Pylon.ModLoader;

/// <summary>
/// Projectile 实体 ↔ ModProjectile 实例。
/// 槽位会被复用：SetDefaults 走 <see cref="Rebind"/> 强制换一份新实例，钩子走 <see cref="InstanceOf"/> 复用已有绑定。
/// </summary>
internal sealed class ProjectileBinding
{
    private readonly EntityBinding<Projectile, ModProjectile> _binding = new();

    public ModProjectile? InstanceOf(Projectile? entity)
    {
        if (entity is null) return null;
        if (ModContent.Find(ContentKind.Projectile, entity.type) is not ModProjectile template) return null;

        var current = _binding.Get(entity);
        if (current is not null && current.Type == entity.type) return current;

        return Bind(template, entity);
    }

    /// <summary>SetDefaults 时强制换新实例：否则会拿到上一发留在同一地址上的状态。</summary>
    public ModProjectile? Rebind(Projectile? entity)
    {
        if (entity is null) return null;
        return ModContent.Find(ContentKind.Projectile, entity.type) is ModProjectile template
            ? Bind(template, entity)
            : null;
    }

    public static long Address(Projectile? entity) => EntityBinding<Projectile, ModProjectile>.Address(entity);

    private ModProjectile Bind(ModProjectile template, Projectile entity)
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
        return _binding.Bind(entity, instance);
    }
}
