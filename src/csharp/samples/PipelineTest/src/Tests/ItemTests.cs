using Pylon.ModLoader;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;

namespace PipelineTest.Tests;

/// <summary>
/// 管线 17：Item —— 注册面（功能版在 ItemTests2.cs：使用链/绘制/世界）。
/// 断言 SetDefaults 真的落位、tooltip 链真的工作、原版物品不受污染。
/// </summary>
internal static class ItemTests
{
    public static void Register()
    {
        PT.Group("item", Phase.Ready, Run);
        PT.Group("item-w", Phase.World, RunWorld);
    }

    private static void Run()
    {
        var type = ModContent.ItemType<Content.TestItem>();

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

        PT.Case("ITEM-SORT-SAMPLE", c =>
        {
            // ItemSorting comparers resolve OriginalRarity through this dictionary.
            var samples = ContentSamples.ItemsByType;
            c.NotNull(samples, "ContentSamples.ItemsByType");
            c.True(samples is not null && samples.ContainsKey(type),
                $"custom item sample exists for type {type}");
            if (samples is not null && samples.TryGetValue(type, out var sample))
            {
                c.NotNull(sample, "custom item sample value");
                c.Eq(sample.type, type, "custom item sample type");
                c.Eq(sample.damage, 15, "sample SetDefaults damage");
                c.Eq(sample.useTime, 20, "sample SetDefaults useTime");
            }
        });

        PT.Case("ITEM-SORT-SAMPLE-REENTRY", c =>
        {
            // The sorting hook may run from both SetupWhiteLists and the install
            // plan. Rebuilding a probe must remain stable and keep the dictionary
            // entry usable by OriginalRarity-based comparers.
            var samples = ContentSamples.ItemsByType;
            var probe = new Item();
            probe.SetDefaults(type, null);
            c.NotNull(samples, "ContentSamples.ItemsByType after repeated classification");
            if (samples is not null && samples.TryGetValue(type, out var sample))
            {
                c.NotNull(sample, "sample survives repeated classification");
                c.Eq(sample.type, probe.type, "sample type stable");
                c.Eq(sample.damage, probe.damage, "sample defaults stable");
            }
        });

        PT.Case("ITEM-API-1", c =>
        {
            var created = Item.Create(type, 3);
            c.Eq(created.type, type, "Item.Create type");
            c.Eq(created.stack, 3, "Item.Create stack");
            c.NotNull(created.ModItem, "Item.ModItem");
            c.Eq(created.ModItem!.LocalizationCategory, "Items", "LocalizationCategory");

            var clone = new Item();
            clone.SetDefaults(type, null);
            clone.CloneDefaults(ItemID.DirtBlock);
            c.Eq(clone.type, type, "CloneDefaults 保留 type");
            c.Eq(clone.ModItem?.GetType(), typeof(Content.TestItem), "CloneDefaults 保留绑定");

            var wall = Item.Create(0);
            wall.DefaultToPlaceableWall(ModContent.WallType<Content.TestWall>());
            c.Eq(wall.createWall, ModContent.WallType<Content.TestWall>(), "DefaultToPlaceableWall");

            var music = Item.Create(0);
            music.DefaultToMusicBox(TileID.MusicBoxes, 2);
            c.Eq(music.createTile, TileID.MusicBoxes, "DefaultToMusicBox tile");
            c.Eq(music.placeStyle, 2, "DefaultToMusicBox style");
        });

        PT.Case("ITEM-API-2", c =>
        {
            var item = Item.Create(type);
            c.True(item.EntityGlobals.Count > 0, "EntityGlobals");
            c.NotNull(item.GetGlobalItem<Content.TestItemApiProbe>(), "GetGlobalItem<T>");
            c.True(item.TryGetGlobalItem<Content.TestItemApiProbe>(out var probe) && probe is not null,
                "TryGetGlobalItem");
            c.Eq(item.GetGlobalItem<Content.TestItemApiProbe>(probe!).GetType(),
                typeof(Content.TestItemApiProbe), "GetGlobalItem(base)");
        });

        PT.Case("ITEM-API-3", c =>
        {
            var item = Item.Create(type);
            var rules = ItemLoot.For(item);
            c.True(rules.Count > 0, $"ItemLoot.For 规则数={rules.Count}");
            c.True(Content.TestItemApiProbe.LootCalls > 0,
                $"GlobalItem.ModifyItemLoot 已调（{Content.TestItemApiProbe.LootCalls} 次）");
        });

        PT.Case("ITEM-API-4", c =>
        {
            var item = Item.Create(type);
            item.ResearchUnlockCount = 50;
            c.Eq(item.ResearchUnlockCount, 50, "ResearchUnlockCount 写回");
            item.ResearchUnlockCount = 0;
            c.Eq(item.ResearchUnlockCount, 0, "ResearchUnlockCount 清零");
        });

        PT.Case("ITEM-API-5", c =>
        {
            var player = new Player();
            var npc = new NPC();
            npc.SetDefaults(NPCID.BlueSlime, new NPCSpawnParams());
            var projectile = new Projectile();
            projectile.SetDefaults(ProjectileID.WoodenArrowFriendly);

            c.NotNull(player.GetSource_FromThis(), "Player.GetSource_FromThis");
            c.NotNull(npc.GetSource_FromThis(), "NPC.GetSource_FromThis");
            c.NotNull(projectile.GetSource_FromThis(), "Projectile.GetSource_FromThis");
            c.NotNull(EntitySource_TileBreak.Create(1, 2), "EntitySource_TileBreak.Create");

            var frame = SpriteFrame.Create(2, 3, 1, 1);
            c.Eq((int)frame.ColumnCount, 2, "SpriteFrame columns");
            c.Eq((int)frame.RowCount, 3, "SpriteFrame rows");
            c.Eq((int)frame.CurrentColumn, 1, "SpriteFrame current column");
            c.Eq(frame.PaddingX, 2, "SpriteFrame padding");
        });
    }

    private static void RunWorld()
    {
        PT.Case("ITEM-W1", c =>
        {
            var player = Main.player[Main.myPlayer];
            if (player is null || !player.active)
            {
                PT.SkipCase("ITEM-W1", "玩家还没就绪");
                return;
            }

            var slot = player.selectedItem;
            if (slot < 0 || slot >= player.inventory.Length)
            {
                PT.SkipCase("ITEM-W1", "选中槽越界");
                return;
            }

            var saved = player.inventory[slot];
            var item = Item.Create(ModContent.ItemType<Content.TestItem>());
            player.inventory[slot] = item;

            var oldRight = Main.mouseRight;
            var oldRelease = Main.mouseRightRelease;
            var before = Content.TestItemApiProbe.CanRightClickCalls;
            Main.mouseRight = true;
            Main.mouseRightRelease = true;
            try
            {
                player.ItemCheck_ManageRightClickFeatures();
            }
            finally
            {
                Main.mouseRight = oldRight;
                Main.mouseRightRelease = oldRelease;
                player.inventory[slot] = saved;
            }

            c.True(Content.TestItemApiProbe.CanRightClickCalls > before,
                $"GlobalItem.CanRightClick 分派（{Content.TestItemApiProbe.CanRightClickCalls - before} 次）");
        });
    }
}
