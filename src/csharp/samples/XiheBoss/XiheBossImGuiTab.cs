// 羲和召唤与试玩物品入口。
using Pylon.Mod;
using Pylon.ModLoader;
using Pylon.ModLoader.UI;
using Terraria;

namespace XiheBoss
{
    public sealed class XiheBossImGuiTab : IModImGuiTab
    {
        public string Title => "羲和 Boss";
        private static string _status;

        public void Draw()
        {
            TerrariaUI.SeparatorText("羲和");
            if (TerrariaUI.Button("召唤羲和")) Spawn();
            TerrariaUI.SeparatorText("试玩物品");
            if (TerrariaUI.Button("给我一把天顶剑")) Give(Terraria.ID.ItemID.Zenith, "天顶剑");
            if (TerrariaUI.Button("给我一个扫把坐骑")) Give(Terraria.ID.ItemID.WitchBroom, "女巫扫帚");
            if (!string.IsNullOrEmpty(_status)) TerrariaUI.Status(_status);
        }

        private static void Give(int type, string name)
        {
            try
            {
                var player = Main.player[Main.myPlayer];
                if (player == null || !player.active)
                {
                    _status = "需要先进世界。";
                    return;
                }
                player.QuickSpawnItem(player.GetItemSource_Misc(0), type, 1);
                _status = "已添加：" + name;
            }
            catch (System.Exception e)
            {
                _status = "添加失败：" + e.Message;
                XiheBossMod.Log?.Warn("[tab] 添加物品失败：" + e.Message);
            }
        }

        private static void Spawn()
        {
            try
            {
                var player = Main.player[Main.myPlayer];
                if (player == null || !player.active) return;

                var type = ModContent.NpcType<XiheBossNpc>();
                if (type <= 0) return;

                var idx = NPC.NewNPC(null, (int)player.Center.X, (int)player.Center.Y - 500, type);
                if (idx >= 0 && idx < Main.npc.Length)
                {
                    Main.npc[idx].target = player.whoAmI;
                    Main.npc[idx].GivenName = "羲和";
                }
            }
            catch (System.Exception e)
            {
                XiheBossMod.Log?.Warn("[tab] 召唤失败：" + e.Message);
            }
        }
    }
}