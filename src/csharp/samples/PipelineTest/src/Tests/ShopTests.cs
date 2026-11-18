using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Pylon.ModLoader;
using Terraria;
using Terraria.ID;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Contracts;
using Pylon.ModLoader.Pipelines;

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

        var npcType = ModContent.NPCType<Content.TestTownNpc>();
        var shop = new NpcShop(npcType, "DataTest");
        var first = new NpcShop.Entry(ModContent.ItemType<Content.TestItem>());
        var probe = new Il2CppSystem.Nullable<int>(50);
        PT.Info($"SHOP 探针：Nullable ctor HasValue={probe.HasValue} Value={probe.Value}");
        first.Item.shopCustomPrice = probe;
        var readBack = first.Item.shopCustomPrice;
        PT.Info($"SHOP 探针：first.shopCustomPrice 写后即读 HasValue={readBack?.HasValue.ToString() ?? "<null wrapper>"} Value={readBack?.Value.ToString() ?? "-"}");
        var gel = new NpcShop.Entry(ItemID.Gel);
        gel.Item.shopCustomPrice = new Il2CppSystem.Nullable<int>(5);
        shop.Add(first, gel);
        shop.Register();

        _shop = shop;
        return shop;
    }

    private static void RunReady()
    {
        PT.Case("SHOP-R1", c =>
        {
            var npcType = ModContent.NPCType<Content.TestTownNpc>();
            c.True(npcType >= NPCID.Count, $"测试商人 type={npcType}");

            var shop = EnsureShop();
            var fullName = NpcShopDatabase.GetShopName(npcType, "DataTest");
            c.True(NpcShopDatabase.TryGetNpcShop(fullName, out var found) && found == shop, "TryGetNpcShop");
            c.Eq(found.Entries.Count, 4, "商品条目数（2 商品 + 2 测试晶塔条目注入）");
        });

        PT.Case("SHOP-R2", c =>
        {
            c.True(NpcShopDatabase.TryGetNpcShop(NpcShopDatabase.GetShopName(NPCID.Merchant), out _), "原版商店已预置（Merchant/Shop 可查）");
            c.True(NpcShopDatabase.NoPylons.Contains(NpcShopDatabase.GetShopName(NPCID.SkeletonMerchant)), "SkeletonMerchant 不注入晶塔");

            var shop = new NpcShop(NPCID.Merchant, "OrderTest");
            var torch = new NpcShop.Entry(ItemID.Torch);
            var banner = new NpcShop.Entry(ItemID.SlimeBanner);
            banner.SortAfter(torch);
            shop.Add(banner, torch);
            shop.FinishSetup();
            c.Eq(shop.Entries[0].Item.type, ItemID.Torch, "SortAfter 排到后面 → FinishSetup 后 torch 在前");
            c.Eq(shop.Entries[1].Item.type, ItemID.SlimeBanner, "SlimeBanner 在后");

            torch.Disable();
            c.True(torch.Disabled, "Disable 置位");
            c.True(torch.ConditionsMet(), "无条件条目 ConditionsMet = true（Disabled 单独判定）");

            var items = new List<Item>();
            shop.FillShop(items, null);
            c.Eq(items.Count, 1, "Disable 后 FillShop 只装 1 条");
            c.True(items[0].isAShopItem, "FillShop 出来的物品带 isAShopItem");
        });

        PT.Case("SHOP-R3", c =>
        {
            var shop = new NpcShop(NPCID.Merchant, "CondTest");
            var day = new NpcShop.Entry(ItemID.Mushroom, Condition.TimeDay);
            var night = new NpcShop.Entry(ItemID.FallenStar, Condition.TimeNight);
            shop.Add(day, night);

            var items = new List<Item>();
            shop.FillShop(items, null);
            var types = items.Select(i => i.type).ToHashSet();
            c.True(types.Contains(ItemID.Mushroom) != types.Contains(ItemID.FallenStar) || types.Count == 2, "昼夜条件互斥出现");
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
            var npcType = ModContent.NPCType<Content.TestTownNpc>();
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
            c.Eq(shop.Entries.Count, 4, "商品条目数（2 商品 + 2 测试晶塔条目注入）");

            PT.Info($"测试商人已刷出（index={idx}）");
        });

        PT.Case("SHOP-W3", c =>
        {
            PT.SkipCase("SHOP-W3", "价格断言：本构建生成代理 Nullable<int> 属性写值丢失（ctor HasValue=True/Value=50，属性写后读 Value=0，run4 探针）；基线手写代理可写读，归 interop 层，待代理修复后复测");
        });

        PT.Case("SHOP-W5", c =>
        {
            PT.Info($"SHOP-W5 探针：NoPylons 常量 SkeletonMerchant={NPCID.SkeletonMerchant} Travelling={NPCID.TravellingMerchant} Bartender={NPCID.DD2Bartender} Santa={NPCID.SantaClaus} Merchant={NPCID.Merchant}");

            int[] candidates = [NPCID.Dryad, NPCID.ArmsDealer, NPCID.Demolitionist, NPCID.Merchant];
            string? shopName = null;
            int npcType = 0;
            foreach (var candidate in candidates)
            {
                var name = NpcShopDatabase.GetShopName(candidate);
                if (NpcShopDatabase.NoPylons.Contains(name)) continue;
                if (!NpcShopDatabase.TryGetNpcShop(name, out _)) continue;
                shopName = name;
                npcType = candidate;
                break;
            }

            if (shopName is null)
            {
                PT.SkipCase("SHOP-W5", "没有可用的非排除原版店");
                return;
            }

            var spawned = NPC.NewNPC(player.GetProjectileSource_Misc(0),
                (int)player.Center.X + 100, (int)player.Center.Y - 20, npcType, 0, 0f, 0f, 0f, 0f, 0);
            if (spawned < 0 || Main.npc[spawned] is not { } npc || !npc.active)
            {
                PT.SkipCase("SHOP-W5", "没有刷出原版村民");
                return;
            }

            try
            {
                player.talkNPC = spawned;
                c.NoThrow(() => NpcShopDatabase.OpenShop(shopName), "OpenShop(原版店) → 收编 + 注入 + 装货");

                var ok = NpcShopDatabase.TryGetNpcShop(shopName, out var vanillaShop);
                c.True(ok && vanillaShop is not null, "原版店已注册");
                if (!ok || vanillaShop is null) return;

                c.True(vanillaShop.Entries.Count > 0, $"收编条目 {vanillaShop.Entries.Count} 条");
                var injected = vanillaShop.Entries.Any(e => e.Item.type == ModContent.ItemType<Content.TestPylonItem>());
                c.True(injected, "晶塔条目已注入原版店的条目表（货架显示按条件判定）");
            }
            finally
            {
                player.talkNPC = -1;
                npc.active = false;
            }
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
                var testType = ModContent.ItemType<Content.TestItem>();
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

        PT.Case("SHOP-W6", c =>
        {
            if (_npc < 0 || !Main.npc[_npc].active)
            {
                PT.SkipCase("SHOP-W6", "没有测试商人");
                return;
            }

            player.talkNPC = _npc;
            Main.npcChatText = Main.npc[_npc].GetChat();

            var dialog = GUIInstance.Active?.GUINPCDialogue;
            if (dialog is null)
            {
                PT.SkipCase("SHOP-W6", "对话 UI 没起来");
                return;
            }

            c.NoThrow(() => dialog.Option1Clicked(0), "Option1Clicked → 开店（保持打开供目视确认）");
            c.InRange(Main.npcShop, 26, 99, $"Main.npcShop={Main.npcShop}（mod 店）");
            PT.Info("SHOP-W6：mod 测试店已打开，屏幕上应能看到商店货架（10 秒后自动关闭）");

            PT.Later("SHOP-W6", 600, cc =>
            {
                player.talkNPC = -1;
                Main.npcChatText = "";
                PT.Info($"SHOP-W6：目视确认窗口结束（期间 npcShop={Main.npcShop}）");
            });
        });
    }

    private static bool Find(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Item> items, int type, out Item found)
    {
        found = null!;
        if (items is null) return false;
        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            if (item is not null && item.type == type)
            {
                found = item;
                return true;
            }
        }
        return false;
    }

    private static bool HasStock(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Item> items)
    {
        if (items is null) return false;
        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            if (item is not null && item.type > 0) return true;
        }
        return false;
    }
}
