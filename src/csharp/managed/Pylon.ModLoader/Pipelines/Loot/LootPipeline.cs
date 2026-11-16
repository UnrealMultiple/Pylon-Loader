using Terraria;
using Terraria.GameContent.ItemDropRules;

namespace Pylon.ModLoader;

/// <summary>掉落管线：全局掉落（对全部 NPC 生效的规则）、mod 物品的规则表，以及开 Boss 袋。</summary>
internal sealed class LootPipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static LootPipeline Instance { get; } = new();

    public string Name => "loot";

    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        On.Terraria.Player.OpenBossBag += OpenBossBag;
        On.Terraria.Player.ItemCheck_ManageRightClickFeatures += RightClickFeatures;
        On.Terraria.GameContent.ItemDropRules.CommonCode.DropItemFromNPC += DropModItem;

        PatchNpcLootLimit();

        Log.Info("[loot] 已挂（全局掉落 / 物品规则预建 / 开 Boss 袋 / 物品右键）");
        return true;
    }

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
                ContentErrors.Report("[loot] mod 物品掉落失败：" + Safe.Describe(e));
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
        if (ModContent.Find(ContentKind.Item, item.type) is not ModItem modItem) return;
        if (!ItemRightClicked(modItem, item, self)) return;

        try
        {
            modItem.RightClick(self);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[loot] {modItem.Name}.RightClick 异常：{Safe.Describe(e)}");
        }

        if (ItemLoot.For(modItem).Count > 0) RollItemLoot(self, modItem);

        if (!ConsumeItem(modItem, item, self)) return;

        try
        {
            item.stack--;
            if (item.stack <= 0) item.TurnToAir();
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[loot] {modItem.Name}.ConsumeItem 扣物品异常：{Safe.Describe(e)}");
        }
    }

    private static bool ConsumeItem(ModItem modItem, Item item, Player player)
    {
        var consume = true;

        if (modItem.Plan.Has(HookId.ConsumeItem))
        {
            try
            {
                consume &= modItem.ConsumeItem(player);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[loot] {modItem.Name}.ConsumeItem 异常：{Safe.Describe(e)}");
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
                ContentErrors.Report($"[loot] {global.FullKey}.ConsumeItem 异常：{Safe.Describe(e)}");
            }
        }

        return consume;
    }

    private static bool ItemRightClicked(ModItem modItem, Item item, Player player)
    {
        try
        {
            return modItem.CanRightClick();
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[loot] {modItem.Name}.CanRightClick 异常：{Safe.Describe(e)}");
            return false;
        }
    }

    private static void OpenBossBag(On.Terraria.Player.orig_OpenBossBag orig, Player self, int type)
    {
        if (ModContent.Find(ContentKind.Item, type) is not ModItem modItem)
        {
            orig(self, type);
            return;
        }

        RollItemLoot(self, modItem);
    }

    private static void RollItemLoot(Player player, ModItem modItem)
    {
        var rules = ItemLoot.For(modItem);
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
                        ContentErrors.Report($"[loot] {modItem.Name} 开袋链异常：{Safe.Describe(e)}");
                    }
                }
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[loot] {modItem.Name} 开袋规则异常：{Safe.Describe(e)}");
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
