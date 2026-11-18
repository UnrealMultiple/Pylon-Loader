using Pylon.ModLoader.Pipelines;
using Terraria;

namespace Pylon.ModLoader.Content;
/// <summary> RecipeGroup.TML.cs 的等价扩展成员。</summary>
public static class RecipeGroupTml
{
    extension(RecipeGroup group)
    {
        public string Key => RecipeTable.KeyOf(group) ?? "";

        public RecipeGroup Add(int itemID) => group.Add(itemID, null!);

        public static RecipeGroup Register(string key, string groupDescriptorKey, params int[] validItems)
        {
            var existing = RecipeTable.Group(key);
            if (existing is not null) return Merge(existing, validItems);

            var created = new RecipeGroup(groupDescriptorKey, validItems);
            created.Register();
            RecipeTable.SetGroup(key, created, string.IsNullOrEmpty(groupDescriptorKey) ? null : () => groupDescriptorKey);
            return created;
        }

        public static RecipeGroup Register(string key, Func<string> getName, params int[] validItems)
        {
            var existing = RecipeTable.Group(key);
            if (existing is not null) return Merge(existing, validItems);

            var created = new RecipeGroup(key, validItems);
            created.Register();
            RecipeTable.SetGroup(key, created, getName);
            return created;
        }
    }

    private static RecipeGroup Merge(RecipeGroup group, int[] items)
    {
        if (items is null) return group;

        foreach (var item in items) group.Add(item, null!);
        return group;
    }
}
