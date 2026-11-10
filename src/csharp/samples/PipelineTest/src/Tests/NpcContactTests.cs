using System.Collections.Generic;
using PipelineTest.Content;
using Terraria;

namespace PipelineTest.Tests;

internal static class NpcContactTests
{
    public static void Register() => PT.Group("npccontact-w", Phase.World, Run);

    private static void Run()
    {
        var slots = new List<int>();
        for (var i = 0; i < Main.npc.Length && slots.Count < 2; i++)
            if (Main.npc[i] is { active: false }) slots.Add(i);
        if (slots.Count != 2) { PT.SkipCase("CONTACT-H1", "需要两个空闲NPC槽位"); return; }
        var a = slots[0]; var b = slots[1];
        var savedA = Main.npc[a]; var savedB = Main.npc[b];
        var source = new NPC(); source.SetDefaults(Terraria.ID.NPCID.BlueSlime, new NPCSpawnParams());
        var victim = new NPC(); victim.SetDefaults(Terraria.ID.NPCID.Guide, new NPCSpawnParams());
        source.whoAmI = a; victim.whoAmI = b;
        source.active = victim.active = true;
        victim.life = victim.lifeMax = 100;
        source.damage = 20;
        Main.npc[a] = source; Main.npc[b] = victim;
        NpcContactProbe.Source = a; NpcContactProbe.Target = b;
        NpcContactProbe.Active = true;
        try
        {
            PT.Case("CONTACT-H1", c =>
            {
                NpcContactProbe.Veto = true;
                NpcContactProbe.Gates = 0;
                var before = victim.life;
                victim.BeHurtByOtherNPC(a, source);
                c.Eq(NpcContactProbe.Gates, 1, "真实NPC接触入口查询来源门控");
                c.Eq(victim.life, before, "否决后不执行原版伤害");
            });
        }
        finally
        {
            NpcContactProbe.Active = NpcContactProbe.Veto = false;
            NpcContactProbe.Source = NpcContactProbe.Target = -1;
            source.active = victim.active = false;
            Main.npc[a] = savedA; Main.npc[b] = savedB;
        }
    }
}
