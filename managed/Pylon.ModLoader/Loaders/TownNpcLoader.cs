using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;

namespace Pylon.ModLoader
{
    /// <summary>城镇 NPC：搬进空房、名字表、房间条件、聊天按钮与商店。</summary>
    internal static class TownNpcLoader
    {
        private const int Merchant = 17;

        private static readonly Dictionary<Type, OverridePlan> Plans = new();
        private static bool _installed;
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

        internal static void Install(Harmony harmony, ModLogger log)
        {
            if (_installed || ModContent.Count(ContentKind.Npc) == 0) return;
            _installed = true;

            GameHooks.Patch(harmony, log, typeof(TownNpcLoader), typeof(Main), "UpdateTime_SpawnTownNPCs",
                Type.EmptyTypes, postfixHandler: nameof(SpawnPostfix));
            GameHooks.Patch(harmony, log, typeof(TownNpcLoader), typeof(WorldGen),
                "CheckSpecialTownNPCSpawningConditions", new[] { typeof(int) },
                nameof(CheckConditionsPrefix));
            GameHooks.Patch(harmony, log, typeof(TownNpcLoader), typeof(NPC), "getNewNPCName",
                new[] { typeof(int) }, prefixHandler: nameof(NewNamePrefix));

            GameHooks.Patch(harmony, log, typeof(TownNpcLoader), typeof(GUINPCDialogue), "SetupButtonText",
                new[]
                {
                    typeof(string).MakeByRefType(), typeof(Texture2D).MakeByRefType(),
                    typeof(string).MakeByRefType(), typeof(Texture2D).MakeByRefType(),
                    typeof(int).MakeByRefType(), typeof(bool).MakeByRefType(),
                }, nameof(ButtonsPrefix), nameof(ButtonsPostfix));
            GameHooks.Patch(harmony, log, typeof(TownNpcLoader), typeof(GUINPCDialogue), "Option1Clicked",
                new[] { typeof(int) }, prefixHandler: nameof(Option1Prefix));
            GameHooks.Patch(harmony, log, typeof(TownNpcLoader), typeof(GUINPCDialogue), "Option2Clicked",
                Type.EmptyTypes, prefixHandler: nameof(Option2Prefix));
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

        private static void SpawnPostfix()
        {
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

        private static bool CheckConditionsPrefix(int type)
        {
            var npc = ModContent.Find(ContentKind.Npc, type) as ModNpc;
            if (npc == null || !Over(npc, nameof(ModNpc.CheckConditions))) return true;
            try { return npc.CheckConditions(WorldGen.roomX1, WorldGen.roomX2, WorldGen.roomY1, WorldGen.roomY2); }
            catch (Exception e) { Safe.Report(npc.Name + ".CheckConditions", e); return true; }
        }

        private static bool NewNamePrefix(int npcType, ref string __result)
        {
            var npc = ModContent.Find(ContentKind.Npc, npcType) as ModNpc;
            if (npc == null || !Over(npc, nameof(ModNpc.SetNPCNameList))) return true;
            try
            {
                var names = npc.SetNPCNameList();
                if (names == null || names.Length == 0) return true;
                __result = names[Main.rand.Next(names.Length)];
                return false;
            }
            catch (Exception e)
            {
                Safe.Report(npc.Name + ".SetNPCNameList", e);
                return true;
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

        private static bool ButtonsPrefix()
        {
            _talking = TalkingTo();
            if (_talking == null) return true;
            _talkingType = _talking.type;
            // 游戏按类型 switch 选按钮图；先按商人跑，再覆盖文字。
            _talking.type = Merchant;
            return true;
        }

        private static void ButtonsPostfix(ref string focusText, ref Texture2D option1Tex, ref string focusText3,
                                           ref Texture2D option2Tex, ref int cost)
        {
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

        private static bool Option1Prefix(int healCost)
        {
            var npc = TalkingTo();
            if (npc == null) return true;
            var modNpc = ModContent.Find(ContentKind.Npc, npc.type) as ModNpc;
            if (modNpc == null || !Over(modNpc, nameof(ModNpc.OnChatButtonClicked))) return true;
            return !Clicked(npc, modNpc, true);
        }

        private static bool Option2Prefix()
        {
            var npc = TalkingTo();
            if (npc == null) return true;
            var modNpc = ModContent.Find(ContentKind.Npc, npc.type) as ModNpc;
            if (modNpc == null || !Over(modNpc, nameof(ModNpc.OnChatButtonClicked))) return true;
            return !Clicked(npc, modNpc, false);
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
