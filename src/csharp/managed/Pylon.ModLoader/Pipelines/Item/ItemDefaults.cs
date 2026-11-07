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
        if (type < 0 || ModContent.Find(ContentKind.Item, type) is null)
        {
            orig(self, type, variant);
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

            // 不调 RebuildTooltip（item.ToolTip 是游戏管的结构体，重建走它的校验路径）。
            // 介绍已在安装期写进 Lang._itemTooltipCache，悬停时游戏自然取到。
            ApplyEquipSlots(item);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"Item.SetDefaults 自定义分支异常：{e.Message}");
        }
    }

    /// <summary>作者在 Load 里 AddEquipTexture 过的物品：SetDefaults 后自动把槽位写进 Item 的对应字段。</summary>
    private static void ApplyEquipSlots(Item item)
    {
        var slots = EquipTable.SlotsOfItem(item.type);
        if (slots is null) return;

        foreach (var (part, slot) in slots)
        {
            if (slot < 0 || slot > 127) continue;

            switch (part)
            {
                case EquipType.Head: item.headSlot = (sbyte)slot; break;
                case EquipType.Body: item.bodySlot = (sbyte)slot; break;
                case EquipType.Legs: item.legSlot = (sbyte)slot; break;
                case EquipType.HandOn: item.handOnSlot = (sbyte)slot; break;
                case EquipType.HandOff: item.handOffSlot = (sbyte)slot; break;
                case EquipType.Back: item.backSlot = (sbyte)slot; break;
                case EquipType.Front: item.frontSlot = (sbyte)slot; break;
                case EquipType.Shoes: item.shoeSlot = (sbyte)slot; break;
                case EquipType.Waist: item.waistSlot = (sbyte)slot; break;
                case EquipType.Wing: item.wingSlot = (sbyte)slot; break;
                case EquipType.Shield: item.shieldSlot = (sbyte)slot; break;
                case EquipType.Neck: item.neckSlot = (sbyte)slot; break;
                case EquipType.Face: item.faceSlot = (sbyte)slot; break;
                case EquipType.Balloon: item.balloonSlot = (sbyte)slot; break;
                case EquipType.Beard: item.beardSlot = (sbyte)slot; break;
            }
        }
    }
}
