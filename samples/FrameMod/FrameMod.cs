// 示例 Mod（框架式）：验证"自动发现 + 占号 + 阶段顺序"这套骨架
using System;
using HarmonyLib;
using Pylon.ModLoader;
using Pylon.ModLoader.UI;
using Terraria;
using Terraria.ID;

namespace FrameMod
{
    public sealed class FrameMod : Mod 
    {
        /// <summary>触屏按钮演示：到 Controls 编辑器里放置（Actions 列表尾部有 "Dash"）。</summary>
        public static ModTouchButton DashButton;

        internal static ModLogger Log;
        internal static FrameMod Instance;
        internal static bool PacketHandled;

        public override void Load()
        {
            Instance = this;
            Log = Logger;
            Logger.Info($"自定义物品 ID：剑={ModContent.ItemType<TestSword>()}，" +
                        $"材料={ModContent.ItemType<TestMaterial>()}");

            Logger.Info($"已注册 {GameContent.Pending} 个待注入内容（注入在晚阶段做）");

            // ImGui 菜单 Tab：标题进侧边栏，Draw 每帧在菜单内容区画（Pylon.ImGui 控件 API）
            RegisterImGuiTab(new FrameImGuiTab());
            Logger.Info("[imgui] 已注册示例 ImGui Tab（菜单侧边栏 → FrameMod）");

            // 触屏按钮：注册后在游戏 Controls 编辑器里像 Jump/Heal 一样放置；按下时 onPress 触发
            DashButton = TouchButtonLoader.RegisterButton(this, "Dash", new TouchButtonOptions
            {
                OnPress = player => Logger.Info("[touch] Dash 按下（onPress）"),
            });
            Logger.Info("[touch] 已注册 Dash 触屏按钮（去 Controls 编辑放置）");

            // 示例配置：读到的值（默认或上次存的） + 改一个值（校验/Snap → 落盘 → OnChanged）
            var config = ModContent.GetInstance<FrameConfig>();
            Logger.Info($"[config] 值：EnableThing={config.GetBool("EnableThing")} " +
                        $"Power={config.GetNumber("Power")} Mode={config.GetString("Mode")}");
            config.SetOption("Power", 75);
            ModConfigLoader.Flush();   // 参考里屏外的 SetOption 只标 dirty，落盘由 Apply/Reset/松手触发
            Logger.Info($"[config] SetOption 后 Power={config.GetNumber("Power")}");
        }

        /// <summary>配方：内容就绪后一次（AddRecipeGroups 之后）。用 1 木材做测试之剑（无工作台，随处可做）。</summary>
        public override void AddRecipes()
        {
            var sword = ModContent.ItemType<TestSword>();
            var recipe = new ModRecipe();
            recipe.SetResult(sword, 1);
            recipe.AddIngredient(ItemID.Wood, 1);
            recipe.Register();
            Log?.Info($"[recipe] 已注册测试之剑配方：{sword}（木材×1）");
        }

        /// <summary>延迟阶段：游戏内容就绪后（游戏线程）。</summary>
        public override void SetupContent()
        {
            _log = Logger;

            // 更晚的 hook 注册时机：挂 Main.Update（早阶段挂也行，但需要游戏表的钩子只能在这）
            var update = typeof(Main).GetMethod("Update", new[] { typeof(Il2CppMicrosoft.Xna.Framework.GameTime) });
            if (update != null)
            {
                Harmony.Patch(update, postfix: new HarmonyMethod(typeof(FrameMod).GetMethod(
                    nameof(AfterUpdate), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)));
                Logger.Info("延迟阶段注册 hook：Main.Update（postfix）");
            }
        }

        private static ModLogger _log;
        private static bool _worldJoined;

        /// <summary>进世界后交给 FrameSelfTest：坐骑 / Buff / NPC / 配方 / 前缀 五条管线逐条自检。</summary>
        private static void AfterUpdate()
        {
            if (!_worldJoined && !Main.gameMenu && GameContent.Installed)
            {
                _worldJoined = true;
                _log?.Info("进世界了：开始自检（坐骑 / Buff / NPC / 配方 / 前缀）");
                FrameSelfTest.GiveFurniture();
            }
            FrameSelfTest.Tick(_log);
        }

        public override void PostSetupContent() => Logger.Info("PostSetupContent（内容安装完成后）");

        public override void OnModLoad() => Logger.Info("OnModLoad（Load 之后）");

        public override void OnWorldLoad() => Logger.Info("OnWorldLoad（世界加载/联机收到世界）");

        public override void OnWorldUnload() => Logger.Info("OnWorldUnload（离开世界）");

        public override void HandlePacket(NetReader reader, int whoAmI)
        {
            if (reader.ReadInt32() == 42) PacketHandled = true;
        }
    }
}