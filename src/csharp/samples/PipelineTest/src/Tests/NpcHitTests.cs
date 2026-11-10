using Il2CppMicrosoft.Xna.Framework;
using PipelineTest.Content;
using Terraria;

namespace PipelineTest.Tests;

internal static class NpcHitTests
{
    public static void Register() => PT.Group("npchit-w", Phase.World, Run);

    private static void Run()
    {
        var player = Main.LocalPlayer;
        var index = -1;
        for (var i = 0; i < Main.npc.Length; i++)
            if (Main.npc[i] is { active: false }) { index = i; break; }
        if (index < 0) { PT.SkipCase("NPCHIT-H1", "没有空闲NPC槽位"); return; }
        var previous = Main.npc[index];
        var npc = new NPC();
        npc.SetDefaults(Terraria.ID.NPCID.BlueSlime, new NPCSpawnParams());
        npc.whoAmI = index;
        npc.life = npc.lifeMax = 100;
        npc.defense = 10;
        npc.active = true;
        npc.position = player.position;
        Main.npc[index] = npc;
        NpcHitProbe.Active = true;
        NpcHitProbe.TargetIndex = index;
        NpcHitProbe.IncomingCalls = NpcHitProbe.ItemGateCalls = 0;
        try
        {
            PT.Case("NPCHIT-H1", c =>
            {
                var before = npc.life;
                var done = npc.StrikeNPC(40, 0f, 0, true, false, Main.myPlayer);
                c.Eq(NpcHitProbe.IncomingCalls, 1, "真实StrikeNPC入口调用目标修饰");
                c.True(done > 0 && done <= 7, $"暴击修饰后实际伤害上限 done={done}");
                c.Eq(before - npc.life, done, "伤害返回值与实际扣血一致");
            });
            PT.Case("NPCHIT-H2", c =>
            {
                var weapon = new Item();
                weapon.SetDefaults(Terraria.ID.ItemID.CopperShortsword, null!);
                var before = npc.life;
                player.ProcessHitAgainstNPC(weapon, new Rectangle((int)npc.position.X, (int)npc.position.Y, 32, 32), 40, 0f, index);
                c.Eq(NpcHitProbe.ItemGateCalls, 1, "真实物品命中入口查询GlobalItem");
                c.Eq(npc.life, before, "作者否决后不调用原版扣血");
            });
        }
        finally
        {
            NpcHitProbe.Active = false;
            NpcHitProbe.TargetIndex = -1;
            npc.active = false;
            Main.npc[index] = previous;
        }
    }
}
