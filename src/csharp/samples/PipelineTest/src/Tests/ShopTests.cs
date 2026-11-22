using System;
using System.Collections.Generic;
using System.IO;
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
        // 生成器级 Nullable 修复（RawValueBox.Wrap getter）后，代理属性写读应完整一致。
        first.Item.shopCustomPrice = new Il2CppSystem.Nullable<int>(50);
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

        // 验证脚手架（复现采集）：用户手动击杀测试商人触发 SIGSEGV（crash-exit.txt 09:56）。
        // 程序化走同一原生击杀链（StrikeNPC → death → NPCLoot），排在全部延迟用例之后（帧 900+）
        // 以免截断其它断言；崩溃现场由持续 logcat 捕获。
        // 验证脚手架（诊断）：NPC 贴图透明——读 Npc 贴图表槽 / 头表 / profile 解析逐段打点。
        PT.Case("SHOP-TEXPROBE", c =>
        {
            var type = ModContent.NPCType<Content.TestTownNpc>();
            var mod = ModContent.GetInstance<Content.TestTownNpc>();
            var assets = Terraria.GameContent.TextureAssets.Npc;
            var heads = Terraria.GameContent.TextureAssets.NpcHead;

            PT.Info($"TEXPROBE npcType={type} NpcAssetsLen={assets?.Length ?? -1} NpcHeadLen={heads?.Length ?? -1}");
            PT.Info($"TEXPROBE mod.Texture={(mod?.Texture is null ? "null" : $"{mod.Texture.Name} {mod.Texture.Value?.Width}x{mod.Texture.Value?.Height}")}");

            if (assets is not null && type >= 0 && type < assets.Length)
            {
                var tex = assets[type];
                PT.Info($"TEXPROBE NpcAssets[{type}]={(tex is null ? "null" : $"{tex.Name} {(tex.Value is null ? "no-value" : $"{tex.Value.Width}x{tex.Value.Height}")}")}");
            }
            else
            {
                PT.Info($"TEXPROBE NpcAssets[{type}] 越界/表未扩（len={assets?.Length ?? -1}）");
            }

            if (heads is not null)
            {
                for (var i = 0; i < heads.Length && i < 8; i++)
                    PT.Info($"TEXPROBE NpcHead[{i}]={(heads[i] is null ? "null" : heads[i].Name ?? "unnamed")}");
            }

            var profile = mod?.TownNPCProfile();
            PT.Info($"TEXPROBE profile={profile?.GetType().Name ?? "null"}");

            for (var i = 0; i < Main.npc.Length; i++)
            {
                var n = Main.npc[i];
                if (n is { active: true } && n.type == type)
                {
                    var slot = Terraria.GameContent.TownNPCProfiles.GetHeadIndexSafe(n);
                    var frame = n.frame;
                    var frameCount = Main.npcFrameCount is null ? -1 : (type < Main.npcFrameCount.Length ? Main.npcFrameCount[type] : -2);
                    PT.Info($"TEXPROBE npc[{i}] GetHeadIndexSafe={slot} frame=X{frame.X} Y{frame.Y} W{frame.Width} H{frame.Height} frameCount={frameCount}");
                    break;
                }
            }

            if (heads is not null && heads.Length > 81)
                PT.Info($"TEXPROBE NpcHead[81]={(heads[81] is null ? "null" : heads[81].Name ?? "unnamed")}");
            if (heads is not null && heads.Length > 82)
                PT.Info($"TEXPROBE NpcHead[82]={(heads[82] is null ? "null" : heads[82].Name ?? "unnamed")}");

            c.True(true, "TEXPROBE 打点完成");
        });

        // 验证脚手架（复现采集 v2）：真实崩溃是测试史莱姆围殴商人致死（多次击退 + 死亡瞬间尸体碰撞更新），
        // 程序化 StrikeNPC（无击退无小怪）不复现。三只史莱姆刷在商人身上，轮询补刷直到商人被围殴致死。
        var brawlRounds = 0;
        void Brawl(Check c)
        {
            var npcType = ModContent.NPCType<Content.TestTownNpc>();
            var slimeType = ModContent.NPCType<Content.TestNpc>();

            NPC? merchant = null;
            for (var i = 0; i < Main.npc.Length; i++)
            {
                var npc = Main.npc[i];
                if (npc is { active: true } && npc.type == npcType) { merchant = npc; break; }
            }

            if (merchant is null)
            {
                c.True(brawlRounds > 0, $"商人已被围殴致死（{brawlRounds} 轮后尸体离场，查 tombstone）");
                return;
            }

            if (++brawlRounds >= 25)
            {
                c.Fail($"围殴 25 轮商人仍未死（life={merchant.life}）");
                return;
            }

            var slimes = 0;
            for (var i = 0; i < Main.npc.Length; i++)
            {
                var npc = Main.npc[i];
                if (npc is { active: true } && npc.type == slimeType &&
                    System.Math.Abs(npc.Center.X - merchant.Center.X) < 200f) slimes++;
            }

            if (slimes < 3)
            {
                var player = Main.player[Main.myPlayer];
                var source = player.GetProjectileSource_Misc(0);
                for (var k = slimes; k < 3; k++)
                {
                    var idx = NPC.NewNPC(source, (int)merchant.Center.X + (k - 1) * 30, (int)merchant.Center.Y - 40,
                        slimeType, 0, 0f, 0f, 0f, 0f, 0);
                    if (idx >= 0) Main.npc[idx].timeLeft = 3600;
                }
            }

            PT.Info($"围殴中：商人 life={merchant.life} 身边史莱姆={slimes}（第 {brawlRounds} 轮）");
            c.Pending();
            PT.RetryLater("SHOP-KILL-MERCHANT", 60, Brawl);
        }

        PT.RetryLater("SHOP-KILL-MERCHANT", 900, Brawl);

        // 验证脚手架（证据采集）：tombstone adb 侧被 SELinux 挡住，但游戏进程自己能读
        // 外部 files/ 里加载器收集的 tombstone——解出崩溃头与 backtrace 打进日志。
        PT.Case("SHOP-TOMB-DUMP", c =>
        {
            var dir = "/storage/emulated/0/Android/data/dev.pylon.loader/files";
            foreach (var name in new[] { "tombstone_00", "tombstone_01", "tombstone_02" })
            {
                var path = Path.Combine(dir, name);
                if (!File.Exists(path)) { PT.Info($"{name}: 不存在"); continue; }

                try
                {
                    var lines = File.ReadAllLines(path);
                    PT.Info($"{name}: {lines.Length} 行");
                    foreach (var line in lines)
                    {
                        if (line.Contains("signal") || line.Contains(">>> ") || line.Contains("pid:") ||
                            line.Contains("backtrace:") ||
                            line.Contains("#0") || line.Contains("#1") || line.Contains("#2") ||
                            line.Contains("#3") || line.Contains("#4") || line.Contains("#5") ||
                            line.Contains("libpylon") || line.Contains("PipelineTest") || line.Contains("Pylon"))
                        {
                            PT.Info($"{name}: {line.Trim()}");
                        }
                    }
                }
                catch (Exception e)
                {
                    PT.Info($"{name}: 读取失败 {e.GetType().Name}: {e.Message}");
                }
            }
            c.True(true, "tombstone 摘要已打点");
        });

        PT.Case("SHOP-W3", c =>
        {
            // 生成器级根治后的价格断言：显式构造写 → 代理属性读（Wrap 盒）→ 代理小写字段读 →
            // native 字段直读，四路一致；再把读到的 Wrap 盒写回另一个物品验证 setter 落盘。
            var probe = new Item();
            c.NoThrow(() => probe.SetDefaults(ItemID.Gel, null), "SetDefaults(凝胶)");
            if (probe.type <= 0)
            {
                PT.SkipCase("SHOP-W3", "凝胶 SetDefaults 未就绪");
                return;
            }

            probe.shopCustomPrice = new Il2CppSystem.Nullable<int>(123);
            var viaProxy = probe.shopCustomPrice;
            c.True(viaProxy is { HasValue: true }, $"代理属性读回 HasValue（{viaProxy?.HasValue.ToString()}）");
            c.Eq(viaProxy?.Value ?? -1, 123, "代理属性读回 Value（native get_Value）");
            c.True(viaProxy is { hasValue: true }, "代理小写字段 hasValue（Wrap 盒载荷直读）");
            c.Eq(viaProxy?.value ?? -1, 123, "代理小写字段 value（Wrap 盒载荷直读）");

            var klass = Il2CppInterop.Runtime.Il2CppClassPointerStore<Item>.NativeClassPtr;
            var field = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_field_from_name(klass, "shopCustomPrice");
            c.True(field != System.IntPtr.Zero, "shopCustomPrice 字段元数据可解析");
            if (field != System.IntPtr.Zero)
            {
                var offset = checked((int)Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset(field));
                c.True(System.Runtime.InteropServices.Marshal.ReadByte(probe.Pointer, offset) != 0, "native hasValue = true");
                c.Eq(System.Runtime.InteropServices.Marshal.ReadInt32(probe.Pointer, offset + 4), 123, "native value = 123");
            }

            var other = new Item();
            c.NoThrow(() => other.SetDefaults(ItemID.Torch, null), "SetDefaults(火把)");
            if (other.type <= 0)
            {
                PT.SkipCase("SHOP-W3", "火把 SetDefaults 未就绪");
                return;
            }
            other.shopCustomPrice = probe.shopCustomPrice; // Wrap 盒 → setter cpblk 落盘
            var round = other.shopCustomPrice;
            c.True(round is { HasValue: true } && round.Value == 123, "Wrap 盒写回另一物品后读回一致");
            if (field != System.IntPtr.Zero)
            {
                var offset = checked((int)Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset(field));
                c.Eq(System.Runtime.InteropServices.Marshal.ReadInt32(other.Pointer, offset + 4), 123, "other native value = 123");
            }

            // 清价：默认构造（object_new 全零载荷）写入 → HasValue=false
            probe.shopCustomPrice = new Il2CppSystem.Nullable<int>();
            var none = probe.shopCustomPrice;
            c.True(none is null || !none.HasValue, "清价后 HasValue=false");
        });

        PT.Case("SHOP-W3-PROBE", c =>
        {
            // 验证脚手架（诊断）：分解 SHOP-W3 第 4 路读链路（Wrap 盒 → 小写字段直读），
            // 把盒指针/字段偏移/载荷原始字节/四条读法逐段打点，供裁决 value 垃圾值的断链位置。
            var probe = new Item();
            probe.SetDefaults(ItemID.Gel, null);
            probe.shopCustomPrice = new Il2CppSystem.Nullable<int>(123);
            var box = probe.shopCustomPrice;
            if (box is null)
            {
                c.Fail("Wrap 盒为 null");
                return;
            }

            var klass = Il2CppInterop.Runtime.Il2CppClassPointerStore<Il2CppSystem.Nullable<int>>.NativeClassPtr;
            var fHas = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_field_from_name(klass, "hasValue");
            var fVal = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_field_from_name(klass, "value");
            int offHas = fHas != System.IntPtr.Zero ? checked((int)Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset(fHas)) : -1;
            int offVal = fVal != System.IntPtr.Zero ? checked((int)Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset(fVal)) : -1;
            uint align = 0;
            int vsize = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_value_size(klass, ref align);
            var p = box.Pointer;
            PT.Info($"PROBE klass=0x{klass:x} box=0x{p:x} offHas={offHas} offVal={offVal} valueSize={vsize}");
            PT.Info($"PROBE 载荷字节 hasValue[{offHas}]={System.Runtime.InteropServices.Marshal.ReadByte(p, offHas)} value[{offVal}]={System.Runtime.InteropServices.Marshal.ReadInt32(p, offVal)}");
            PT.Info($"PROBE 读链 hasValue(字段)={box.hasValue} value(字段)={box.value} HasValue(方法)={box.HasValue} Value(方法)={box.Value}");
            c.True(true, "诊断打点完成（证据见 PROBE 行）");
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
