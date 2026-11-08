using System.Collections.Generic;
using Pylon.ModLoader;
using Terraria;
using Terraria.ID;

namespace PipelineTest.Content;

/// <summary>测试商人：城镇 NPC，聊天有「商店」按钮，店里卖 测试物品(50金) 与 凝胶(5金)。</summary>
public sealed class TestTownNpc : ModNpc
{
    public override string DisplayName => "测试商人";

    public override void SetDefaults(NPC npc)
    {
        npc.width = 18;
        npc.height = 40;
        npc.aiStyle = 7;          // 城镇 NPC 的站立 AI
        npc.townNPC = true;
        npc.friendly = true;
        npc.lifeMax = 250;
        npc.life = 250;
        npc.damage = 0;
        npc.defense = 15;
        npc.knockBackResist = 0.5f;
        npc.HitSound = SoundID.NPCHit1;
        npc.DeathSound = SoundID.NPCDeath1;
    }

    public override bool CanTownNPCSpawn(int numTownNPCs) => true;

    public override string[] SetNPCNameList() => ["小明"];

    public override string GetChat(NPC npc)
    {
        PT.Info($"GetChat 被调用（npc.type={npc.type}）");
        return "我是测试商人，点下面的按钮看看商店。";
    }

    public override void SetChatButtons(NPC npc, ChatButtons buttons)
    {
        PT.Info("SetChatButtons 被调用");
        buttons.Button = "商店";
    }

    public override string OnChatButtonClicked(NPC npc, bool firstButton)
    {
        PT.Info($"OnChatButtonClicked(first={firstButton})");
        return firstButton ? "TestShop" : null!;
    }

    public override void AddShops(List<NpcShop> shops)
    {
        var shop = new NpcShop(Type, "TestShop");
        shop.Add(ModContent.TypeOf<TestItem>(), null, 50);
        shop.Add(ItemID.Gel, null, 5);
        shop.Register();

        shops.Add(shop);
    }
}
