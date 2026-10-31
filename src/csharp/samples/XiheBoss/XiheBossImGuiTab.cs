// ImGui 页：只保留「召唤羲和」入口（正式游戏用；不再放阶段跳转/技能测试等调试按钮）。
using Pylon.Mod;
using Pylon.ModLoader;
using Pylon.ModLoader.UI;
using Terraria;

namespace XiheBoss
{
    public sealed class XiheBossImGuiTab : IModImGuiTab
    {
        public string Title => "羲和 Boss";

        public void Draw()
        {
            TerrariaUI.SeparatorText("羲和");
            if (TerrariaUI.Button("召唤羲和")) Spawn();
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