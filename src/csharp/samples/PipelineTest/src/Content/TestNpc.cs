using Pylon.ModLoader;
using Terraria;
using Terraria.DataStructures;

namespace PipelineTest.Content;

/// <summary>测试 NPC：一只紫色史莱姆（aiStyle=1）。贴图 assets/npcs/TestNpc.png（40x36，单帧）。</summary>
public sealed class TestNpc : ModNpc
{
    public static int OnSpawnCount;

    public override string DisplayName => "测试史莱姆";

    public override void SetDefaults(NPC npc)
    {
        npc.width = 40;
        npc.height = 32;
        npc.aiStyle = 1;          // 原版史莱姆 AI（会朝玩家跳）
        npc.lifeMax = 120;
        npc.life = 120;
        npc.damage = 8;
        npc.defense = 0;
        npc.friendly = false;
        npc.knockBackResist = 0.6f;
        npc.value = 100f;

        // 命中/死亡音效：不写就是静音（和 XiheBoss 一样直接给 SoundID 常量）。
        npc.HitSound = Terraria.ID.SoundID.NPCHit1;
        npc.DeathSound = Terraria.ID.SoundID.NPCDeath1;
    }

    public override void OnSpawn(NPC npc, IEntitySource source) => OnSpawnCount++;

    public override void OnKill(NPC npc) => PT.Info($"测试史莱姆死亡（掉落应已生成）");

    /// <summary>Loot 管线：必掉 1~2 个测试物品（打死测试史莱姆就能看到）。</summary>
    public override void ModifyNPCLoot(NPCLoot npcLoot)
    {
        npcLoot.Add(Terraria.GameContent.ItemDropRules.ItemDropRule.Common(
            ModContent.TypeOf<TestItem>(), 1, 1, 2));

    }
}
