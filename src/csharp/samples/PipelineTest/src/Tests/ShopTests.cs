using System.Reflection;
using Pylon.ModLoader;
using Terraria;
using Terraria.ID;

namespace PipelineTest.Tests;

/// <summary>
/// 管线：Shop（商店）+ 城镇 NPC 对话入口（Npc 管线）。
/// 直接驱动对话对象，不依赖触摸输入。
/// 另外单独走一次 <c>Player.SetTalkNPC</c>——真机点击就是这条路径
/// （Main.HoverOverNPCs → Player.SetTalkNPC → ShopHelper.GetShoppingSettings），
/// 之前会在 GetShoppingSettings 里抛 KeyNotFoundException 698。
/// </summary>
internal static class ShopTests
{
    private static NpcShop? _shop;
    private static int _npc = -1;

    public static void Register()
    {
        PT.Group("shop", Phase.Ready, RunReady);
        PT.Group("shop-w", Phase.World, RunWorld);
    }

    private static NpcShop EnsureShop()
    {
        if (_shop is not null) return _shop;

        var npcType = ModContent.NpcType<Content.TestTownNpc>();
        var shop = new NpcShop(npcType, "DataTest");
        shop.Add(ModContent.TypeOf<Content.TestItem>(), null, 50);
        shop.Add(ItemID.Gel, null, 5);
        shop.Register();

        _shop = shop;
        return shop;
    }

    private static void RunReady()
    {
        PT.Case("SHOP-R1", c =>
        {
            var npcType = ModContent.NpcType<Content.TestTownNpc>();
            c.True(npcType >= NPCID.Count, $"测试商人 type={npcType}");

            var shop = EnsureShop();
            c.InRange(shop.Index, 0, 99, "shop.Index");
            c.Eq(shop.Entries.Count, 2, "商品条目数");
        });
    }

    private static void RunWorld()
    {
        var player = Main.player[Main.myPlayer];
        if (player is null || !player.active)
        {
            PT.SkipCase("SHOP-W1", "玩家还没就绪");
            return;
        }

        PT.Case("SHOP-W1", c =>
        {
            var npcType = ModContent.NpcType<Content.TestTownNpc>();
            var idx = NPC.NewNPC(player.GetProjectileSource_Misc(0),
                (int)player.Center.X + 60, (int)player.Center.Y - 20, npcType, 0, 0f, 0f, 0f, 0f, 0);
            c.True(idx >= 0, $"测试商人 NewNPC 返回 {idx}");

            if (idx >= 0)
            {
                var npc = Main.npc[idx];
                c.True(npc.active, "商人 active");
                c.True(npc.townNPC, "townNPC");
                npc.GivenName = "小明";
                _npc = idx;
            }

            var shop = EnsureShop();
            c.Eq(shop.Entries.Count, 2, "商品条目数");
            c.Eq(shop.Entries[0].Price ?? -1, 50, "测试物品价格");

            PT.Info($"测试商人已刷出（index={idx}）");
        });

        PT.Case("SHOP-W2", c =>
        {
            if (_npc < 0)
            {
                c.Fail("没有测试商人");
                return;
            }

            var npc = Main.npc[_npc];
            c.True(npc.active && npc.townNPC, "商人可对话");

            // get_CanTalk 被强制 true（原版对 type > 696 返回 false）。
            c.True(npc.CanTalk, "CanTalk（get_CanTalk 钩子）");

            // 真机点击路径。修好后不应抛 KeyNotFoundException(698)。
            c.NoThrow(() => player.SetTalkNPC(_npc), "Player.SetTalkNPC(mod NPC)");
            c.Eq(player.talkNPC, _npc, "player.talkNPC");

            // 头像：mod 类型不该有 NPCPortraits 条目（以前拿向导的 provider 顶替，对话里显示向导头像）。
            // 原版 SetTalkNPC 先把 npcChatPortrait 清空、再 TryGetValue；查不到就维持空，画 NPC 本体。
            var portraits = NPCID.Sets.NPCPortraits;
            c.True(portraits is null || !portraits.ContainsKey(npc.type), "mod NPC 不占用原版头像槽");
            c.True(Main.npcChatPortrait is null, "对话头像为空（画 NPC 本体，不是向导）");

            // HoverOverNPCs 点开后会把 GetChat 填进 npcChatText。
            Main.npcChatText = npc.GetChat();
            c.Eq(Main.npcChatText, "我是测试商人，点下面的按钮看看商店。", "聊天文本");

            var dialog = GUIInstance.Active?.GUINPCDialogue;
            c.NotNull(dialog, "GUIInstance.Active.GUINPCDialogue");
            if (dialog is null) return;

            // SetupButtonText 在本版是 private，用反射驱动（钩子照常分派）。
            var method = typeof(GUINPCDialogue).GetMethod("SetupButtonText",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            c.NotNull(method, "GUINPCDialogue.SetupButtonText");
            if (method is not null)
            {
                var args = new object?[] { "", null, "", null, 0, false };
                c.NoThrow(() => method.Invoke(dialog, args), "SetupButtonText");
                c.Eq(args[0] as string, "商店", "第一个按钮文字");
            }

            // 点「商店」：NpcTown 的 Option1Clicked 钩子 → OnChatButtonClicked → NpcShop.Open。
            c.NoThrow(() => dialog.Option1Clicked(0), "Option1Clicked");
            var opened = Main.npcShop;
            c.InRange(opened, 0, 99, "Main.npcShop");

            var storage = Main.instance.shop[opened];
            c.NotNull(storage, "打开的商店");
            if (storage?.item is { } items)
            {
                var testType = ModContent.TypeOf<Content.TestItem>();
                var foundTest = false;
                var foundGel = false;
                for (var i = 0; i < items.Length; i++)
                {
                    var item = items[i];
                    if (item is null || item.IsAir) continue;
                    if (item.type == testType) foundTest = true;
                    if (item.type == ItemID.Gel) foundGel = true;
                }

                c.True(foundTest, "商店里有测试物品");
                c.True(foundGel, "商店里有凝胶");
            }

            player.talkNPC = -1;
            Main.npcChatText = "";
        });
    }
}
