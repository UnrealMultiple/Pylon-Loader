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

    public override void SetDefaults()
    {
        NPC.width = 40;
        NPC.height = 40;
        NPC.lifeMax = 5000;
        NPC.life = 5000;
        NPC.damage = 0;
        NPC.defense = 0;
        NPC.boss = true;
        NPC.friendly = true;
        NPC.noGravity = true;
        NPC.noTileCollide = true;
        NPC.aiStyle = -1;
        NPC.HitSound = SoundID.NPCHit1;
        NPC.DeathSound = SoundID.NPCDeath1;
    }

    public override void OnSpawn(IEntitySource source) => NPC.timeLeft = 3600;

    public override bool CheckActive() => false;
}
