using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;

namespace Pylon.ModLoader;

/// <summary>
/// 掉落侧表：跑全局规则、预建物品规则、记数。规则本身存在游戏自己的 <c>ItemDropsDB</c> 里，
/// 这里不复制一份（复制就会和游戏的表不一致）。
/// </summary>
internal static class LootTable
{
    private static int _globals;
    private static int _items;
    private static int _rulesBefore;
    private static int _npcs;

    /// <summary>跑过按实体掉落钩子的 NPC 种类数。</summary>
    internal static int Npcs => _npcs;

    /// <summary>跑过的全局规则入口数。</summary>
    internal static int Globals => _globals;

    /// <summary>预建过物品规则的物品数。</summary>
    internal static int Items => _items;

    /// <summary>全局规则让游戏那张表多了几条规则。</summary>
    internal static int Added => Math.Max(0, GlobalCount() - _rulesBefore);

    /// <summary>全局规则：对全部 NPC 生效，跑一次。<b>异常不吞</b>——作者写错要在安装期炸。</summary>
    internal static void ApplyGlobals()
    {
        var database = Main.ItemDropsDB;
        _rulesBefore = GlobalCount();

        _globals = 0;
        foreach (var global in ModContent.GetContent<GlobalNpc>())
        {
            global.ModifyGlobalLoot(database);
            _globals++;
        }
    }

    /// <summary>物品规则预建：让 <c>ItemLoot.For</c> 有东西可查。单个物品失败不断整轮。</summary>
    internal static void PrepareItems()
    {
        _items = 0;
        foreach (var item in ModContent.GetContent<ModItem>())
        {
            if (!item.Registered) continue;

            try
            {
                ItemLoot.For(item);
                _items++;
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[loot] {item.Name}.ModifyItemLoot 异常：{Safe.Describe(e)}");
            }
        }
    }

    /// <summary>
    /// 按实体跑的全局掉落（<c>GlobalNpc.ModifyNPCLoot</c>）：每种 NPC 一次，收的是那一类的样表。
    /// 必须在 ModNpc 自己的掉落表建完之后跑——全局规则是加在作者规则之上的。
    /// 样表来自 <c>ContentSamples.NpcsByNetId</c>：原版与 mod 的 NPC 都在里面，不用自己造实例。
    /// </summary>
    internal static void ApplyNpcGlobals()
    {
        var samples = ContentSamples.NpcsByNetId;
        if (samples is null) return;

        var database = Main.ItemDropsDB;
        _npcs = 0;

        foreach (var pair in samples)
        {
            var npc = pair.Value;
            if (npc is null) continue;

            var globals = GlobalNpcRegistry.For(npc, HookId.ModifyNPCLoot);
            for (var i = 0; i < globals.Count; i++)
            {
                var global = globals[i];

                try
                {
                    global.ModifyNPCLoot(npc, database);
                }
                catch (Exception e)
                {
                    ContentErrors.Report($"[loot] {global.Name}.ModifyNPCLoot 异常：{Safe.Describe(e)}");
                }
            }

            _npcs++;
        }
    }

    private static int GlobalCount() => new GlobalLoot().Get().Count;
}
