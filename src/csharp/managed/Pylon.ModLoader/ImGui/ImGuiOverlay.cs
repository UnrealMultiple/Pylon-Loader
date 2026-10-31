using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Pylon.ImGui;
using ImGuiApi = Pylon.ImGui.ImGui;
using Vector2 = System.Numerics.Vector2;

namespace Pylon.ModLoader.UI
{
    /// <summary>ImGui 覆盖层的驱动与菜单骨架（左侧导航 + 右侧 Tab 内容）。</summary>
    internal static class ImGuiOverlay
    {
        private const string MenuTitle = "Pylon Mod";
        private const float BallSize = 36f;

        private static bool _started;
        private static bool _viewReady;
        private static bool _menuOpen = true;
        private static int _activeTab;
        private static bool _sizeInit;
        private static bool _frameOpen;

        private static readonly HashSet<string> ReportedFrameErrors = [];
        private static readonly HashSet<Pylon.Mod.IModImGuiTab> BrokenTabs = [];

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
            _viewReady = false;
            _sizeInit = false;
            _frameOpen = false;
            ReportedFrameErrors.Clear();
            BrokenTabs.Clear();
            try
            {
                ImGuiPlatform.Detach();
                ImGuiContext.Destroy();
            }
            catch { }
        }

        internal static void Update()
        {
            if (!Visible) return;
            EnsureStarted();
            if (!_started) return;

            try
            {
                if (!_viewReady) _viewReady = ImGuiPlatform.EnsureView();
                if (!ImGuiPlatform.TryAttachSurface())
                {
                    ImGuiPlatform.LogPendingState();
                    return;
                }
                if (ImGuiNativeApi.BeginFrame() == 0) return;

                var io = ImGuiApi.GetIO();
                var (pw, ph) = ImGuiNativeApi.DisplaySize;
                io.DisplaySize = new Vector2(pw, ph);
                io.DeltaTime = 1f / 60f;

                ImGuiApi.NewFrame();
                _frameOpen = true;
                ImGuiTouchRouter.BeginFrame();
                ImGuiInputText.TickKeyboard();
                ImGuiInput.Feed();
                ImGuiContext.ApplyScale(UiSettings.Scale);
                Draw();
                ImGuiApi.Render();
                _frameOpen = false;
                ImGuiNativeApi.EndFrame();
                ImGuiInputText.EndFrame();
            }
            catch (Exception e)
            {
                // 必须收尾：否则下一帧 NewFrame 会因“上一帧未结束”断言 abort。
                CloseBrokenFrame();
                ReportFrameErrorOnce(e);
            }
        }

        private static void CloseBrokenFrame()
        {
            if (_frameOpen)
            {
                _frameOpen = false;
                try { ImGuiApi.Render(); }
                catch { try { ImGuiApi.EndFrame(); } catch { } }
            }
            try { ImGuiNativeApi.EndFrame(); } catch { }
        }

        private static void ReportFrameErrorOnce(Exception e)
        {
            var key = e.GetType().FullName + "|" + e.Message;
            if (!ReportedFrameErrors.Add(key)) return;

            var where = (e.StackTrace ?? "").Replace("\r", "").Replace("\n", " | ");
            ModUI.Logger?.Warn($"ImGui 帧异常：{e.GetType().Name}: {e.Message} @ {where}");
        }

        private static void Draw()
        {
            var tabs = Pylon.Mod.ImGuiTabRegistry.All;
            if (_activeTab < 0 || _activeTab > tabs.Count) _activeTab = 0;

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
                if (ImGuiApi.Selectable(SafeTitle(tabs[i]), _activeTab == i + 1)) _activeTab = i + 1;
            }
            ImGuiApi.EndChild();
        }

        private static void DrawContent(IReadOnlyList<Pylon.Mod.IModImGuiTab> tabs)
        {
            ImGuiApi.BeginChild("##content", new Vector2(0f, 0f));
            if (_activeTab == 0) SettingsPage.Draw();
            else DrawModTab(tabs[_activeTab - 1]);
            ImGuiApi.EndChild();
        }

        private static void DrawModTab(Pylon.Mod.IModImGuiTab tab)
        {
            if (BrokenTabs.Contains(tab))
            {
                DrawTabError(tab, null);
                return;
            }

            try
            {
                tab.Draw();
            }
            catch (Exception e) when (IsTabLoadFailure(e))
            {
                // 装载失败发生在进入 Draw 之前，窗口栈没动，可以安全地画错误提示。
                BrokenTabs.Add(tab);
                ReportFrameErrorOnce(e);
                DrawTabError(tab, e);
            }
        }

        private static bool IsTabLoadFailure(Exception e) =>
            e is TypeLoadException or MissingMethodException or MissingFieldException
                or FileNotFoundException or FileLoadException or BadImageFormatException;

        private static void DrawTabError(Pylon.Mod.IModImGuiTab tab, Exception error)
        {
            ImGuiApi.SeparatorText("Mod Tab 无法加载");
            ImGuiApi.Text($"{SafeTitle(tab)} 与当前 Pylon.ImGui 不兼容。");
            ImGuiApi.Text("请使用当前版本的 Pylon.ImGui 重新编译该 Mod。");
            if (error != null) ImGuiApi.Text($"{error.GetType().Name}: {error.Message}");
        }

        private static string SafeTitle(Pylon.Mod.IModImGuiTab tab)
        {
            try { return string.IsNullOrEmpty(tab.Title) ? "Mod" : tab.Title; }
            catch { return "Mod"; }
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
                if (ImGuiApi.Button("☰", new Vector2(BallSize, BallSize)))
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
