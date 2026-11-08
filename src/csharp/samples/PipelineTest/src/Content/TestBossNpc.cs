using Pylon.ModLoader;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;

namespace PipelineTest.Content;

/// <summary>测试 Boss：声明 TestBossBar 的普通 NPC（不要求 npc.boss，管线按 ModNpc.BossBar 找）。</summary>
public sealed class TestBossNpc : ModNpc
{
    public override string DisplayName => "测试血条 Boss";

    public override ModBossBar BossBar => ModContent.GetInstance<TestBossBar>();

    public override void SetDefaults(NPC npc)
    {
        npc.width = 40;
        npc.height = 40;
        npc.lifeMax = 5000;
        npc.life = 5000;
        npc.damage = 0;
        npc.defense = 0;
        npc.boss = true;
        npc.friendly = true;
        npc.noGravity = true;
        npc.noTileCollide = true;
        npc.aiStyle = -1;
        npc.HitSound = SoundID.NPCHit1;
        npc.DeathSound = SoundID.NPCDeath1;
    }

    public override void OnSpawn(NPC npc, IEntitySource source) => npc.timeLeft = 3600;

    public override bool CheckActive(NPC npc) => false;
}
