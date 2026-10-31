using System;
using System.Collections.Generic;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;

namespace Pylon.ModLoader
{
    /// <summary>城镇 NPC：搬进空房、名字表、房间条件、聊天按钮与商店。</summary>
    internal sealed class TownNpcLoader : ILoaderModule
    {
        public TownNpcLoader(ModLogger logger) => _ = logger; // 本管线不用日志

        public string Name => "townnpc";

        public int Order => 320;

        private const int Merchant = 17;

        private static readonly Dictionary<Type, OverridePlan> Plans = new();
        private static NPC _talking;
        private static int _talkingType;

        private static OverridePlan PlanOf(ModNpc npc)
        {
            var type = npc.GetType();
            if (Plans.TryGetValue(type, out var plan)) return plan;
            plan = OverridePlan.Build(type, typeof(ModNpc));
            Plans[type] = plan;
            return plan;
        }

        private static bool Over(ModNpc npc, string method) => PlanOf(npc).Has(method);

        public bool Install()
        {
            if (ModContent.Count(ContentKind.Npc) == 0) return false;

            On.Terraria.Main.UpdateTime_SpawnTownNPCs += SpawnPostfix;
            On.Terraria.WorldGen.CheckSpecialTownNPCSpawningConditions += CheckConditionsPrefix;
            On.Terraria.NPC.getNewNPCName += NewNamePrefix;
            On.GUINPCDialogue.SetupButtonText += Buttons;
            On.GUINPCDialogue.Option1Clicked += Option1Prefix;
            On.GUINPCDialogue.Option2Clicked += Option2Prefix;
            return true;
        }

        /// <summary>内容安装时把各城镇 NPC 的商店登记进来。</summary>
        internal static void Setup(ModLogger log)
        {
            foreach (var npc in ModContent.GetContent<ModNpc>())
            {
                if (!Over(npc, nameof(ModNpc.AddShops))) continue;
                var shops = new List<NpcShop>();
                try { npc.AddShops(shops); }
                catch (Exception e) { log?.Warn($"{npc.Name} 的 AddShops 失败：{e.Message}"); }
            }
        }

        // ===================== 搬进空房 =====================

        private static void SpawnPostfix(On.Terraria.Main.orig_UpdateTime_SpawnTownNPCs orig)
        {
            orig();
            if (Main.netMode == 1 || Main.checkForSpawns != 0) return;

            var townCount = -1;
            foreach (var npc in ModContent.GetContent<ModNpc>())
            {
                if (!Over(npc, nameof(ModNpc.CanTownNPCSpawn))) continue;
                if (NPC.AnyNPCs(npc.Type)) continue;

                if (townCount < 0) townCount = CountTownNPCs();
                var can = false;
                try { can = npc.CanTownNPCSpawn(townCount); }
                catch (Exception e) { Safe.Report(npc.Name + ".CanTownNPCSpawn", e); }
                if (!can) continue;

                Main.townNPCCanSpawn[npc.Type] = true;
                if (WorldGen.prioritizedTownNPCType == 0) WorldGen.prioritizedTownNPCType = npc.Type;
            }
        }

        private static int CountTownNPCs()
        {
            var count = 0;
            foreach (var npc in Main.npc)
            {
                if (npc != null && npc.active && npc.townNPC && npc.type != NPCID.TravellingMerchant) count++;
            }
            return count;
        }

        private static bool CheckConditionsPrefix(On.Terraria.WorldGen.orig_CheckSpecialTownNPCSpawningConditions orig,
                                                  int type)
        {
            var npc = ModContent.Find(ContentKind.Npc, type) as ModNpc;
            if (npc == null || !Over(npc, nameof(ModNpc.CheckConditions))) return orig(type);
            try
            {
                var can = npc.CheckConditions(WorldGen.roomX1, WorldGen.roomX2, WorldGen.roomY1, WorldGen.roomY2);
                return can ? orig(type) : false;
            }
            catch (Exception e) { Safe.Report(npc.Name + ".CheckConditions", e); return orig(type); }
        }

