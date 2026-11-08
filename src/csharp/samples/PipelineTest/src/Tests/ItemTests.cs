using Pylon.ModLoader;
using Terraria;
using Terraria.ID;

namespace PipelineTest.Tests;

/// <summary>
/// 管线 17：Item —— 注册面（功能版在 ItemTests2.cs：使用链/绘制/世界）。
/// 断言 SetDefaults 真的落位、tooltip 链真的工作、原版物品不受污染。
/// </summary>
internal static class ItemTests
{
    public static void Register() => PT.Group("item", Phase.Ready, Run);

    private static void Run()
    {
        var type = ModContent.TypeOf<Content.TestItem>();

        PT.Case("ITEM-R1", c =>
        {
            c.True(type >= ItemID.Count, $"type={type} base={ItemID.Count}");
            c.NotNull(ModContent.GetInstance<Content.TestItem>(), "GetInstance<TestItem>");
        });

        PT.Case("ITEM-R2", c =>
        {
            var found = ModContent.Find<Content.TestItem>("PipelineTest/TestItem");
            c.NotNull(found, "Find<PipelineTest/TestItem>");
        });

        PT.Case("ITEM-S1", c =>
        {
            var item = new Item();
            item.SetDefaults(type, null);
            c.True(item.width == 20 && item.height == 20, $"size={item.width}x{item.height}（期望 20x20）");
            c.Eq(item.damage, 15, "damage");
            c.Eq(item.useTime, 20, "useTime");
        });

        PT.Case("ITEM-N1", c =>
        {
            var name = Lang.GetItemNameValue(type);
            if (string.IsNullOrEmpty(name)) PT.SkipCase("ITEM-N1", "名字为空（本地化时机）");
            else c.True(name != "ItemName." + "TestItem", "name=" + name + "（不是键名回显）");
        });

        PT.Case("ITEM-T1", c =>
        {
            var item = new Item();
            item.SetDefaults(type, null);
            item.RebuildTooltip();
            c.NotNull(item.ToolTip, "mod 物品 ToolTip");
            c.True(item.ToolTip.Lines >= 1, $"tooltip 行数={item.ToolTip.Lines}（TestItem 至少 1 行介绍）");
        });

        PT.Case("ITEM-T2", c =>
        {
            // 原版物品回归：SetDefaults 不崩、tooltip 正常
            var item = new Item();
            item.SetDefaults(ItemID.DirtBlock, null);
            c.Eq(item.type, ItemID.DirtBlock, "type");
            c.NotNull(item.ToolTip, "原版 ToolTip");
        });
    }
}
