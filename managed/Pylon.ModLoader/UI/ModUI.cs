using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Pylon.Mod;
using ImGuiApi = Pylon.ImGui.ImGui;
using ImVec2 = Pylon.ImGui.ImVec2;
using ImVec4 = Pylon.ImGui.ImVec4;

namespace Pylon.ModLoader.UI
{
    /// <summary>Main.spriteBatch 直绘，面板与文字同帧出现；只留一个更新钩子 + 一个绘制钩子。</summary>
    public static class ModUI
    {
        private static bool _hooked;
        private static ModLogger _logger;
        private static readonly List<ModPanel> Panels = new();

        public static bool Visible { get; set; } = true;

        internal static ModLogger Logger => _logger;

        /// <summary>晚阶段调用</summary>
        internal static void EnsureInitialized(Harmony harmony, ModLogger logger)
        {
            if (_hooked) return;
            _logger = logger;
            _hooked = true;
            InstallHooks(harmony, logger);
            ModConfigMenu.Install(harmony, logger, forMainMenu: true);
            TouchButtonHooks.RequestInstall(harmony, logger);
            ImGuiTouchGuard.RequestInstall(harmony, logger);
        }

        internal static void Register(ModPanel panel)
        {
            if (!Panels.Contains(panel)) Panels.Add(panel);
        }

        /// <summary>Mod 注册 ImGui 菜单 Tab（Mod.RegisterImGuiTab 转到这里，进全局注册表）。</summary>
        internal static void RegisterImGuiTab(IModImGuiTab tab)
            => Pylon.Mod.ImGuiTabRegistry.Register(tab);

        internal static void Update(GameTime gameTime)
        {
            ModConfigMenu.TryInstallPending();
            TouchButtonHooks.TryInstallPending();
            try
            {
                if (ImGuiDriver.Visible)
                {
                    ImGuiDriver.EnsureStarted();
                    ImGuiTouchGuard.BeginFrame();   // 本帧命中区清空；窗口绘制时重新注册
                    ImGuiDriver.FeedTouchFromUnity();
                    ImGuiDriver.UpdateKeyboard();   // WantTextInput → 弹 Unity 键盘；文本增量喂回
                    ImGuiDriver.BeginFrame();
                    ImGuiDriver.EnsureThemeApplied();   // 上下文就绪后应用一次现代暗色主题
                    DrawFrameworkWindows();   // Chrome 内部自带特效驱动（极光/彩雨/星光/浮尘/粒子）
                    ImGuiDriver.EndFrame();
                }
            }
            catch (Exception e)
            {
                Logger?.Warn("ImGui 帧异常：" + Flatten(e));
            }
        }

        private static void DrawFrameworkWindows()
        {
            if (!_menuOpen)
            {
                DrawMenuBall();   // 折叠态：悬浮球（可拖动，点击恢复）
                return;
            }
            // 默认大小随 UI 比例一起长，否则调大比例后字变大、内容被裁；仅比例变化时重算一次，不逐帧强制
            var uiScale = UiSettings.Scale;
            if (!_menuSizeSet || Math.Abs(_menuSizeScale - uiScale) > 0.001f)
            {
                _menuSizeSet = true;
                _menuSizeScale = uiScale;
                var (w, h) = ImGuiDriver.DisplaySize;
                var width = Math.Min(w * 0.62f * uiScale, Math.Min(920f * uiScale, w * 0.92f));
                var height = Math.Min(h * 0.78f * uiScale, Math.Min(800f * uiScale, h * 0.92f));
                ImGuiApi.SetNextWindowSize(width, height, 0);
            }

            if (ImGuiApi.Begin("Pylon Mod", ref _menuOpen,
                               (int)(Pylon.ImGui.ImGuiWindowFlags.NoTitleBar |
                                     Pylon.ImGui.ImGuiWindowFlags.NoMove |
                                     Pylon.ImGui.ImGuiWindowFlags.NoCollapse |
                                     Pylon.ImGui.ImGuiWindowFlags.NoScrollbar |
                                     Pylon.ImGui.ImGuiWindowFlags.NoScrollWithMouse |
                                     Pylon.ImGui.ImGuiWindowFlags.NoSavedSettings)))
            {
                //背景特效 + 头部 + 侧边栏
                var modTabs = Pylon.Mod.ImGuiTabRegistry.All;
                if (_activeTab >= modTabs.Count + 1) _activeTab = 0;
                var navs = new ChromeNav[modTabs.Count + 1];
                navs[0] = new ChromeNav("设置", "设置", 7);
                for (int i = 0; i < modTabs.Count; i++)
                    navs[i + 1] = new ChromeNav(modTabs[i].Title, modTabs[i].Title, 5);
                int clicked = Chrome.Draw(_chrome, "Pylon Mod", "设置", navs);
                if (clicked >= 0) _activeTab = clicked;
                if (!_chrome.Open) _menuOpen = false;

                Chrome.BeginContent(_chrome);
                if (_activeTab == 0) SettingsPage.Draw();
                else modTabs[_activeTab - 1].Draw();
                Chrome.EndContent();

                // End 前取本窗口坐标（End 后拿不到）；注册为触摸拦截命中区，附覆盖层→屏幕坐标换算比例
                var pos = ImGuiApi.GetWindowPos();
                var size = ImGuiApi.GetWindowSize();
                var (dispW, dispH) = ImGuiDriver.DisplaySize;
                float scrW = UnityEngine.Screen.width;
                float scrH = UnityEngine.Screen.height;
                float kx = scrW / dispW;
                float ky = scrH / dispH;
                ImGuiTouchGuard.AddWindowRect(
                    pos.X, pos.Y, size.X, size.Y,
                    kx, ky, scrH);
            }
            ImGuiApi.End();
        }

