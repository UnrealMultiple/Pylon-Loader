using System;
using System.Collections.Generic;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Pylon.Mod;

namespace Pylon.ModLoader.UI
{
    /// <summary>
    /// 加载器 UI 的挂载点：游戏里开两个钩子（更新 / 绘制），把工作分派给
    /// ImGui 覆盖层（ImGuiOverlay）与 SpriteBatch 面板（ModPanel）。
    /// 具体的 ImGui 框架全在 ImGui/ 目录内。
    /// </summary>
    public static class ModUI
    {
        private static bool _hooked;
        private static ModLogger _logger;
        private static readonly List<ModPanel> Panels = new();

        public static bool Visible
        {
            get => ImGuiOverlay.Visible;
            set => ImGuiOverlay.Visible = value;
        }

        internal static ModLogger Logger => _logger;

        /// <summary>晚阶段调用（游戏 UI 就绪后）。</summary>
        internal static void EnsureInitialized(ModLogger logger)
        {
            if (_hooked) return;
            _logger = logger;
            _hooked = true;
            InstallHooks(logger);
            ModConfigMenu.Install(logger, forMainMenu: true);
            TouchButtonHooks.RequestInstall(logger);
        }

        internal static void Register(ModPanel panel)
        {
            if (!Panels.Contains(panel)) Panels.Add(panel);
        }

        /// <summary>Mod 注册 ImGui 菜单 Tab（Mod.RegisterImGuiTab 转到这里，进全局注册表）。</summary>
        internal static void RegisterImGuiTab(IModImGuiTab tab)
            => ImGuiTabRegistry.Register(tab);

        internal static void Update(GameTime gameTime)
        {
            ModConfigMenu.TryInstallPending();
            TouchButtonHooks.TryInstallPending();
            ImGuiOverlay.Update();
        }

        internal static void Draw(SpriteBatch spriteBatch, GameTime gameTime)
        {
            if (!Visible) return;
            foreach (var panel in Panels) panel.Draw(spriteBatch);
        }

        private static void InstallHooks(ModLogger logger)
        {
            On.Terraria.Main.UpdateUIStates += UpdateUIStatesHook;
            logger.Info("UI 驱动已挂：Main.UpdateUIStates(GameTime)");

            On.Terraria.Main.DrawInterface_15_InvasionProgressBars += DrawNoArgsHook;
            logger.Info("UI 绘制点已挂：Main.DrawInterface_15_InvasionProgressBars()");
        }

        private static bool _drawLogged;

        private static void UpdateUIStatesHook(On.Terraria.Main.orig_UpdateUIStates orig, GameTime gameTime)
        {
            orig(gameTime);
            Update(gameTime);
        }

        private static void DrawNoArgsHook(On.Terraria.Main.orig_DrawInterface_15_InvasionProgressBars orig)
        {
            orig();
            if (!_drawLogged)
            {
                _drawLogged = true;
                _logger?.Info("UI 绘制点首次执行：面板开始绘制");
            }
            Draw(Main.spriteBatch, null);
        }

        internal static string Flatten(Exception e)
        {
            var text = new System.Text.StringBuilder();
            var current = e;
            for (var depth = 0; current != null && depth < 6; depth++, current = current.InnerException)
            {
                if (depth > 0) text.Append(" <- ");
                text.Append(current.GetType().Name).Append(": ").Append(current.Message);
            }
            return text.ToString();
        }
    }
}
