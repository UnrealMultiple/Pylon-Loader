using Pylon.ModLoader;
using Terraria.ID;

namespace PipelineTest.Content;

/// <summary>同族 NPC：Banner 指向代表，BannerItem 留空——击杀应记到同一面旗子上。</summary>
public sealed class TestZBannerKin : ModNpc
{
    public override string DisplayName => "测试旗子史莱姆（同族）";

    public override int Banner => ModContent.NpcType<TestZBannerNpc>();

    public override void SetDefaults(Terraria.NPC npc)
    {
        npc.width = 40;
        npc.height = 32;
        npc.aiStyle = 1;
        npc.lifeMax = 100;
        npc.life = 100;
        npc.damage = 0;
        npc.defense = 0;
        npc.friendly = false;
        npc.knockBackResist = 0.6f;
        npc.HitSound = SoundID.NPCHit1;
        npc.DeathSound = SoundID.NPCDeath1;
    }
}
