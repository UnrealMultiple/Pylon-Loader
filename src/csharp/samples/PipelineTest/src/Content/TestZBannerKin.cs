using Pylon.ModLoader;
using Terraria.ID;
using Pylon.ModLoader.Content;

namespace PipelineTest.Content;

/// <summary>同族 NPC：Banner 指向代表，BannerItem 留空——击杀应记到同一面旗子上。</summary>
public sealed class TestZBannerKin : ModNpc
{
    public override string DisplayName => "测试旗子史莱姆（同族）";

    public override int Banner => ModContent.NPCType<TestZBannerNpc>();

    public override void SetDefaults()
    {
        NPC.width = 40;
        NPC.height = 32;
        NPC.aiStyle = 1;
        NPC.lifeMax = 100;
        NPC.life = 100;
        NPC.damage = 0;
        NPC.defense = 0;
        NPC.friendly = false;
        NPC.knockBackResist = 0.6f;
        NPC.HitSound = SoundID.NPCHit1;
        NPC.DeathSound = SoundID.NPCDeath1;
    }
}
