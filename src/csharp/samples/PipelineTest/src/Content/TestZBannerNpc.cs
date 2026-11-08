using Pylon.ModLoader;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;

namespace PipelineTest.Content;

/// <summary>
/// 测试旗子 NPC（代表）：自己就是旗子号；KillsToBanner 改成 3，验证管线读物品表而不是写死 50。
/// 贴图 assets/npcs/TestZBannerNpc.png（40x36）。
/// </summary>
public sealed class TestZBannerNpc : ModNpc
{
    public override string DisplayName => "测试旗子史莱姆";

    public override int Banner => Type;

    public override int BannerItem => ModContent.ItemType<TestZBannerItem>();

    public override void SetStaticDefaults()
    {
        // 约定：新物品默认 BannerStrength=Reduced、KillsToBanner=50，作者可以改。
        // 这里改成 3，测试一次性把"读作者值"也覆盖到。
        ItemID.Sets.KillsToBanner[ModContent.ItemType<TestZBannerItem>()] = 3;
    }

    public override void SetDefaults(NPC npc)
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

    public override void OnSpawn(NPC npc, IEntitySource source) => PT.Info("测试旗子史莱姆已刷出");
}
