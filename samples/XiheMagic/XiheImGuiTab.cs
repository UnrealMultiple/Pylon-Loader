// ImGui 页：一键把羲和魔杖塞进背包（测试/试玩用，不用打 Boss 或找材料）。
using System;
using Pylon.Mod;
using Pylon.ModLoader;
using Terraria;
using ImGuiApi = Pylon.ImGui.ImGui;

namespace XiheMagic
{
    public sealed class XiheImGuiTab : IModImGuiTab
    {
        public string Title => "羲和魔杖";

        private static string _status;

        public void Draw()
        {
            ImGuiApi.SeparatorText("羲和魔杖（MEAC XiHeMagicItem 复刻）");
            ImGuiApi.Text("按住使用蓄力，约 3.3 秒后在准星处降下激光柱；");
            ImGuiApi.Text("激光柱可拖动、持续消耗魔力，松手中断。");

            ImGuiApi.SeparatorText("获得");

            if (ImGuiApi.Button("添加到背包"))
                Give(1);

            ImGuiApi.SameLine();
        }


        private static void Give(int stack)
        {
            try
            {
                var player = Main.player[Main.myPlayer];
                if (player == null || !player.active)
                {
                    _status = "需要先进世界。";
                    return;
                }

                var id = ModContent.ItemType<XiHeMagicItem>();
                if (id <= 0)
                {
                    _status = "物品还没注册好（等进世界后再点）。";
                    return;
                }

                var source = player.GetItemSource_Misc(0);
                player.QuickSpawnItem(source, id, stack);
                _status = $"已添加 {stack} 把羲和魔杖（ID {id}）。";
                XiheMagicMod.Log?.Info($"[tab] 添加羲和魔杖 x{stack}（ID {id}）");
            }
            catch (Exception e)
            {
                _status = "添加失败：" + e.Message;
                XiheMagicMod.Log?.Warn("[tab] 添加失败：" + e);
            }
        }
    }
}
