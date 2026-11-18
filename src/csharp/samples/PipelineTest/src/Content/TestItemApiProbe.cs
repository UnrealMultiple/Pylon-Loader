using Pylon.ModLoader;
using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;

namespace PipelineTest.Content;

/// <summary>Item TML 探针：全局访问、开袋规则与新的全局钩子。</summary>
public sealed class TestItemApiProbe : GlobalItem
{
    public static int LootCalls;
    public static int CanRightClickCalls;

    public override bool AppliesToEntity(Item entity, bool lateInstantiation) =>
        entity.type == ModContent.ItemType<TestItem>();

    public override bool CanRightClick(Item item)
    {
        CanRightClickCalls++;
        return false;
    }

    public override void ModifyItemLoot(Item item, ItemLoot itemLoot)
    {
        LootCalls++;
        itemLoot.Add(ItemDropRule.Common(ItemID.Gel, 1, 1, 1));
    }

    public override void UpdateVanity(Item item, Player player) { }
}
