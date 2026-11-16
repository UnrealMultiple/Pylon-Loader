using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Terraria;
using Terraria.GameContent;

namespace Pylon.ModLoader;

/// <summary>合成原料注入：AddMaterialsForCrafting 的列表按 200 一块装进自建 InventoryStorage 并追加到原版材料源；ConsumeItemsFrom 把消耗记回作者物品并派 ItemConsumedCallback。</summary>
internal static class PlayerCraftingHooks
{
    private const int ChunkSize = 200;

    private sealed class Source
    {
        public nint Player;
        public Item[] Items = [];
        public int[] Indexes = [];
        public ModPlayer.ItemConsumedCallback? Callback;
        public string Label = "";
        public Il2CppReferenceArray<Item> Array = null!;
        public InventoryStorage Storage = null!;
    }

    private static readonly Dictionary<nint, Source> Sources = [];

    private static IModLog Log => LoaderHost.Current.Log;

    internal static void Install()
    {
        On.Terraria.Recipe.BuildPlayerChestSourceList += BuildPlayerChestSourceList;
        On.Terraria.GameContent.CraftingRequests.ConsumeItemsFrom_Il2CppReferenceArray_Item_int_RequiredItemEntry_intRef_List_Item_int += ConsumeItemsFrom;
    }

    private static void BuildPlayerChestSourceList(
        On.Terraria.Recipe.orig_BuildPlayerChestSourceList orig,
        Player player, Il2CppSystem.Collections.Generic.List<DestinationInventory> outputList)
    {
        orig(player, outputList);

        if (!PlayerPipeline.Instance.ModsActive(HookId.AddMaterialsForCrafting)) return;

        Sources.Remove(player.Pointer);
        if (outputList is null) return;

        var seen = new HashSet<nint>();
        foreach (var mod in PlayerPipeline.Instance.ModsOf(player))
        {
            if (!mod.HookEnabled(HookId.AddMaterialsForCrafting)) continue;

            IEnumerable<Item>? materials;
            ModPlayer.ItemConsumedCallback? callback;
            try
            {
                materials = mod.AddMaterialsForCrafting(out callback);
            }
            catch (Exception e)
            {
                Report(mod, e);
                continue;
            }
            if (materials is null) continue;

            var items = new List<Item>();
            var indexes = new List<int>();
            var index = 0;
            foreach (var item in materials)
            {
                if (item is not null && item.type > 0 && item.stack > 0 && seen.Add(item.Pointer))
                {
                    items.Add(item);
                    indexes.Add(index);
                }
                index++;
            }

            for (var start = 0; start < items.Count; start += ChunkSize)
            {
                var count = Math.Min(ChunkSize, items.Count - start);
                var array = new Il2CppReferenceArray<Item>(count);
                for (var i = 0; i < count; i++) array[i] = items[start + i];

                var storage = new InventoryStorage();
                storage.maxItems = count;
                storage.item = array;

                var destination = new DestinationInventory(storage, player.Center);
                outputList.Add(destination);

                Sources[array.Pointer] = new Source
                {
                    Player = player.Pointer,
                    Items = items.GetRange(start, count).ToArray(),
                    Indexes = indexes.GetRange(start, count).ToArray(),
                    Callback = callback,
                    Label = mod.Mod?.Name + "/" + mod.GetType().Name,
                    Array = array,
                    Storage = storage,
                };
            }
        }
    }

    private static void ConsumeItemsFrom(
        On.Terraria.GameContent.CraftingRequests.orig_ConsumeItemsFrom_Il2CppReferenceArray_Item_int_RequiredItemEntry_intRef_List_Item_int orig,
        Il2CppReferenceArray<Item> inventory, int maxItems, Recipe.RequiredItemEntry req, ref int toConsume,
        Il2CppSystem.Collections.Generic.List<Item> consumedItems, int chestIndex)
    {
        Sources.TryGetValue(inventory.Pointer, out var source);
        if (source is null)
        {
            orig(inventory, maxItems, req, ref toConsume, consumedItems, chestIndex);
            return;
        }

        var before = new int[source.Items.Length];
        for (var i = 0; i < before.Length; i++) before[i] = source.Items[i].stack;

        var consumedStart = consumedItems?.Count ?? 0;
        orig(inventory, maxItems, req, ref toConsume, consumedItems, chestIndex);

        for (var i = 0; i < source.Items.Length; i++)
        {
            var item = source.Items[i];
            var slot = inventory[i];
            if (slot is null || slot.Pointer != item.Pointer)
            {
                // 原版把这一格消耗掉了：consumed 列表里指向它的条目换成克隆（物件马上要清空），
                // 再把作者物品 TurnToAir 后放回槽位，保持库存结构稳定。
                if (consumedItems is not null)
                {
                    for (var c = consumedStart; c < consumedItems.Count; c++)
                    {
                        var consumed = consumedItems[c];
                        if (consumed is not null && consumed.Pointer == item.Pointer) consumedItems[c] = item.Clone();
                    }
                }
                item.TurnToAir();
                inventory[i] = item;
            }
            if (item.stack >= before[i] || source.Callback is null) continue;
            try { source.Callback(item, source.Indexes[i]); }
            catch (Exception e) { Safe.Once("craft.source", $"[craft] {source.Label}.ItemConsumedCallback 异常：{Safe.Describe(e)}", true); }
        }
    }

    private static void Report(ModPlayer mod, Exception e) =>
        ContentErrors.Report($"[craft] {mod.Mod?.Name}/{mod.GetType().Name}.AddMaterialsForCrafting 异常：{Safe.Describe(e)}");
}
