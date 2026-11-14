using Terraria;

namespace Pylon.ModLoader;

/// <summary>
/// Item.SetDefaults 接管：自定义 type 不走原版分支（原版会把 type 归零），
/// 改跑 ModItem 的默认值与 tooltip 重建。
/// </summary>
internal sealed class ItemDefaults(ItemBinding binding)
{
    public void Install() => On.Terraria.Item.SetDefaults += SetDefaults;

    private void SetDefaults(On.Terraria.Item.orig_SetDefaults orig, Item self, int type,
                             Terraria.GameContent.Items.ItemVariant variant)
    {
        if (type < 0)
        {
            orig(self, type, variant);
            return;
        }

        if (UnloadedItems.IsUnloadedType(type))
        {
            UnloadedItems.Apply(self, type);
            return;
        }

        if (ModContent.Find(ContentKind.Item, type) is null)
        {
            orig(self, type, variant);
            DispatchGlobals(self);
            return;
        }

        Apply(self, type);
    }

    private void Apply(Item item, int type)
    {
        try
        {
            item.ResetStats(type);
            item.type = type;

            var mod = binding.InstanceOf(item);
            if (mod is not null)
            {
                mod.SetDefaults(item);
                mod.PostSetDefaults(item);
            }

            ApplyEquipSlots(item);
            DispatchGlobals(item);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"Item.SetDefaults 自定义分支异常：{e.Message}");
        }
    }

    private static void DispatchGlobals(Item item)
    {
        if (item.type <= 0) return;
        if (!GlobalItemRegistry.AnyWith(HookId.SetDefaults)) return;

        GlobalItemRegistry.Attach(item, lateInstantiation: false);
        foreach (var global in GlobalItemRegistry.For(item, HookId.SetDefaults))
        {
            try
            {
                global.SetDefaults(item);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"{global.GetType().Name}.SetDefaults 异常：{Safe.Describe(e)}");
            }
        }
    }

    private static void ApplyEquipSlots(Item item)
    {
        var slots = EquipTable.SlotsOfItem(item.type);
        if (slots is null) return;

        foreach (var (part, slot) in slots)
        {
            if (slot < 0) continue;

            switch (part)
            {
                case EquipType.Head: item.headSlot = slot; break;
                case EquipType.Body: item.bodySlot = slot; break;
                case EquipType.Legs: item.legSlot = slot; break;
                case EquipType.HandOn when slot <= sbyte.MaxValue: item.handOnSlot = (sbyte)slot; break;
                case EquipType.HandOff when slot <= sbyte.MaxValue: item.handOffSlot = (sbyte)slot; break;
                case EquipType.Back when slot <= sbyte.MaxValue: item.backSlot = (sbyte)slot; break;
                case EquipType.Front when slot <= sbyte.MaxValue: item.frontSlot = (sbyte)slot; break;
                case EquipType.Shoes when slot <= sbyte.MaxValue: item.shoeSlot = (sbyte)slot; break;
                case EquipType.Waist when slot <= sbyte.MaxValue: item.waistSlot = (sbyte)slot; break;
                case EquipType.Wing when slot <= sbyte.MaxValue: item.wingSlot = (sbyte)slot; break;
                case EquipType.Shield when slot <= sbyte.MaxValue: item.shieldSlot = (sbyte)slot; break;
                case EquipType.Neck when slot <= sbyte.MaxValue: item.neckSlot = (sbyte)slot; break;
                case EquipType.Face when slot <= sbyte.MaxValue: item.faceSlot = (sbyte)slot; break;
                case EquipType.Balloon when slot <= sbyte.MaxValue: item.balloonSlot = (sbyte)slot; break;
                case EquipType.Beard when slot <= sbyte.MaxValue: item.beardSlot = (sbyte)slot; break;
            }
        }
    }
}
