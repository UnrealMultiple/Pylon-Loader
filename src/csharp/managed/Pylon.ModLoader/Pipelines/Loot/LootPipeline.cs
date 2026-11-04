using Terraria;
using Terraria.GameContent.ItemDropRules;

namespace Pylon.ModLoader;

/// <summary>
/// 掉落管线：全局掉落（对全部 NPC 生效的规则）、mod 物品的规则表，以及开 Boss 袋。
/// </summary>
internal sealed class LootPipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static LootPipeline Instance { get; } = new();

    public string Name => "loot";

    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        On.Terraria.Player.OpenBossBag += OpenBossBag;
        On.Terraria.Player.ItemCheck_ManageRightClickFeatures += RightClickFeatures;

        Log.Info("[loot] 已挂（全局掉落 / 物品规则预建 / 开 Boss 袋 / 物品右键）");
        return true;
    }

    /// <summary>右键：原版不认的 mod 物品（袋、箱子、召唤物以外的自定义用法）派给作者。</summary>
    private static void RightClickFeatures(On.Terraria.Player.orig_ItemCheck_ManageRightClickFeatures orig, Player self)
    {
        orig(self);

        if (self.HeldItem is not { } item || item.IsAir) return;
        if (Main.myPlayer != self.whoAmI) return;
        if (ModContent.Find(ContentKind.Item, item.type) is not ModItem modItem) return;
        if (!ItemRightClicked(modItem, item, self)) return;

        try
        {
            modItem.RightClick(item, self);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[loot] {modItem.Name}.RightClick 异常：{Safe.Describe(e)}");
        }

        if (ItemLoot.For(modItem).Count > 0) RollItemLoot(self, modItem);
    }

    private static bool ItemRightClicked(ModItem modItem, Item item, Player player)
    {
        try
        {
            return modItem.CanRightClick(item);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[loot] {modItem.Name}.CanRightClick 异常：{Safe.Describe(e)}");
            return false;
        }
    }

    /// <summary>开 Boss 袋：mod 物品就轮它自己的 ItemLoot 规则表，原版袋走原版。</summary>
    private static void OpenBossBag(On.Terraria.Player.orig_OpenBossBag orig, Player self, int type)
    {
        if (ModContent.Find(ContentKind.Item, type) is not ModItem modItem)
        {
            orig(self, type);
            return;
        }

        RollItemLoot(self, modItem);
    }

    /// <summary>把一个物品的规则表轮一遍，掉出来的东西丢到玩家脚下。</summary>
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

                // 失败链：链对象自己看父规则的结果决定要不要接（游戏自己的判定）。
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
        // 掉落表不按类型发号，没有要在注册后重建的东西：规则直接进游戏的 ItemDropsDB。
    }

    /// <summary>安装期（Mods 阶段，所有内容装完之后）：全局规则 + 物品规则预建。</summary>
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