        private static string NewNamePrefix(On.Terraria.NPC.orig_getNewNPCName orig, int npcType)
        {
            var npc = ModContent.Find(ContentKind.Npc, npcType) as ModNpc;
            if (npc == null || !Over(npc, nameof(ModNpc.SetNPCNameList))) return orig(npcType);
            try
            {
                var names = npc.SetNPCNameList();
                if (names == null || names.Length == 0) return orig(npcType);
                return names[Main.rand.Next(names.Length)];
            }
            catch (Exception e)
            {
                Safe.Report(npc.Name + ".SetNPCNameList", e);
                return orig(npcType);
            }
        }

        // ===================== 聊天 =====================

        private static NPC TalkingTo()
        {
            var player = Main.player[Main.myPlayer];
            var index = player?.talkNPC ?? -1;
            if (index < 0 || index >= Main.npc.Length) return null;
            var npc = Main.npc[index];
            if (npc == null || !npc.active) return null;
            return ModContent.Find(ContentKind.Npc, npc.type) is ModNpc ? npc : null;
        }

        /// <summary>SetupButtonText：先按商人跑按钮图，orig 之后覆盖成 mod 的文字。</summary>
        private static void Buttons(On.GUINPCDialogue.orig_SetupButtonText orig, GUINPCDialogue self,
                                    ref string focusText, ref Texture2D option1Tex, ref string focusText3,
                                    ref Texture2D option2Tex, ref int cost, ref bool showHappiness)
        {
            _talking = TalkingTo();
            if (_talking != null)
            {
                _talkingType = _talking.type;
                // 游戏按类型 switch 选按钮图；先按商人跑，再覆盖文字。
                _talking.type = Merchant;
            }

            orig(self, ref focusText, ref option1Tex, ref focusText3, ref option2Tex, ref cost, ref showHappiness);

            var npc = _talking;
            _talking = null;
            if (npc == null) return;
            npc.type = _talkingType;

            var modNpc = ModContent.Find(ContentKind.Npc, npc.type) as ModNpc;
            if (modNpc == null || !Over(modNpc, nameof(ModNpc.SetChatButtons))) return;

            var buttons = new ModNpc.ChatButtons();
            try { modNpc.SetChatButtons(npc, buttons); }
            catch (Exception e) { Safe.Report(modNpc.Name + ".SetChatButtons", e); }

            var icon = option1Tex;
            focusText = buttons.Button ?? "";
            focusText3 = buttons.Button2 ?? "";
            option1Tex = string.IsNullOrEmpty(buttons.Button) ? null : icon;
            option2Tex = string.IsNullOrEmpty(buttons.Button2) ? null : icon;
            cost = 0;
        }

        private static void Option1Prefix(On.GUINPCDialogue.orig_Option1Clicked orig, GUINPCDialogue self,
                                          int healCost)
        {
            var npc = TalkingTo();
            if (npc == null) { orig(self, healCost); return; }
            var modNpc = ModContent.Find(ContentKind.Npc, npc.type) as ModNpc;
            if (modNpc == null || !Over(modNpc, nameof(ModNpc.OnChatButtonClicked))) { orig(self, healCost); return; }
            if (Clicked(npc, modNpc, true)) return;   // 被 mod 接走：跳过原版
            orig(self, healCost);
        }

        private static void Option2Prefix(On.GUINPCDialogue.orig_Option2Clicked orig, GUINPCDialogue self)
        {
            var npc = TalkingTo();
            if (npc == null) { orig(self); return; }
            var modNpc = ModContent.Find(ContentKind.Npc, npc.type) as ModNpc;
            if (modNpc == null || !Over(modNpc, nameof(ModNpc.OnChatButtonClicked))) { orig(self); return; }
            if (Clicked(npc, modNpc, false)) return;   // 被 mod 接走：跳过原版
            orig(self);
        }

        private static bool Clicked(NPC npc, ModNpc modNpc, bool first)
        {
            string shopName;
            try { shopName = modNpc.OnChatButtonClicked(npc, first); }
            catch (Exception e)
            {
                Safe.Report(modNpc.Name + ".OnChatButtonClicked", e);
                return true;
            }
            if (shopName == null) return true;

            var shop = NpcShop.Find(npc.type, shopName);
            if (shop == null)
            {
                Safe.Once("shop." + npc.type + "." + shopName,
                    $"{modNpc.Name} 要开商店 '{shopName}'，但没有注册", true);
                return true;
            }
            shop.Open();
            return true;
        }
    }
}
