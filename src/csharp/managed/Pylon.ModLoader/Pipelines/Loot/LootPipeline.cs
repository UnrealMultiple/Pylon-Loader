using Pylon.ModLoader.Content;
using Pylon.ModLoader.Contracts;
using Pylon.ModLoader.Core;
using Pylon.ModLoader.Hooks;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Patching;
using Pylon.ModLoader.Pipeline;
using Terraria;

namespace Pylon.ModLoader.Pipelines;
/// <summary>掉落管线：全局掉落（对全部 NPC 生效的规则）、mod 物品的规则表，以及开 Boss 袋。</summary>
internal sealed class LootPipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static LootPipeline Instance { get; } = new();

    public string Name => "loot";

    private static IModLog Log => LoaderHost.Current.Log;

    /// <summary>loot 管线的安装门：条件与订阅同居一门，由 GateHub 自动发现与派发。</summary>
    internal static class Gates
    {
        internal static bool HasItems() => ModContent.GetContent<ModItem>().Count > 0;

        internal struct BossBag : IGate
        {
            public static bool Wanted() => HasItems() || GlobalItemRegistry.AnyWith(HookId.ModifyItemLoot);

            public static void Install()
            {
                On.Terraria.Player.OpenBossBag += OpenBossBag;

                Log.Info("[loot] 已挂：开 Boss 袋");
            }
        }

        internal struct RightClick : IGate
        {
            public static bool Wanted() =>
                HasItems() ||
                GlobalItemRegistry.AnyWith(HookId.CanRightClick, HookId.RightClick, HookId.ConsumeItem,
                    HookId.ModifyItemLoot);

            public static void Install()
            {
                On.Terraria.Player.ItemCheck_ManageRightClickFeatures += RightClickFeatures;

                Log.Info("[loot] 已挂：物品右键");
            }
        }

        internal struct Drop : IGate
        {
            public static bool Wanted() => HasItems();

            public static void Install()
            {
                On.Terraria.GameContent.ItemDropRules.CommonCode.DropItemFromNPC += DropModItem;

                Log.Info("[loot] 已挂：mod 物品掉落");
            }
        }

        internal struct Patch : IGate
        {
            public static bool Wanted() => ModContent.GetContent<ModNpc>().Count > 0;

            public static void Install() => PatchNpcLootLimit();
        }
    }

    public bool Install() => GateHub<LootPipeline>.Run();

    private static void DropModItem(On.Terraria.GameContent.ItemDropRules.CommonCode.orig_DropItemFromNPC orig,
                                    NPC npc, int itemId, int stack, bool scattered)
    {
        if (itemId >= VanillaCounts.Item)
        {
            try
            {
                Item.NewItem((int)npc.position.X, (int)npc.position.Y, npc.width, npc.height, itemId, stack);
                return;
            }
            catch (Exception e)
            {
                ContentErrors.Line("[loot] mod 物品掉落失败：" + Safe.Dump(e));
            }
        }

        orig(npc, itemId, stack, scattered);
    }

    private static void PatchNpcLootLimit()
    {
        var total = (uint)(VanillaCounts.Npc + ModContent.GetContent<ModNpc>().Count);
        var old = (uint)(VanillaCounts.Npc - 1);
        var updated = total - 1;

        try
        {
            var changed = 0;
            foreach (var below in new[] { 0, 1 })
            {
                var result = RuntimeLimitInstaller.PatchMethod(typeof(NPC), "NPCLoot", 0,
                    old, updated, below != 0, false, Log);
                if (result > 0) changed += result;
            }

            if (changed > 0) Log.Info($"[loot] NPC.NPCLoot 上限补丁：{old}→{updated}（{changed} 处）");
            else Log.Warn($"[loot] NPC.NPCLoot 上限补丁没生效（{old}→{updated}）");
        }
        catch (Exception e)
        {
            Log.Warn("[loot] NPC.NPCLoot 上限补丁异常：" + e.Message);
        }
    }

    private static void RightClickFeatures(On.Terraria.Player.orig_ItemCheck_ManageRightClickFeatures orig, Player self)
    {
        orig(self);

        if (self.HeldItem is not { } item || item.IsAir) return;
        if (Main.myPlayer != self.whoAmI) return;

        var modItem = ModContent.Find(ContentKind.Item, item.type) as ModItem;
        if (!ItemRightClicked(modItem, item)) return;

        RightClicked(modItem, item, self);

        if (ItemLoot.For(item).Count > 0) RollItemLoot(self, item);

        if (!ConsumeItem(modItem, item, self)) return;

        try
        {
            item.stack--;
            if (item.stack <= 0) item.TurnToAir();
        }
        catch (Exception e)
        {
            ContentErrors.Line("[loot] 扣物品异常：" + Safe.Dump(e));
        }
    }

    private static void RightClicked(ModItem? modItem, Item item, Player player)
    {
        if (modItem is not null)
        {
            try
            {
                modItem.RightClick(player);
            }
            catch (Exception e)
            {
                ContentErrors.Line($"[loot] {modItem.Name}.RightClick 异常：{Safe.Dump(e)}");
            }
        }

        foreach (var global in GlobalItemRegistry.For(item, HookId.RightClick))
        {
            try
            {
                global.RightClick(item, player);
            }
            catch (Exception e)
            {
                ContentErrors.Line($"[loot] {global.FullKey}.RightClick 异常：{Safe.Dump(e)}");
            }
        }
    }

    private static bool ConsumeItem(ModItem? modItem, Item item, Player player)
    {
        var consume = true;

        if (modItem is not null && modItem.Plan.Has(HookId.ConsumeItem))
        {
            try
            {
                consume &= modItem.ConsumeItem(player);
            }
            catch (Exception e)
            {
                ContentErrors.Line($"[loot] {modItem.Name}.ConsumeItem 异常：{Safe.Dump(e)}");
            }
        }

        foreach (var global in GlobalItemRegistry.For(item, HookId.ConsumeItem))
        {
            try
            {
                consume &= global.ConsumeItem(item, player);
            }
            catch (Exception e)
            {
                ContentErrors.Line($"[loot] {global.FullKey}.ConsumeItem 异常：{Safe.Dump(e)}");
            }
        }

        return consume;
    }

    private static bool ItemRightClicked(ModItem? modItem, Item item)
    {
        if (modItem is not null)
        {
            try
            {
                if (modItem.CanRightClick()) return true;
            }
            catch (Exception e)
            {
                ContentErrors.Line($"[loot] {modItem.Name}.CanRightClick 异常：{Safe.Dump(e)}");
            }
        }

        foreach (var global in GlobalItemRegistry.For(item, HookId.CanRightClick))
        {
            try
            {
                if (global.CanRightClick(item)) return true;
            }
            catch (Exception e)
            {
                ContentErrors.Line($"[loot] {global.FullKey}.CanRightClick 异常：{Safe.Dump(e)}");
            }
        }

        return false;
    }

    private static void OpenBossBag(On.Terraria.Player.orig_OpenBossBag orig, Player self, int type)
    {
        if (ModContent.Find(ContentKind.Item, type) is not ModItem)
        {
            orig(self, type);
            return;
        }

        var item = new Item();
        item.SetDefaults(type, null);
        RollItemLoot(self, item);
        TryDevArmor(self, type);
    }

    /// <summary>
    /// mod Boss 袋的开发者套件掷取：非「肉前袋」（ItemID.Sets.PreHardmodeLikeBossBag）才掷。
    /// 原版袋走 orig 自带这条链，只有 mod 袋被这边接管、需要补上。
    /// </summary>
    private static void TryDevArmor(Player self, int type)
    {
        if (!ItemTml.RollsDevArmor(type)) return;

        try
        {
            self.TryGettingDevArmor(self.GetItemSource_OpenItem(type));
        }
        catch (Exception e)
        {
            ContentErrors.Line($"[loot] 开发者套件掷取异常（type={type}）：{Safe.Dump(e)}");
        }
    }

    private static void RollItemLoot(Player player, Item item)
    {
        var rules = ItemLoot.For(item);
        if (rules.Count == 0) return;

        var info = new Terraria.GameContent.ItemDropRules.DropAttemptInfo
        {
            player = player,
            rng = Main.rand,
            IsInSimulation = false,
            IsExpertMode = Main.expertMode,
            IsMasterMode = Main.masterMode,
        };

        foreach (var rule in rules)
        {
            try
            {
                var result = rule.TryDroppingItem(info);

                var chains = rule.ChainedRules;
                if (chains is null) continue;

                foreach (var chain in chains)
                {
                    if (chain is null) continue;
                    try
                    {
                        if (!chain.CanChainIntoRule(result)) continue;
                        chain.RuleToChain?.TryDroppingItem(info);
                    }
                    catch (Exception e)
                    {
                        ContentErrors.Line($"[loot] 物品 {item.type} 开袋链异常：{Safe.Dump(e)}");
                    }
                }
            }
            catch (Exception e)
            {
                ContentErrors.Line($"[loot] 物品 {item.type} 开袋规则异常：{Safe.Dump(e)}");
            }
        }
    }

    public void Refresh()
    {
    }

    internal void Build()
    {
        LootTable.ApplyGlobals();
        LootTable.ApplyNpcGlobals();
        LootTable.PrepareItems();

        if (LootTable.Globals > 0 || LootTable.Added > 0)
            Log.Info($"[loot] 全局规则：{LootTable.Globals} 个入口，新增 {LootTable.Added} 条");

        if (LootTable.Npcs > 0) Log.Info($"[loot] 按实体全局掉落：{LootTable.Npcs} 种 NPC");

        if (LootTable.Items > 0) Log.Info($"[loot] 物品规则预建：{LootTable.Items} 个物品");
    }
}
