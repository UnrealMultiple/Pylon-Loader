using Pylon.ModLoader;
using Terraria;
using Pylon.ModLoader.Globals;

namespace PipelineTest.Content;

/// <summary>装备全局链探针：UpdateEquip / UpdateVanity / IsArmorSet→UpdateArmorSet / 翅膀速度的全局回调计数。</summary>
public sealed class TestEquipGlobal : GlobalItem
{
    public static int UpdateEquipCalls;
    public static int UpdateVanityCalls;
    public static int IsArmorSetCalls;
    public static int UpdateArmorSetCalls;
    public static int VerticalCalls;
    public static int HorizontalCalls;

    public override void UpdateEquip(Item item, Player player) => UpdateEquipCalls++;

    public override void UpdateVanity(Item item, Player player) => UpdateVanityCalls++;

    public override string IsArmorSet(Item head, Item body, Item legs)
    {
        IsArmorSetCalls++;
        return "G";
    }

    public override void UpdateArmorSet(Player player, string set) => UpdateArmorSetCalls++;

    public override void VerticalWingSpeeds(Item? item, Player player, ref float ascentWhenFalling, ref float ascentWhenRising,
                                           ref float maxCanAscendMultiplier, ref float maxAscentMultiplier, ref float constantAscend) =>
        VerticalCalls++;

    public override void HorizontalWingSpeeds(Item? item, Player player, ref float speed, ref float acceleration) =>
        HorizontalCalls++;
}
