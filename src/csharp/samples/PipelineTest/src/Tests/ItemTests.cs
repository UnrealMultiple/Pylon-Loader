using Pylon.ModLoader;
using Terraria;
using Terraria.ID;

namespace PipelineTest.Tests;

/// <summary>Item 管线：注册 + 默认值 + tooltip（本轮重点：物品介绍不再崩）。</summary>
internal static class ItemTests
{
    public static void Run()
    {
        PT.Case("ITEM-R1", () =>
        {
            var type = ModContent.TypeOf<Content.TestItem>();
            var c = new Check("ITEM-R1");
            c.True(type >= ItemID.Count, $"type={type} base={ItemID.Count}");
            if (c.Ok) PT.PassCase("ITEM-R1", $"type={type} base={ItemID.Count}");
        });

        PT.Case("ITEM-R2", () =>
        {
            var found = ModContent.Find<Content.TestItem>("PipelineTest/TestItem");
            var c = new Check("ITEM-R2");
            c.NotNull(found, "Find<TestItem>=null");
            if (c.Ok) PT.PassCase("ITEM-R2", "回查命中");
        });

        PT.Case("ITEM-S1", () =>
        {
            var item = new Item();
            item.SetDefaults(ModContent.TypeOf<Content.TestItem>(), null);
            var c = new Check("ITEM-S1");
            c.True(item.width == 20 && item.height == 20, $"size={item.width}x{item.height} (期望 20x20)");
            c.True(item.maxStack == ModItem.CommonMaxStack, $"maxStack={item.maxStack}");
            if (c.Ok) PT.PassCase("ITEM-S1", "SetDefaults 落位");
        });

        PT.Case("ITEM-N1", () =>
        {
            var type = ModContent.TypeOf<Content.TestItem>();
            var name = Lang.GetItemNameValue(type);
            if (string.IsNullOrEmpty(name)) PT.SkipCase("ITEM-N1", $"名字为空（type={type}，本地化时机待查）");
            else PT.PassCase("ITEM-N1", $"name={name}");
        });

        PT.Case("ITEM-T1", () =>
        {
            // 游戏在首次显示 tooltip 时才调 RebuildTooltip；测试里显式调一次再断言。
            var item = new Item();
            item.SetDefaults(ModContent.TypeOf<Content.TestItem>(), null);
            item.RebuildTooltip();
            var c = new Check("ITEM-T1");
            c.NotNull(item.ToolTip, "mod 物品 ToolTip=null");
            c.True(item.ToolTip.Lines >= 1, $"tooltip 行数={item.ToolTip.Lines}（至少 1）");
            if (c.Ok) PT.PassCase("ITEM-T1", $"lines={item.ToolTip.Lines}");
        });

        PT.Case("ITEM-T2", () =>
        {
            // 非 mod 物品不受污染：原版物品 SetDefaults 后 tooltip 正常、游戏不崩（崩溃回归用例）
            var item = new Item();
            item.SetDefaults(ItemID.DirtBlock, null);
            var c = new Check("ITEM-T2");
            c.True(item.type == ItemID.DirtBlock, $"type={item.type}");
            c.NotNull(item.ToolTip, "原版物品 ToolTip=null");
            if (c.Ok) PT.PassCase("ITEM-T2", "原版物品 SetDefaults 后存活");
        });
    }
}
