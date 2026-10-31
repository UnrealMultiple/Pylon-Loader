using System;
using System.Collections.Generic;
using UnityEngine;
using Pylon.ImGui;
using ImGuiApi = Pylon.ImGui.ImGui;
using Vector2 = System.Numerics.Vector2;

namespace Pylon.ModLoader.UI
{
    /// <summary>
    /// ImGui 覆盖层的唯一驱动与界面骨架：上下文/字体/样式/缩放、每帧取帧-绘制-呈现、
    /// 菜单窗口（左侧导航 + 右侧内容）与折叠态悬浮球。Mod 的 Tab 通过 IModImGuiTab 注册进来。
    /// </summary>
    internal static class ImGuiOverlay
    {
        private const string MenuTitle = "Pylon Mod";

        private static bool _started;
        private static bool _viewReady;
        private static bool _menuOpen = true;   // 首帧就把菜单显示出来，否则只有一个 36x36 的小球
        private static int _activeTab;
        private static bool _sizeInit;
        private static int _frames;
        private static bool _frameErrorLogged;

        public static bool Visible { get; set; } = true;

        internal static void EnsureStarted()
        {
            if (_started) return;
            _started = true;
            try
            {
                if (!ImGuiContext.Create())
                {
                    _started = false;
                    return;
                }
                _viewReady = ImGuiPlatform.EnsureView();
                ImGuiTouchRouter.RequestInstall();
                ModUI.Logger?.Info("ImGui 覆盖层已初始化");
            }
            catch (Exception e)
            {
                _started = false;
                ModUI.Logger?.Error("ImGui 初始化异常：" + ModUI.Flatten(e));
            }
        }

        internal static void Shutdown()
        {
            if (!_started) return;
            _started = false;
            try
            {
                ImGuiPlatform.Detach();
                ImGuiContext.Destroy();
            }
            catch { }
        }

        /// <summary>每帧驱动（游戏线程）。</summary>
        internal static void Update()
        {
            if (++_frames == 1 || _frames % 300 == 0)
                ModUI.Logger?.Info($"覆盖层驱动 第 {_frames} 帧（visible={Visible} view={_viewReady} attached={ImGuiPlatform.Attached}）");
            if (!Visible) return;
            EnsureStarted();
            if (!_started) return;

            try
            {
                if (!_viewReady) _viewReady = ImGuiPlatform.EnsureView();
                if (!ImGuiPlatform.TryAttachSurface())
                {
                    ImGuiPlatform.LogPendingState();   // Surface 还没好
                    return;
                }
                if (ImGuiNativeApi.BeginFrame() == 0) return;

                // ImGui 要求 DisplaySize 非负、DeltaTime 为正，否则 NewFrame 直接断言中止进程。
                var io = ImGuiApi.GetIO();
                var (pw, ph) = ImGuiNativeApi.DisplaySize;
                io.DisplaySize = new Vector2(pw, ph);
                io.DeltaTime = 1f / 60f;

                ImGuiApi.NewFrame();
                ImGuiTouchRouter.BeginFrame();
                ImGuiInputText.TickKeyboard();
                ImGuiInput.Feed();
                ImGuiContext.ApplyScale(UiSettings.Scale);
                Draw();
                ImGuiApi.Render();
                ImGuiNativeApi.EndFrame();
                ImGuiInputText.EndFrame();
            }
            catch (Exception e)
            {
                if (!_frameErrorLogged)   // 只留第一次的完整堆栈，避免刷屏
                {
                    _frameErrorLogged = true;
                    var where = (e.StackTrace ?? "").Replace("\r", "").Replace("\n", " | ");
                    ModUI.Logger?.Warn($"ImGui 帧异常：{e.GetType().Name}: {e.Message} @ {where}");
                }
            }
        }

        private static void Draw()
        {
            var tabs = Pylon.Mod.ImGuiTabRegistry.All;
            if (_activeTab >= tabs.Count + 1) _activeTab = 0;

            if (!_menuOpen)
            {
                DrawBall();
                return;
            }

            var (dw, dh) = ImGuiNativeApi.DisplaySize;
            if (dw <= 0 || dh <= 0) return;
            if (!_sizeInit)
            {
                _sizeInit = true;
                var width = Math.Min(dw * 0.72f, 960f);
                var height = Math.Min(dh * 0.78f, 760f);
                ImGuiApi.SetNextWindowSize(new Vector2(width, height), ImGuiCond.FirstUseEver);
            }

            var open = true;
            var flags = ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse;
            if (ImGuiApi.Begin(MenuTitle, ref open, flags))
            {
                DrawNav(tabs);
                ImGuiApi.SameLine();
                DrawContent(tabs);

                // End 之前取窗口坐标，注册为触摸拦截命中区
                var pos = ImGuiApi.GetWindowPos();
                var size = ImGuiApi.GetWindowSize();
                RegisterHitRect(pos, size);
            }
            ImGuiApi.End();

            if (!open) _menuOpen = false;
        }

        private static void DrawNav(IReadOnlyList<Pylon.Mod.IModImGuiTab> tabs)
        {
            ImGuiApi.BeginChild("##nav", new Vector2(180f, 0f));
            if (ImGuiApi.Selectable("设置", _activeTab == 0)) _activeTab = 0;
            for (int i = 0; i < tabs.Count; i++)
            {
                if (ImGuiApi.Selectable(tabs[i].Title, _activeTab == i + 1)) _activeTab = i + 1;
            }
            ImGuiApi.EndChild();
        }

        private static void DrawContent(IReadOnlyList<Pylon.Mod.IModImGuiTab> tabs)
        {
            ImGuiApi.BeginChild("##content", new Vector2(0f, 0f));
            if (_activeTab == 0) SettingsPage.Draw();
            else tabs[_activeTab - 1].Draw();
            ImGuiApi.EndChild();
        }

        private static void DrawBall()
        {
            ImGuiApi.SetNextWindowPos(new Vector2(0f, 0f), ImGuiCond.FirstUseEver);
            ImGuiApi.SetNextWindowBgAlpha(0.35f);
            var open = true;
            var flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings |
                        ImGuiWindowFlags.AlwaysAutoResize;
            if (ImGuiApi.Begin("##pylonBall", ref open, flags))
            {
                if (ImGuiApi.Button("☰", new Vector2(36f, 36f)))
                {
                    _menuOpen = true;
                }
                RegisterHitRect(ImGuiApi.GetWindowPos(), ImGuiApi.GetWindowSize());
            }
            ImGuiApi.End();
        }

        private static void RegisterHitRect(Vector2 pos, Vector2 size)
        {
            float scrW = Screen.width, scrH = Screen.height;
            var (dw, dh) = ImGuiNativeApi.DisplaySize;
            if (dw <= 0 || dh <= 0 || scrW <= 0 || scrH <= 0) return;
            ImGuiTouchRouter.AddWindowRect(pos.X, pos.Y, size.X, size.Y, scrW / dw, scrH / dh, scrH);
        }
    }
}
