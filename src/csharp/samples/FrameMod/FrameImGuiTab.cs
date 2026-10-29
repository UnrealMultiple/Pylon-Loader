// FrameMod / FrameImGuiTab —— 示例 ImGui Tab：Mod 实现 IModImGuiTab，宿主把它排进菜单侧边栏
using System;
using Pylon.Mod;
using ImGuiApi = Pylon.ImGui.ImGui;
using Terraria;
using Terraria.ID;
using Il2CppMicrosoft.Xna.Framework;

namespace FrameMod
{
    public sealed class FrameImGuiTab : IModImGuiTab
    {
        private bool _enabled = true;
        private float _power = 0.7f;
        private static int _mode;

        public string Title => "FrameMod";

        public void Draw()
        {
            ImGuiApi.SeparatorText("Mod Tab 示例（Pylon.ImGui API）");
            ImGuiApi.Text("这个页是 Mod 自己画的——注册时只需一个 Title + Draw。");
            ImGuiApi.Checkbox("启用", ref _enabled);
            if (ImGuiApi.SliderFloat("能量", ref _power, 0f, 1f)) { }
            if (ImGuiApi.Combo("模式", ref _mode, ["Normal", "Hard"])) { }
            if (ImGuiApi.Button("示例按钮")) ImGuiApi.Text("按钮被点击了！");

            // ---- 添加到背包（测试物品链路）----
            // 按钮竖排、每行两个；日志/状态另起一行不被按钮挤。
            ImGuiApi.SeparatorText("测试内容");
            _buttonColumn = 0;

            AddButton("添加太空枪", () => { var id = ItemID.SpaceGun; AddToInventory(id, 1); _lastAdded = $"太空枪 ({id})"; });
            AddButton("添加测试之剑", () =>
            {
                var id = Pylon.ModLoader.ModContent.ItemType<TestSword>();
                SpawnTestSwordWithPrefix(id);
            });
            AddButton("添加测试召唤物", () => { var id = Pylon.ModLoader.ModContent.ItemType<TestSummonItem>(); AddToInventory(id, 1); _lastAdded = $"测试召唤物 ({id})，用它召唤测试史莱姆"; });
            AddButton("添加测试药剂", () => { var id = Pylon.ModLoader.ModContent.ItemType<TestBuffPotion>(); AddToInventory(id, 1); _lastAdded = $"测试药剂 ({id})，喝了给测试增益"; });
            AddButton("添加测试坐骑哨", () => { var id = Pylon.ModLoader.ModContent.ItemType<TestMountItem>(); AddToInventory(id, 1); _lastAdded = $"测试坐骑哨 ({id})，用后骑测试史莱姆"; });
            AddButton("测试剑加前缀", () =>
            {
                var id = Pylon.ModLoader.ModContent.ItemType<TestSword>();
                SpawnTestSwordWithPrefix(id);
            });

            if (_lastAdded != null) ImGuiApi.Text("已添加：" + _lastAdded);

            // ---- 管线测试（进世界后点；结果看日志 / 下方状态）----
            ImGuiApi.SeparatorText("管线测试（进世界后点）");
            _buttonColumn = 0;
            AddButton("跑全部", () => RunSuite("all"));
            AddButton("方块", () => RunSuite("tile"));
            AddButton("家具", () => RunSuite("furniture"));
            AddButton("墙壁", () => RunSuite("wall"));
            AddButton("城镇 NPC", () => RunSuite("town"));
            AddButton("音效", () => RunSuite("sound"));
            AddButton("音乐", () => RunSuite("music"));
            AddButton("网络包", () => RunSuite("net"));
            if (_lastAdded != null) ImGuiApi.Text("测试：" + _lastAdded);

            // ---- 纹理上传 + 图片绘制示例 ----
            EnsureTexture();
            if (_texId != 0)
            {
                ImGuiApi.SeparatorText("纹理/图片");
                ImGuiApi.Image(_texId, new Pylon.ImGui.ImVec2(64, 64));   // 控件：光标处画 64×64
                ImGuiApi.SameLine();
                ImGuiApi.Image(_texId, new Pylon.ImGui.ImVec2(128, 128));  // 放大版
            }
        }

