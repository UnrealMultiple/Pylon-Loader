using System.Diagnostics.CodeAnalysis;
using Terraria;

namespace Pylon.ModLoader.Pipelines;
/// <summary>
/// 箱子槽的实例数据侧表：按 (箱子, 槽位) 记住 mod 物品随 <see cref="Pylon.ModLoader.Contracts.NetWriter"/> 过网的那些值。
/// 游戏的箱子槽是编号/堆叠/前缀/收藏四字段的精简结构，实例数据进箱子后没有容身之处；
/// 这里在"物品层"与"精简槽"两条同步路径上各补一站——物品存进箱子时从物品抓取
/// （<c>InventoryStorage.SyncToChestItem</c>），箱子槽展开成完整物品时贴回去
/// （<c>SyncFromChestItem</c> / <c>SyncFromChest</c>，开箱即由构造路径调用）。
/// 侧表只在本次世界会话内有效：世界箱子存档不收 mod 物品数据（既定豁免），换世界即清空；
/// 绕过物品层直接写箱子槽的路径（机关沉积等）不经过这两站，不在覆盖范围。
/// </summary>
internal sealed class ChestTable
{
    private readonly Dictionary<(int Chest, int Slot), ItemSync.Envelope> _slots = [];

    /// <summary>挂物品层与箱子槽之间的两站钩子；世界重置时清表。</summary>
    internal void Install()
    {
        On.Terraria.InventoryStorage.SyncToChestItem += Capture;
        On.Terraria.InventoryStorage.SyncFromChestItem += Hydrate;
        On.Terraria.InventoryStorage.SyncFromChest += HydrateAll;
        On.Terraria.Chest.Clear += Reset;
    }

    /// <summary>箱子槽当前是否带着可发的实例数据（槽里还是同一件物品且没空）。</summary>
    internal bool TryGet(int chest, int slot, [NotNullWhen(true)] out ItemSync.Envelope? envelope)
    {
        envelope = null;

        if (!IsWorldChest(chest, out var self)) return false;

        var slots = self.item;
        if (slots is null || (uint)slot >= (uint)slots.Length) return false;

        var compact = slots[slot];
        if (compact.type <= 0 || compact.stack <= 0) return false;
        if (!_slots.TryGetValue((chest, slot), out var entry) || entry.Type != compact.type) return false;

        envelope = entry;
        return true;
    }

    /// <summary>
    /// 收到箱子槽的附属包，先记入侧表（原版处理器的服务器转发发生在它自己内部，
    /// 必须在原版动槽位之前就位，转发出的箱子包才带得上附属数据）。
    /// </summary>
    internal void Stage(int chest, int slot, ItemSync.Envelope envelope)
    {
        envelope.Type = ItemSync.TypeOf(envelope.Key);
        _slots[(chest, slot)] = envelope;
    }

    /// <summary>原版箱子槽处理完：贴附属数据到槽里展开出的物品上并刷新打开中的箱子界面；没带附属包就对齐本地条目。</summary>
    internal void Confirm(int chest, int slot, ItemSync.Envelope? envelope)
    {
        if (envelope is null)
        {
            Align(chest, slot);
            return;
        }

        if (!IsWorldChest(chest, out var self) || self.item is null || (uint)slot >= (uint)self.item.Length)
        {
            _slots.Remove((chest, slot));
            return;
        }

        var compact = self.item[slot];
        if (compact.type <= 0)
        {
            _slots.Remove((chest, slot));
            return;
        }

        var item = compact.ExpandItem();
        if (item is null || !ItemSync.Matches(item, envelope.Key))
        {
            _slots.Remove((chest, slot));
            return;
        }

        ItemSync.Apply(item, envelope);

        envelope.Type = compact.type;
        _slots[(chest, slot)] = envelope;

        InventoryStorage.SyncChestItemUpdateToStorage(chest, slot);
    }

    /// <summary>原版箱子槽更新（没带附属包）：槽空了或换了物品就撤掉本地条目。</summary>
    internal void Align(int chest, int slot)
    {
        if (!IsWorldChest(chest, out var self))
        {
            _slots.Remove((chest, slot));
            return;
        }

        var slots = self.item;
        if (slots is null || (uint)slot >= (uint)slots.Length)
        {
            _slots.Remove((chest, slot));
            return;
        }

        var compact = slots[slot];
        if (_slots.TryGetValue((chest, slot), out var entry) && entry.Type == compact.type && compact.stack > 0)
            return;

        _slots.Remove((chest, slot));
    }

    private void Capture(On.Terraria.InventoryStorage.orig_SyncToChestItem orig, InventoryStorage self, int slot)
    {
        orig(self, slot);

        if (!IsBoundToWorld(self, out var chest)) return;

        var item = SlotOf(self, slot);
        var envelope = item is not null && item.stack > 0 ? ItemSync.Build(item) : null;

        if (envelope is null || item is null)
        {
            _slots.Remove((chest.index, slot));
            return;
        }

        envelope.Type = item.type;
        _slots[(chest.index, slot)] = envelope;
    }

    private void Hydrate(On.Terraria.InventoryStorage.orig_SyncFromChestItem orig, InventoryStorage self, int slot)
    {
        orig(self, slot);
        HydrateSlot(self, slot);
    }

    private void HydrateAll(On.Terraria.InventoryStorage.orig_SyncFromChest orig, InventoryStorage self)
    {
        orig(self);

        var items = self.item;
        if (items is null) return;

        for (var slot = 0; slot < items.Length; slot++) HydrateSlot(self, slot);
    }

    private void HydrateSlot(InventoryStorage self, int slot)
    {
        if (!IsBoundToWorld(self, out var chest)) return;
        if (!_slots.TryGetValue((chest.index, slot), out var entry)) return;

        var item = SlotOf(self, slot);
        if (item is null || item.type != entry.Type) return;

        ItemSync.Apply(item, entry);
    }

    private void Reset(On.Terraria.Chest.orig_Clear orig)
    {
        orig();
        _slots.Clear();
    }

    private static Item? SlotOf(InventoryStorage self, int slot)
    {
        var items = self.item;
        if (items is null || (uint)slot >= (uint)items.Length) return null;

        return items[slot];
    }

    /// <summary>这个箱子界面对象绑定的得是 Main.chest 里那只世界箱子——商店之类代用箱子不算。</summary>
    private static bool IsBoundToWorld(InventoryStorage self, [NotNullWhen(true)] out Chest? chest)
    {
        chest = self.underlyingChest;
        if (chest is null || chest.bankChest || Main.chest is null) return false;
        if (chest.index < 0 || chest.index >= Main.chest.Length) return false;

        var bound = Main.chest[chest.index];
        if (bound is null || bound.Pointer != chest.Pointer) return false;

        return true;
    }

    private static bool IsWorldChest(int index, [NotNullWhen(true)] out Chest? chest)
    {
        chest = null;

        if (Main.chest is null || index < 0 || index >= Main.chest.Length) return false;

        var self = Main.chest[index];
        if (self is null || self.bankChest) return false;

        chest = self;
        return true;
    }
}
