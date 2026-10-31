using Terraria;

namespace Pylon.ModLoader;

/// <summary>
/// ModNpc 实体实例的映射与创建。NPC 槽位复用频繁（同类型敌人同格重生），
/// 所以 SetDefaults 一律 Rebind：每只新 NPC 都是新实例。
/// </summary>
internal static class NpcEntities
{
    private static readonly EntityBinding<NPC, ModNpc> Binding = new();

    /// <summary>拿实体对应的实例；原版类型一次比较就返回（每帧每个钩子都会走）。</summary>
    internal static ModNpc? InstanceOf(NPC? entity)
    {
        if (entity is null) return null;
        if (FindTemplate(entity.type) is not { } template) return null;

        var current = Binding.Get(entity);
        return current is not null && current.Type == entity.type ? current : Bind(template, entity);
    }

    /// <summary>SetDefaults 时强制换一份新实例（槽位复用/同类型重生都不继承旧状态）。</summary>
    internal static ModNpc? Rebind(NPC? entity) =>
        entity is null || FindTemplate(entity.type) is not { } template ? null : Bind(template, entity);

    /// <summary>CloneDefaults 借用另一类型的 SetDefaults 之后，把绑定还原成原实例。</summary>
    internal static void RebindTo(NPC? entity, ModNpc? instance)
    {
        if (entity is null || instance is null) return;
        Binding.Bind(entity, instance);
    }

    internal static long Address(NPC? entity) => EntityBinding<NPC, ModNpc>.Address(entity);

    private static ModNpc? FindTemplate(int type) =>
        type < VanillaCounts.Npc ? null : ModContent.Find(ContentKind.Npc, type) as ModNpc;

    private static ModNpc Bind(ModNpc template, NPC entity)
    {
        var instance = (ModNpc)Activator.CreateInstance(template.GetType())!;
        instance.Type = template.Type;
        instance.Registered = template.Registered;
        instance.Mod = template.Mod;
        instance.Plan = template.Plan;
        instance.Texture = template.Texture;
        instance.BoundEntity = entity;
        return Binding.Bind(entity, instance);
    }
}