        /// <summary>用 Item.NewItem 带前缀直接生成测试之剑（掉落/背包都带）。
        /// ⚠ 用**不带 source 的老版重载** NewItem(X, Y, W, H, Type, Stack, noBroadcast, pfix)——带 source 重载的
        /// velocity/modifier 是 interop "Missing" 可选参，不显式传会以 Missing 进原生 → NRE；别用 QuickSpawnItem（掉落随机前缀）。</summary>
        private static void SpawnTestSwordWithPrefix(int id)
        {
            try
            {
                var player = Main.LocalPlayer;
                if (player == null || !player.active)
                {
                    _lastAdded = "需要先进世界（当前没有玩家）";
                    return;
                }
                var prefix = Pylon.ModLoader.ModContent.PrefixType<TestPrefix>();
                var pos = player.Center;
                var index = Item.NewItem((int)pos.X, (int)pos.Y, 1, 1, id, 1, false, prefix);
                _lastAdded = $"测试剑：NewItem 带前缀 {prefix}（index={index}）";
                FrameMod.Log?.Info($"[prefix] 按钮：NewItem({id}, 1, prefix={prefix}) index={index}");
            }
            catch (Exception e)
            {
                _lastAdded = "失败：" + e.Message;
                FrameMod.Log?.Info($"[prefix] 按钮异常：{e.GetType().Name}: {e.Message}\n{e.StackTrace}");
            }
        }

        /// <summary>按钮竖排、每行两个（第二列之后自动换行）。</summary>
        private static void AddButton(string label, Action onClick)
        {
            if (ImGuiApi.Button(label))
            {
                try { onClick(); }
                catch (Exception e) { _lastAdded = "失败：" + e.Message; }
            }
            if (++_buttonColumn % 2 != 0) ImGuiApi.SameLine();
        }

        private static void RunSuite(string suite)
        {
            if (Main.gameMenu || Main.LocalPlayer == null || !Main.LocalPlayer.active)
            {
                _lastAdded = "需要先进世界";
                return;
            }
            FrameSelfTest.RunManual(suite);
            _lastAdded = suite + " 已排队（日志搜 [selftest]）";
        }

        private static int _buttonColumn;
        private static string _lastAdded;

        /// <summary>给当前玩家背包塞物品（原版/占号 ID 都能走这条路，验证添加链路）。</summary>
        private static void AddToInventory(int itemId, int stack)
        {
            try
            {
                var player = Main.player[Main.myPlayer];
                if (player == null) return;
                var source = player.GetItemSource_Misc(0);   // ItemDropReason.Misc
                player.QuickSpawnItem(source, itemId, stack);
            }
            catch (Exception e)
            {
                ImGuiApi.Text("添加失败：" + e.Message);
            }
        }

        // 纹理：Mod 侧纯字节生成（64×64 RGBA 渐变），UploadTexture 上传后 ImGuiApi.Image 显示
        private static ulong _texId;
        private static bool _texTried;

        private static void EnsureTexture()
        {
            if (_texTried) return;
            _texTried = true;
            const int w = 64, h = 64;
            var rgba = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4;
                    rgba[i] = (byte)(x * 255 / w);          // R：横向渐变
                    rgba[i + 1] = (byte)(y * 255 / h);      // G：纵向渐变
                    rgba[i + 2] = 124;                       // B：固定紫
                    rgba[i + 3] = 255;                       // A：不透明
                }
            }
            _texId = ImGuiApi.UploadTexture(rgba, w, h);
            if (_texId == 0) ImGuiApi.Text("纹理上传失败（Vulkan 未就绪？）");
        }
    }
}