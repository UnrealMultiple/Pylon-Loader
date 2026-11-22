using Pylon.ModLoader;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Pylon.ModLoader.Content;

namespace PipelineTest.Content;

/// <summary>
/// 测试旗子 NPC（代表）：自己就是旗子号；作者显式声明 BannerStrength=Reduced、KillsToBanner=3
/// （加载器不代补默认值，想要减伤与领取节奏必须自己声明），验证管线读的是声明的值。
/// 贴图 assets/npcs/TestZBannerNpc.png（40x36）。
/// </summary>
public sealed class TestZBannerNpc : ModNpc
{
    public override string DisplayName => "测试旗子史莱姆";

    public override int Banner => Type;

    public override int BannerItem => ModContent.ItemType<TestZBannerItem>();

    public override void SetStaticDefaults()
    {
        ItemID.Sets.BannerStrength[ModContent.ItemType<TestZBannerItem>()] = ItemID.BannerEffect.Reduced;
        ItemID.Sets.KillsToBanner[ModContent.ItemType<TestZBannerItem>()] = 3;
    }

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

    public override void OnSpawn(IEntitySource source) => PT.Info("测试旗子史莱姆已刷出");
}