        /// <summary>折叠态悬浮球：小球窗口（无装饰、自适应、可拖动），点击恢复菜单。</summary>
        private static void DrawMenuBall()
        {
            // 第一个使用时的默认位置（屏幕中上部偏右）
            if (_ballFirstUse)
            {
                _ballFirstUse = false;
                var (w, h) = ImGuiDriver.DisplaySize;
                ImGuiApi.SetNextWindowPos(w * 0.86f, h * 0.12f, 0);
            }
            // 悬浮球窗口不用 _menuOpen（折叠态它是 false）
            var ballOpen = true;
            ImGuiApi.SetNextWindowBgAlpha(0f);   // 窗口背景透明
            if (ImGuiApi.Begin("##menuBallWin", ref ballOpen,
                               (int)(Pylon.ImGui.ImGuiWindowFlags.NoDecoration |
                                     Pylon.ImGui.ImGuiWindowFlags.NoSavedSettings |
                                     Pylon.ImGui.ImGuiWindowFlags.AlwaysAutoResize)))
            {
                if (Chrome.DrawCollapsedBall(_chrome, "Mod"))
                {
                    _chrome.Open = true;
                    _menuOpen = true;
                }
            }
            ImGuiApi.End();
        }

        private static int _activeTab = 0;
        private static bool _ballFirstUse = true;   // 悬浮球还没放过的默认位置标记
        private static bool _menuSizeSet;           // 菜单默认大小是否已设置
        private static float _menuSizeScale;        // 上次设置菜单大小时用的 UI 比例

        // Chrome 窗口状态（背景特效/侧栏/悬浮球动画都在里面）
        private static readonly ChromeState _chrome = new();

        private static bool _menuOpen = true;

        internal static void Draw(SpriteBatch spriteBatch, GameTime gameTime)
        {
            if (!Visible) return;
            foreach (var panel in Panels) panel.Draw(spriteBatch);
        }

        private static void InstallHooks(Harmony harmony, ModLogger logger)
        {
            Patch(harmony, logger, "UpdateUIStates", true);
            var any = false;
            foreach (var candidate in new[]
                     {
                         "DrawInterface_15_InvasionProgressBars", "DrawInterface_Resources_Buffs",
                         "DrawInterface_Resources_Breath", "DrawInterface_Resources_GolfPower",
                         "DrawInterface_Resources_ClearBuffs",
                     })
            {
                if (PatchNoArgs(harmony, logger, candidate)) { any = true; break; }
            }
            if (!any) logger.Error("没找到可用的 UI 绘制点（零参 DrawInterface_*）");
        }

        /// <summary>按名字找 Main 上"恰好一个 GameTime 参数"的方法并挂 postfix。</summary>
        private static bool Patch(Harmony harmony, ModLogger logger, string name, bool required)
        {
            var target = FindGameTimeMethod(name);
            if (target == null)
            {
                if (required) logger.Error($"找不到 Main.{name}(GameTime)");
                return false;
            }

            try
            {
                harmony.Patch(target, postfix: new HarmonyMethod(
                    typeof(ModUI).GetMethod(nameof(UpdatePostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                logger.Info($"UI 驱动已挂：Main.{target.Name}(GameTime)");
                return true;
            }
            catch (Exception e)
            {
                logger.Error($"挂 Main.{name} 失败：{Flatten(e)}");
                return false;
            }
        }

        /// <summary>挂零参绘制方法（这些方法在游戏的 UI 批处理内部执行，能直接画）。</summary>
        private static bool PatchNoArgs(Harmony harmony, ModLogger logger, string name)
        {
            MethodInfo target = null;
            foreach (var candidate in typeof(Main).GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                                              BindingFlags.Static | BindingFlags.Instance))
            {
                if (candidate.Name == name && candidate.GetParameters().Length == 0) { target = candidate; break; }
            }
            if (target == null) return false;

            try
            {
                harmony.Patch(target, postfix: new HarmonyMethod(
                    typeof(ModUI).GetMethod(nameof(DrawNoArgsPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                logger.Info($"UI 绘制点已挂：Main.{target.Name}()");
                return true;
            }
            catch (Exception e)
            {
                logger.Error($"挂 {name} 失败：{Flatten(e)}");
                return false;
            }
        }

        private static bool _drawLogged;

        private static void DrawNoArgsPostfix()
        {
            if (!_drawLogged)
            {
                _drawLogged = true;
                _logger?.Info("UI 绘制点首次执行：面板开始绘制");
            }
            Draw(Main.spriteBatch, null);
        }

        private static MethodInfo FindGameTimeMethod(string name)
        {
            foreach (var candidate in typeof(Main).GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                                              BindingFlags.Static | BindingFlags.Instance))
            {
                if (candidate.Name != name) continue;
                var parameters = candidate.GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType.Name == "GameTime") return candidate;
            }
            return null;
        }

        private static void UpdatePostfix(GameTime gameTime) => Update(gameTime);

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