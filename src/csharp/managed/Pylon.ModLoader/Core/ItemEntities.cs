using Terraria;

namespace Pylon.ModLoader;

/// <summary>ModItem 实体实例的映射与创建（Item 实体 ↔ ModItem 实例）。</summary>
internal static class ItemEntities
{
    private static readonly EntityBinding<Item, ModItem> Binding = new();

    /// <summary>拿这个实体对应的实例；没有或 type 变了就新建并绑定。</summary>
    internal static ModItem? InstanceOf(Item? entity)
    {
        if (entity is null) return null;
        if (ModContent.Find(ContentKind.Item, entity.type) is not ModItem template) return null;

        var current = Binding.Get(entity);
        if (current is not null && current.Type == entity.type) return current;

        var instance = NewInstance(template);
        instance.Item = entity;
        return Binding.Bind(entity, instance);
    }

    /// <summary>按模板新建一份实例（不绑定实体；Clone 复制字段用）。</summary>
    internal static ModItem NewInstance(ModItem template)
    {
        var instance = (ModItem)Activator.CreateInstance(template.GetType())!;
        instance.Type = template.Type;
        instance.Registered = template.Registered;
        instance.Mod = template.Mod;
        instance.Plan = template.Plan;
        instance.Texture = template.Texture;
        return instance;
    }

    internal static long Address(Item? entity) => EntityBinding<Item, ModItem>.Address(entity);
}
