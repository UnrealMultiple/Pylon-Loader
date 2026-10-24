using Pylon.Mod;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Effects;
using Pylon.ModLoader.ImGuiHost;
using Terraria;
using Terraria.ID;

namespace XiheBoss
{
    /// <summary>羲和示例 Mod 的 ImGui 页：Boss 召唤、网格上传 A/B 与试玩物品行。</summary>
    public sealed class XiheBossImGuiTab : IModImGuiTab
    {
        public string Title => "羲和 Boss";

        private static readonly string[] UploadModes = ["原路径", "聚合"];

        private static int _uploadMode;

        public void Draw()
        {
            ModernUI.PageHead("羲和 Boss", "XIHE BOSS", "羲和召唤与试玩物品入口 · XiheBoss 示例 Mod");

            ModernUI.BeginCard("羲和", "SUMMON");
            ModernUI.BeginRow();
            ModernUI.RowLabel("Boss 召唤", "在玩家头顶生成 XiheBossNpc");
            if (ModernUI.PrimaryButton("召唤羲和", default, ModernUI.Icon.Sparkle, rightAlign: true)) SpawnBoss();
            ModernUI.EndRow();

            ModernUI.BeginRow();
            ModernUI.RowLabel("网格上传 A/B", "XiheMesh 流式聚合开关");
            if (ModernUI.Seg("##uploadAB", ref _uploadMode, UploadModes, rightAlign: true))
            {
                ScreenGraphics.SetMeshStreaming(_uploadMode == 1);
                ModernUI.ShowStatus("网格上传 A/B → " + UploadModes[_uploadMode]);
                XiheBossMod.Log?.Info("[upload-ab] streaming=" + (_uploadMode == 1));
            }
            ModernUI.EndRow();
            ModernUI.StatusRow();
            ModernUI.EndCard();

            ModernUI.BeginCard("试玩物品", "SANDBOX ITEMS");
            if (ModernUI.ItemRow("天顶剑", "ZENITH", "终焉之剑，终极近战武器。", 0x5A7FC0, 0x2C4470,
                    ModernUI.Icon.Sword, "获取")) Give("天顶剑", ItemID.Zenith);
            if (ModernUI.ItemRow("女巫扫帚", "WITCH BROOM", "坐骑：悬浮扫帚。", 0x4FA080, 0x1E5A44,
                    ModernUI.Icon.Broom, "获取")) Give("女巫扫帚", ItemID.WitchBroom);
            if (ModernUI.ItemRow("羲和魔杖", "XIHE WAND", "按住蓄力，降下激光柱。", 0xC08A4A, 0x6E4A1E,
                    ModernUI.Icon.Wand, "获取")) GiveWand(1);
            if (ModernUI.ItemRow("羲和的印章", "XIHE SEAL", "召唤物：外摆线仪式。", 0x8A6BC0, 0x3E2C6E,
                    ModernUI.Icon.Seal, "获取")) GiveSummon(1);
            ModernUI.EndCard();

            ModernUI.BeginCard("史莱姆王", "KING SLIME");
            ModernUI.BeginRow();
            ModernUI.RowLabel("Boss 召唤", "原版 type 50，MEAC AI 接管");
            if (ModernUI.PrimaryButton("召唤史莱姆王", default, ModernUI.Icon.Star, rightAlign: true))
                SummonBoss(NPCID.KingSlime, "史莱姆王");
            ModernUI.EndRow();
            if (ModernUI.ItemRow("史莱姆王冠", "SLIME CROWN", "原版召唤物，使用后召唤史莱姆王。", 0x4F8AC0, 0x2A5A8A,
                    ModernUI.Icon.Star, "获取")) Give("史莱姆王冠", ItemID.SlimeCrown);
            ModernUI.EndCard();

            ModernUI.BeginCard("克苏鲁之眼", "EYE OF CTHULHU");
            ModernUI.BeginRow();
            ModernUI.RowLabel("Boss 召唤", "原版 type 4，MEAC AI 接管");
            if (ModernUI.PrimaryButton("召唤克苏鲁之眼", default, ModernUI.Icon.Sparkle, rightAlign: true))
                SummonBoss(NPCID.EyeofCthulhu, "克苏鲁之眼");
            ModernUI.EndRow();
            if (ModernUI.ItemRow("可疑的眼珠", "SUSPICIOUS EYE", "原版召唤物，夜晚使用召唤克苏鲁之眼。", 0x7A4FA0, 0x3E2A6E,
                    ModernUI.Icon.Seal, "获取")) Give("可疑的眼珠", ItemID.SuspiciousLookingEye);
            ModernUI.EndCard();
        }

