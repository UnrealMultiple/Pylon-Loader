using System.Collections.Generic;
using Pylon.ModLoader;
using Terraria;
using Terraria.ID;

namespace FrameMod
{
    internal sealed class TestTownNpc : ModNpc
    {
        internal static NpcShop Shop;

        public override void SetStaticDefaults()
        {
            Main.npcFrameCount[Type] = 10;
        }

        public override void SetDefaults(NPC npc)
        {
            npc.width = 18;
            npc.height = 40;
            npc.aiStyle = 7;
            npc.friendly = true;
            npc.townNPC = true;
            npc.lifeMax = 250;
            npc.damage = 10;
            npc.defense = 15;
            npc.HitSound = SoundID.NPCHit1;
            npc.DeathSound = SoundID.NPCDeath1;
        }

        public override bool CanTownNPCSpawn(int numTownNPCs) => true;
        public override bool CheckConditions(int left, int right, int top, int bottom) => true;
        public override string[] SetNPCNameList() => ["PylonA", "PylonB"];
        public override string GetChat(NPC npc) => "FrameMod 测试居民";
        public override int NPCHeadSlot() => 0;

        public override void SetChatButtons(NPC npc, ChatButtons buttons)
        {
            buttons.Button = "Test Shop";
            buttons.Button2 = "";
        }

        public override string OnChatButtonClicked(NPC npc, bool firstButton) => firstButton ? "Test Shop" : null;

        public override void AddShops(List<NpcShop> shops)
        {
            Shop = new NpcShop(Type, "Test Shop")
                .Add(ItemID.Torch, price: 100)
                .Register();
        }
    }
}

