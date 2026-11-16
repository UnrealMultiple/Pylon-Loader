using Terraria;

namespace Pylon.ModLoader;

/// <summary>Item 实体 ↔ ModItem 实例：实体换 type 或复制时重建，模板本身不挂状态。</summary>
internal sealed class ItemBinding
{
    private readonly EntityBinding<Item, ModItem> _binding = new();

    public ModItem? InstanceOf(Item? entity)
    {
        if (entity is null) return null;
        if (ModContent.Find(ContentKind.Item, entity.type) is not ModItem template) return null;

        var current = _binding.Get(entity);
        if (current is not null && current.Type == entity.type) return current;

        var instance = template.NewInstance(entity);
        instance.Type = template.Type;
        instance.Registered = template.Registered;
        instance.Plan = template.Plan;
        instance.Texture = template.Texture;
        instance.Item = entity;
        return _binding.Bind(entity, instance);
    }

    public static long AddressOf(Item? entity) => EntityBinding<Item, ModItem>.Address(entity);
}