        private static void SummonBoss(int npcId, string name)
        {
            try
            {
                var player = Main.player[Main.myPlayer];
                if (player == null || !player.active)
                {
                    ModernUI.ShowStatus("需要先进世界。");
                    return;
                }

                var idx = NPC.NewNPC(null, (int)player.Center.X, (int)(player.Center.Y - 200f), npcId);
                ModernUI.ShowStatus($"已召唤{name}（NPC 下标 {idx}）");
                XiheBossMod.Log?.Info($"[tab] NPC.NewNPC({name}，下标 {idx}）");
            }
            catch (System.Exception e)
            {
                ModernUI.ShowStatus("召唤失败：" + e.Message);
                XiheBossMod.Log?.Warn("[tab] 召唤失败：" + e);
            }
        }

        private static void SpawnBoss()
        {
            try
            {
                var player = Main.player[Main.myPlayer];
                if (player == null || !player.active)
                {
                    ModernUI.ShowStatus("需要先进世界。");
                    return;
                }
                XiheBossNpc.SpawnCountdown = 1;
                ModernUI.ShowStatus("已召唤羲和（XiheBossNpc）");
                XiheBossMod.Log?.Info("[tab] NPC.NewNPC(type=XiheBossNpc)");
            }
            catch (System.Exception e)
            {
                ModernUI.ShowStatus("召唤失败：" + e.Message);
                XiheBossMod.Log?.Warn("[tab] 召唤失败：" + e);
            }
        }

        private static void Give(string name, int itemId)
        {
            try
            {
                var player = Main.player[Main.myPlayer];
                if (player == null || !player.active)
                {
                    ModernUI.ShowStatus("需要先进世界。");
                    return;
                }
                var source = player.GetItemSource_Misc(0);
                player.QuickSpawnItem(source, itemId, 1);
                ModernUI.ShowStatus("已添加：" + name);
                XiheBossMod.Log?.Info("[tab] QuickSpawnItem → " + name + "（ID " + itemId + "）");
            }
            catch (System.Exception e)
            {
                ModernUI.ShowStatus("添加失败：" + e.Message);
                XiheBossMod.Log?.Warn("[tab] 添加失败：" + e);
            }
        }

        private static void GiveSummon(int stack)
        {
            try
            {
                var player = Main.player[Main.myPlayer];
                if (player == null || !player.active)
                {
                    ModernUI.ShowStatus("需要先进世界。");
                    return;
                }
                var id = ModContent.ItemType<XiheSummonItem>();
                if (id <= 0)
                {
                    ModernUI.ShowStatus("物品还没注册好（等进世界后再点）。");
                    return;
                }
                var source = player.GetItemSource_Misc(0);
                player.QuickSpawnItem(source, id, stack);
                ModernUI.ShowStatus($"已添加 {stack} 枚羲和的印章（ID {id}）。");
                XiheBossMod.Log?.Info($"[tab] 添加羲和的印章 x{stack}（ID {id}）");
            }
            catch (System.Exception e)
            {
                ModernUI.ShowStatus("添加失败：" + e.Message);
                XiheBossMod.Log?.Warn("[tab] 添加失败：" + e);
            }
        }

        private static void GiveWand(int stack)
        {
            try
            {
                var player = Main.player[Main.myPlayer];
                if (player == null || !player.active)
                {
                    ModernUI.ShowStatus("需要先进世界。");
                    return;
                }
                var id = ModContent.ItemType<Wand.XiHeMagicItem>();
                if (id <= 0)
                {
                    ModernUI.ShowStatus("物品还没注册好（等进世界后再点）。");
                    return;
                }
                var source = player.GetItemSource_Misc(0);
                player.QuickSpawnItem(source, id, stack);
                ModernUI.ShowStatus($"已添加 {stack} 把羲和魔杖（ID {id}）。");
                XiheBossMod.Log?.Info($"[tab] 添加羲和魔杖 x{stack}（ID {id}）");
            }
            catch (System.Exception e)
            {
                ModernUI.ShowStatus("添加失败：" + e.Message);
                XiheBossMod.Log?.Warn("[tab] 添加失败：" + e);
            }
        }
    }
}
