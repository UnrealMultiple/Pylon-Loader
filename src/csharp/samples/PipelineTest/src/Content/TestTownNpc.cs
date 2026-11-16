using System.Collections.Generic;
using Pylon.ModLoader;
using Terraria;
using Terraria.ID;

namespace PipelineTest.Content;

/// <summary>测试商人：城镇 NPC，聊天有「商店」按钮，店里卖 测试物品(50金) 与 凝胶(5金)。</summary>
public sealed class TestTownNpc : ModNpc
{
    public override string DisplayName => "测试商人";

    public override void SetDefaults()
    {
        NPC.width = 18;
        NPC.height = 40;
        NPC.aiStyle = 7;          // 城镇 NPC 的站立 AI
        NPC.townNPC = true;
        NPC.friendly = true;
        NPC.lifeMax = 250;
        NPC.life = 250;
        NPC.damage = 0;
        NPC.defense = 15;
        NPC.knockBackResist = 0.5f;
        NPC.HitSound = SoundID.NPCHit1;
        NPC.DeathSound = SoundID.NPCDeath1;
    }

    public override bool CanTownNPCSpawn(int numTownNPCs) => true;

    public override List<string> SetNPCNameList() => ["小明"];

    /// <summary>档案：借原版向导的贴图路径注册（验证注册链；真外观素材路径作者自备）。</summary>
    public override Terraria.GameContent.ITownNPCProfile? TownNPCProfile() =>
        Terraria.GameContent.TownNPCProfiles.LegacyWithSimpleShimmer("Guide", 6, 6, true, true);

    public override string GetChat()
    {
        PT.Info($"GetChat 被调用（npc.type={NPC.type}）");
        return "我是测试商人，点下面的按钮看看商店。";
    }

    public override void SetChatButtons(ref string button, ref string button2)
    {
        PT.Info("SetChatButtons 被调用");
        button = "商店";
    }

    public override void OnChatButtonClicked(bool firstButton, ref string shopName)
    {
        PT.Info($"OnChatButtonClicked(first={firstButton})");
        if (firstButton) shopName = "TestShop";
    }

    public override void AddShops()
    {
        var shop = new NpcShop(Type, "TestShop");
        shop.Add(ModContent.TypeOf<TestItem>(), null, 50);
        shop.Add(ItemID.Gel, null, 5);
        shop.Register();
    }

    // ===================== 城镇攻击（投掷样式；有敌人靠近时 AI_007 会进攻击状态） =====================

    public override void TownNPCAttackStrength(ref int damage, ref float knockback)
    {
        PT.Info($"TownNPCAttackStrength(damage={damage}, knockback={knockback})");
        damage = 15;
        knockback = 4f;
    }

    public override void TownNPCAttackCooldown(ref int cooldown, ref int randExtraCooldown)
    {
        cooldown = 30;
        randExtraCooldown = 30;
    }

    public override void TownNPCAttackProj(ref int projType, ref int attackDelay)
    {
        projType = ProjectileID.MagicDagger;
        attackDelay = 1;
    }

    public override void TownNPCAttackProjSpeed(ref float multiplier, ref float gravityCorrection, ref float randomOffset)
    {
        multiplier = 12f;
        gravityCorrection = 0f;
        randomOffset = 2f;
    }
}
