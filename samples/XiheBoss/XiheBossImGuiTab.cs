// ImGui 页：只保留「召唤羲和」入口（正式游戏用；不再放阶段跳转/技能测试等调试按钮）。
using Pylon.Mod;
using Pylon.ModLoader;
using Terraria;
using ImGuiApi = Pylon.ImGui.ImGui;

namespace XiheBoss
{
    public sealed class XiheBossImGuiTab : IModImGuiTab
    {
        public string Title => "羲和 Boss";

        private static string _status = "";

        public void Draw()
        {
            ImGuiApi.SeparatorText("羲和");
            if (ImGuiApi.Button("召唤羲和")) Spawn();
            if (!string.IsNullOrEmpty(_status)) ImGuiApi.Text(_status);
        }

        private static void Spawn()
        {
            try
            {
                var player = Main.player[Main.myPlayer];
                if (player == null || !player.active)
                {
                    _status = "需要先进世界。";
                    return;
                }

                var type = ModContent.NpcType<XiheBossNpc>();
                if (type <= 0)
                {
                    _status = "NPC 还没注册好（等进世界后再点）。";
                    return;
                }

                var idx = NPC.NewNPC(null, (int)player.Center.X, (int)player.Center.Y - 500, type);
                if (idx >= 0 && idx < Main.npc.Length)
                {
                    Main.npc[idx].target = player.whoAmI;
                    Main.npc[idx].GivenName = "羲和";
                    _status = $"已召唤（NPC #{idx}）。";
                }
                else _status = "召唤失败：NPC.NewNPC 返回 " + idx;
            }
            catch (System.Exception e)
            {
                _status = "召唤失败：" + e.Message;
                XiheBossMod.Log?.Warn("[tab] 召唤失败：" + e);
            }
        }
    }
}