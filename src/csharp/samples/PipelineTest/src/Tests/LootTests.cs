using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader;
using Terraria;
using Terraria.ID;

namespace PipelineTest.Tests;

/// <summary>
/// 管线：Loot（掉落）—— 注册面 + 功能面。
/// Ready：测试 NPC 的掉落表里确实有我们加的规则。
/// World：刷两只 1 血测试史莱姆，打死后应掉"测试物品"。
/// </summary>
internal static class LootTests
{
    public static void Register()
    {
        PT.Group("loot", Phase.Ready, RunReady);
        PT.Group("loot-w", Phase.World, RunWorld);
    }

    private static void RunReady()
    {
        var npcType = ModContent.NpcType<Content.TestNpc>();

        PT.Case("LOOT-R1", c =>
        {
            var rules = new NPCLoot(npcType).Get();
            c.True(rules is { Count: > 0 }, $"测试 NPC 掉落规则数={rules?.Count ?? 0}（应 > 0）");
        });
    }

    private static int _first;

    private static int first2() => _first;

    private static void RunWorld()
    {
        var npcType = ModContent.NpcType<Content.TestNpc>();
        var player = Main.player[Main.myPlayer];
        if (player is null || !player.active)
        {
            PT.SkipCase("LOOT-W1", "玩家还没就绪");
            return;
        }

        PT.Case("LOOT-W1", c =>
        {
            // 关键对照：进世界后规则还在不在（游戏可能在进图时重建 ItemDropsDB）。
            var rules = new NPCLoot(npcType).Get();
            PT.Info($"进世界后测试 NPC 掉落规则数={rules?.Count ?? -1}");
            c.True(rules is { Count: > 0 }, $"进世界后规则数={rules?.Count ?? -1}（应 > 0）");

            // 样例注册之后重建 type→netID 映射（mod NPC 的类型号要进这张表，掉落解析才找得到）。
            Main.ItemDropsDB.PrepareNPCNetIDsByTypeDictionary();
            PT.Info($"已重建 NPC 类型映射；当前规则数={new NPCLoot(npcType).Get()?.Count ?? -1}");

            var source = player.GetProjectileSource_Misc(0);
            var spawned = 0;
            for (var i = 0; i < 2; i++)
            {
                var idx = NPC.NewNPC(source, (int)player.Center.X + (i == 0 ? -80 : 80), (int)player.Center.Y - 40,
                    npcType, 0, 0f, 0f, 0f, 0f, 0);
                if (idx < 0) continue;
                if (spawned == 0) _first = idx;
                spawned++;
                var npc = Main.npc[idx];
                npc.life = 1;          // 一击必杀，方便看掉落
                npc.lifeMax = 1;
            }

            c.Eq(spawned, 2, "生成只数");
            if (spawned > 0) PT.Info($"测试史莱姆 netID={Main.npc[first2()].netID} type={Main.npc[first2()].type}");

            // 对照：给原版蓝史莱姆也加一条必掉规则，看是"规则不生效"还是"只有 mod NPC 不生效"。
            var blue = NPCID.BlueSlime;
            var blueRules = new NPCLoot(blue).Get();
            if (blueRules is { Count: 0 } || blueRules is null)
            {
                new NPCLoot(blue).Add(Terraria.GameContent.ItemDropRules.ItemDropRule.Common(
                    ModContent.TypeOf<Content.TestItem>(), 1, 1, 1));
            }
            var control = NPC.NewNPC(source, (int)player.Center.X, (int)player.Center.Y - 60, blue, 0, 0f, 0f, 0f, 0f, 0);
            if (control >= 0)
            {
                Main.npc[control].life = 1;
                Main.npc[control].lifeMax = 1;
            }

            PT.Info($"已刷 {spawned} 只 1 血测试史莱姆 + 1 只对照蓝史莱姆；打死应掉 测试物品");
        });
    }
}
