using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using UnityEngine;
using Pylon.ImGui;
using ImGuiApi = Pylon.ImGui.ImGui;
using Vector2 = System.Numerics.Vector2;
using Vector4 = System.Numerics.Vector4;

namespace Pylon.ModLoader.UI
{
    /// <summary>
    /// ImGui 覆盖层的驱动与菜单骨架：Terraria 风格的标题栏 + 左侧导航 + 右侧 Tab 内容，
    /// 并在左上角保留可拖动的悬浮球（点击可收起 / 展开菜单）。布局尺寸对照 terraria-ui-preview.html。
    /// </summary>
    internal static class ImGuiOverlay
    {
        private const string MenuTitle = "Pylon Mod";

        private const float DesignWindowWidth = 960f;
        private const float DesignWindowHeight = 760f;
        private const float DesignWindowMinWidth = 520f;
        private const float DesignWindowMinHeight = 320f;
        private const float DesignNavWidth = 180f;
        private const float WindowWidthRatio = 0.72f;
        private const float WindowHeightRatio = 0.78f;
        private const float WindowCenterYRatio = 0.47f;
        private const float NavWidthRatio = 0.42f;
        private const float MinVisibleWindowHeight = 40f;
        private const float DesignTitleBarHeight = 34f;
        private const float DesignBallSize = 40f;
        private const float DesignBallMargin = 8f;
        private const float DesignBorder = 2f;
        private const float DesignBevel = 3f;
        private const float DesignTitleButton = 26f;
        private const float DesignTitleButtonInset = 6f;
        private const float DesignTitleIconInset = 8f;
        private const float DesignNavPaddingX = 8f;
        private const float DesignNavPaddingY = 10f;
        private const float DesignNavItemGap = 4f;
        private const float DesignContentPaddingX = 24f;
        private const float DesignContentPaddingY = 16f;
        private const float DesignContentRightGap = 12f;
        private const float NavAnimationSeconds = 0.075f;
        private const float BallDragThreshold = 3f;

        private static bool _started;
        private static bool _viewReady;
        private static bool _menuOpen = true;
        private static int _activeTab;
        private static bool _frameOpen;

        private static bool _layoutReady;
        private static bool _windowDragging;
        private static Vector2 _windowPos;
        private static Vector2 _windowGrabOffset;

        private static bool _navExpanded = true;
        private static float _navWidth = -1f;
        private static bool _contentScrollReset;

        private static bool _ballPlaced;
        private static Vector2 _ballPos;
        private static Vector2 _ballPressPos;
        private static Vector2 _ballGrabOffset;
        private static bool _ballDragMoved;

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
            _frameOpen = false;
            _layoutReady = false;
            _windowDragging = false;
            _navExpanded = true;
            _navWidth = -1f;
            _contentScrollReset = false;
            _ballPlaced = false;
            _ballDragMoved = false;
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

                // 缩放必须赶在 NewFrame 之前应用：NewFrame 会读取 FontGlobalScale 计算本帧字体，
                // 迟一步就变成"样式放大、文字没放大"错位一帧，设到 1.5× 时看起来像完全没缩放。
                ImGuiContext.ApplyScale(UiSettings.Scale);
                ImGuiApi.NewFrame();
                _frameOpen = true;
                ImGuiTouchRouter.BeginFrame();
                ImGuiInputText.TickKeyboard();
                ImGuiInput.Feed();
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
            if (_activeTab < 0 || _activeTab > tabs.Count)
            {
                _activeTab = 0;
                _contentScrollReset = true;
            }

            if (_menuOpen) DrawMenu(tabs);
            else _windowDragging = false;
            DrawBall();
        }

        private static void DrawMenu(IReadOnlyList<Pylon.Mod.IModImGuiTab> tabs)
        {
            var (displayWidth, displayHeight) = ImGuiNativeApi.DisplaySize;
            if (displayWidth <= 0 || displayHeight <= 0) return;

            var size = WindowSize(displayWidth, displayHeight);
            EnsureWindowPosition(displayWidth, displayHeight, size);

            TerrariaTheme.DropShadow(ImGuiApi.GetBackgroundDrawList(), _windowPos, _windowPos + size);

            ImGuiApi.SetNextWindowPos(_windowPos, ImGuiCond.Always);
            ImGuiApi.SetNextWindowSize(size, ImGuiCond.Always);
            TerrariaTheme.PushVec2(ImGuiStyleVar.WindowPadding, Vector2.Zero);

            var open = true;
            const ImGuiWindowFlags flags =
                ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse |
                ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBackground |
                ImGuiWindowFlags.NoBringToFrontOnFocus;

            if (ImGuiApi.Begin(MenuTitle, ref open, flags))
                DrawMenuBody(tabs, size);

            ImGuiApi.End();
            ImGuiApi.PopStyleVar(2);

            if (!open) _menuOpen = false;
        }

        private static void DrawMenuBody(IReadOnlyList<Pylon.Mod.IModImGuiTab> tabs, Vector2 size)
        {
            var winPos = ImGuiApi.GetWindowPos();
            var winSize = ImGuiApi.GetWindowSize();
            var winMax = winPos + winSize;
            var border = TerrariaTheme.Px(DesignBorder);
            var titleBarHeight = border + TerrariaTheme.Px(DesignTitleBarHeight);
            var dl = ImGuiApi.GetWindowDrawList();

            TerrariaTheme.Raised(dl, winPos, winMax,
                TerrariaTheme.PanelTop, TerrariaTheme.PanelMid, TerrariaTheme.PanelBottom,
                border, TerrariaTheme.Px(DesignBevel));

            DrawTitleBar(dl, winPos, winMax, border, titleBarHeight);

            var navTarget = _navExpanded
                ? MathF.Min(TerrariaTheme.Px(DesignNavWidth), winSize.X * NavWidthRatio)
                : 0f;
            AnimateNav(navTarget);

            var contentTop = winPos.Y + titleBarHeight;
            var contentBottom = winMax.Y - border;
            var contentRight = winMax.X - border - TerrariaTheme.Px(DesignContentRightGap);
            var showNav = _navWidth > 0.5f;
            var contentLeft = winPos.X + border + (showNav ? _navWidth : 0f);

            if (showNav)
                DrawNavPanel(dl, winPos.X + border, winPos.X + border + _navWidth,
                    contentTop, contentBottom, border);

            ImGuiApi.SetCursorScreenPos(new Vector2(winPos.X + border, contentTop));
            if (showNav) DrawNav(tabs, _navWidth - border, contentBottom - contentTop);

            ImGuiApi.SetCursorScreenPos(new Vector2(contentLeft, contentTop));
            DrawContent(tabs, contentRight - contentLeft, contentBottom - contentTop);

            DrawWindowFrame(dl, winPos, winMax, border);
            RegisterHitRect(winPos, winSize);
        }

        private static Vector2 WindowSize(float displayWidth, float displayHeight)
        {
            var width = MathF.Min(displayWidth * WindowWidthRatio, TerrariaTheme.Px(DesignWindowWidth));
            var height = MathF.Min(displayHeight * WindowHeightRatio, TerrariaTheme.Px(DesignWindowHeight));
            width = MathF.Max(width, MathF.Min(displayWidth, TerrariaTheme.Px(DesignWindowMinWidth)));
            height = MathF.Max(height, MathF.Min(displayHeight, TerrariaTheme.Px(DesignWindowMinHeight)));
            return new Vector2(MathF.Min(width, displayWidth), MathF.Min(height, displayHeight));
        }

        private static void EnsureWindowPosition(float displayWidth, float displayHeight, Vector2 size)
        {
            if (!_layoutReady)
            {
                _layoutReady = true;
                _windowPos = new Vector2((displayWidth - size.X) * 0.5f,
                    displayHeight * WindowCenterYRatio - size.Y * 0.5f);
            }

            var halfWidth = size.X * 0.5f;
            var maxX = MathF.Max(0f, displayWidth - size.X);
            var maxY = MathF.Max(0f, displayHeight - TerrariaTheme.Px(MinVisibleWindowHeight));
            _windowPos = new Vector2(
                Math.Clamp(_windowPos.X, -halfWidth, maxX),
                Math.Clamp(_windowPos.Y, 0f, maxY));
        }

        private static void AnimateNav(float target)
        {
            if (_navWidth < 0f)
            {
                _navWidth = target;
                return;
            }

            if (MathF.Abs(target - _navWidth) < 0.5f)
            {
                _navWidth = target;
                return;
            }

            var dt = MathF.Max(ImGuiApi.GetIO().DeltaTime, 1f / 240f);
            var t = 1f - MathF.Exp(-dt / NavAnimationSeconds);
            _navWidth += (target - _navWidth) * t;
        }

        private static void DrawTitleBar(ImDrawListPtr dl, Vector2 winPos, Vector2 winMax, float border, float height)
        {
            var barMin = new Vector2(winPos.X + border, winPos.Y + border);
            var barMax = new Vector2(winMax.X - border, winPos.Y + height);
            TerrariaTheme.Gradient(dl, barMin, barMax, TerrariaTheme.TitleTop, TerrariaTheme.TitleBottom);
            TerrariaTheme.Fill(dl, barMin, new Vector2(barMax.X, barMin.Y + TerrariaTheme.Px(2f)),
                TerrariaTheme.Alpha(TerrariaTheme.TextMain, 0.16f));
            TerrariaTheme.Fill(dl, new Vector2(barMin.X, barMax.Y - border), barMax, TerrariaTheme.Border);

            var buttonSize = TerrariaTheme.Px(DesignTitleButton);
            var centerY = winPos.Y + border + (height - border) * 0.5f;
            var buttonY = centerY - buttonSize * 0.5f;
            var foldPos = new Vector2(winPos.X + border + TerrariaTheme.Px(DesignTitleButtonInset), buttonY);
            var closePos = new Vector2(winMax.X - border - TerrariaTheme.Px(DesignTitleButtonInset) - buttonSize, buttonY);
            var foldRectMax = foldPos + new Vector2(buttonSize, buttonSize);
            var closeRectMax = closePos + new Vector2(buttonSize, buttonSize);

            HandleWindowDrag(winPos, winMax, height, foldPos, foldRectMax, closePos, closeRectMax);

            TerrariaTheme.TextCenteredSpaced(dl, new Vector2(winPos.X, winPos.Y + border),
                new Vector2(winMax.X, winPos.Y + height), TerrariaTheme.Gold, MenuTitle,
                TerrariaTheme.Px(2f), 1.25f);

            if (TitleButton("##fold", foldPos, new Vector2(buttonSize), _navExpanded ? TitleIcon.FoldLeft : TitleIcon.FoldRight))
                _navExpanded = !_navExpanded;

            if (TitleButton("##close", closePos, new Vector2(buttonSize), TitleIcon.Close))
                _menuOpen = false;
        }

        private static void HandleWindowDrag(Vector2 winPos, Vector2 winMax, float titleBarHeight,
                                             Vector2 foldMin, Vector2 foldMax, Vector2 closeMin, Vector2 closeMax)
        {
            var mouse = ImGuiApi.GetMousePos();
            var titleMin = new Vector2(winPos.X, winPos.Y);
            var titleMax = new Vector2(winMax.X, winPos.Y + titleBarHeight);

            if (!_windowDragging && ImGuiApi.IsMouseClicked(ImGuiMouseButton.Left) &&
                ImGuiApi.IsWindowHovered() && InRect(mouse, titleMin, titleMax) &&
                !InRect(mouse, foldMin, foldMax) && !InRect(mouse, closeMin, closeMax))
            {
                _windowDragging = true;
                _windowGrabOffset = mouse - winPos;
            }

            if (!_windowDragging) return;
            if (!ImGuiApi.IsMouseDown(ImGuiMouseButton.Left))
            {
                _windowDragging = false;
                return;
            }
            _windowPos = mouse - _windowGrabOffset;
        }

        private static bool TitleButton(string id, Vector2 pos, Vector2 size, TitleIcon icon)
        {
            ImGuiApi.SetCursorScreenPos(pos);
            var clicked = ImGuiApi.InvisibleButton(id, size, ImGuiButtonFlags.EnableNav);
            var hovered = ImGuiApi.IsItemHovered() || ImGuiApi.IsItemFocused();
            var held = ImGuiApi.IsItemActive();
            var min = ImGuiApi.GetItemRectMin();
            var max = ImGuiApi.GetItemRectMax();
            var dl = ImGuiApi.GetWindowDrawList();

            TerrariaTheme.ButtonGradient(hovered, held, out var top, out var mid, out var bottom);
            TerrariaTheme.Raised(dl, min, max, top, mid, bottom,
                TerrariaTheme.Px(DesignBorder), TerrariaTheme.Px(DesignBorder));

            var color = hovered || held ? TerrariaTheme.GoldBright : TerrariaTheme.TextMain;
            DrawTitleIcon(dl, min, max, icon, color);
            return clicked;
        }

        private static void DrawTitleIcon(ImDrawListPtr dl, Vector2 min, Vector2 max, TitleIcon icon, Vector4 color)
        {
            var border = TerrariaTheme.U32(TerrariaTheme.Border);
            if (icon == TitleIcon.Close)
            {
                var inset = TerrariaTheme.Px(DesignTitleIconInset);
                var a = min + new Vector2(inset, inset);
                var b = max - new Vector2(inset, inset);
                var c = new Vector2(b.X, a.Y);
                var d = new Vector2(a.X, b.Y);
                var thick = TerrariaTheme.Px(2f);
                dl.AddLine(a, b, border, thick + TerrariaTheme.Px(2f));
                dl.AddLine(c, d, border, thick + TerrariaTheme.Px(2f));
                dl.AddLine(a, b, TerrariaTheme.U32(color), thick);
                dl.AddLine(c, d, TerrariaTheme.U32(color), thick);
                return;
            }

            var left = icon == TitleIcon.FoldLeft;
            var center = (min + max) * 0.5f;
            var half = TerrariaTheme.Px(6f);
            var tipX = center.X + (left ? -half * 0.55f : half * 0.55f);
            var baseX = center.X + (left ? half * 0.55f : -half * 0.55f);
            var top = center.Y - half;
            var bottom = center.Y + half;
            var outline = TerrariaTheme.Px(2f);

            dl.AddTriangleFilled(new Vector2(tipX, center.Y), new Vector2(baseX, top + outline),
                new Vector2(baseX, bottom - outline), border);
            dl.AddTriangleFilled(new Vector2(tipX + (left ? TerrariaTheme.Px(2f) : -TerrariaTheme.Px(2f)), center.Y),
                new Vector2(baseX, top + TerrariaTheme.Px(3f)),
                new Vector2(baseX, bottom - TerrariaTheme.Px(3f)), TerrariaTheme.U32(color));
        }

        private static void DrawNavPanel(ImDrawListPtr dl, float navLeft, float navRight,
                                         float top, float bottom, float border)
        {
            TerrariaTheme.Gradient3(dl, new Vector2(navLeft, top), new Vector2(navRight, bottom),
                TerrariaTheme.NavTop, TerrariaTheme.NavMid, TerrariaTheme.NavBottom);
            TerrariaTheme.Fill(dl, new Vector2(navRight - border, top), new Vector2(navRight, bottom),
                TerrariaTheme.Border);
        }

        private static void DrawNav(IReadOnlyList<Pylon.Mod.IModImGuiTab> tabs, float width, float height)
        {
            TerrariaTheme.PushVec2(ImGuiStyleVar.WindowPadding,
                new Vector2(TerrariaTheme.Px(DesignNavPaddingX), TerrariaTheme.Px(DesignNavPaddingY)));
            ImGuiApi.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 0f);
            TerrariaTheme.PushVec2(ImGuiStyleVar.ItemSpacing, new Vector2(0f, TerrariaTheme.Px(DesignNavItemGap)));

            var childSize = new Vector2(MathF.Max(width, 1f), MathF.Max(height, 1f));
            if (ImGuiApi.BeginChild("##pylonNav", childSize))
            {
                if (TerrariaUI.NavButton("设置", _activeTab == 0, "##navSettings"))
                    ActivateTab(0);

                for (var i = 0; i < tabs.Count; i++)
                {
                    if (TerrariaUI.NavButton(SafeTitle(tabs[i]), _activeTab == i + 1, "##nav" + i))
                        ActivateTab(i + 1);
                }
            }
            ImGuiApi.EndChild();

            ImGuiApi.PopStyleVar(5);
        }

        private static void DrawContent(IReadOnlyList<Pylon.Mod.IModImGuiTab> tabs, float width, float height)
        {
            TerrariaTheme.PushVec2(ImGuiStyleVar.WindowPadding,
                new Vector2(TerrariaTheme.Px(DesignContentPaddingX), TerrariaTheme.Px(DesignContentPaddingY)));
            ImGuiApi.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 0f);

            var childSize = new Vector2(MathF.Max(width, 1f), MathF.Max(height, 1f));
            if (ImGuiApi.BeginChild("##pylonContent", childSize))
            {
                if (_contentScrollReset)
                {
                    _contentScrollReset = false;
                    ImGuiApi.SetScrollY(0f);
                }

                if (_activeTab == 0) SettingsPage.Draw();
                else DrawModTab(tabs[_activeTab - 1]);
            }
            ImGuiApi.EndChild();

            ImGuiApi.PopStyleVar(3);
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
            TerrariaUI.SeparatorText("Mod Tab 无法加载");
            TerrariaUI.Text($"{SafeTitle(tab)} 与当前 Pylon.ImGui 不兼容。");
            TerrariaUI.Text("请使用当前版本的 Pylon.ImGui 重新编译该 Mod。");
            if (error != null) TerrariaUI.DimText($"{error.GetType().Name}: {error.Message}");
        }

        private static string SafeTitle(Pylon.Mod.IModImGuiTab tab)
        {
            try { return string.IsNullOrEmpty(tab.Title) ? "Mod" : tab.Title; }
            catch { return "Mod"; }
        }

        private static void DrawWindowFrame(ImDrawListPtr dl, Vector2 winPos, Vector2 winMax, float border)
        {
            var inset = TerrariaTheme.Px(DesignBevel);
            TerrariaTheme.Outline(dl, winPos + new Vector2(inset), winMax - new Vector2(inset),
                TerrariaTheme.Alpha(TerrariaTheme.Border, 0.20f), TerrariaTheme.Px(1f));
            TerrariaTheme.Bevel(dl, winPos, winMax, border, TerrariaTheme.Px(DesignBevel));
            TerrariaTheme.Outline(dl, winPos, winMax, TerrariaTheme.Border, border);
        }

        private static void ActivateTab(int tab)
        {
            if (_activeTab == tab) return;
            _activeTab = tab;
            _contentScrollReset = true;
        }

        private static void DrawBall()
        {
            var (displayWidth, displayHeight) = ImGuiNativeApi.DisplaySize;
            if (displayWidth <= 0 || displayHeight <= 0) return;

            var size = new Vector2(TerrariaTheme.Px(DesignBallSize));
            if (!_ballPlaced)
            {
                _ballPlaced = true;
                // 默认停靠屏幕左侧中间，避开左上角。
                _ballPos = new Vector2(TerrariaTheme.Px(DesignBallMargin),
                    (displayHeight - size.Y) * 0.5f);
            }
            ClampBall(displayWidth, displayHeight, size);

            ImGuiApi.SetNextWindowPos(_ballPos, ImGuiCond.Always);
            ImGuiApi.SetNextWindowSize(size, ImGuiCond.Always);
            TerrariaTheme.PushVec2(ImGuiStyleVar.WindowPadding, Vector2.Zero);

            var open = true;
            const ImGuiWindowFlags flags =
                ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoFocusOnAppearing |
                ImGuiWindowFlags.NoBringToFrontOnFocus;

            if (ImGuiApi.Begin("##pylonBall", ref open, flags))
            {
                var clicked = ImGuiApi.InvisibleButton("##ball", size,
                    ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.EnableNav);
                HandleBallInput(clicked, size, displayWidth, displayHeight);

                var min = ImGuiApi.GetItemRectMin();
                var max = ImGuiApi.GetItemRectMax();
                DrawBallSurface(ImGuiApi.GetWindowDrawList(), min, max,
                    ImGuiApi.IsItemHovered() || ImGuiApi.IsItemActive());

                RegisterHitRect(ImGuiApi.GetWindowPos(), ImGuiApi.GetWindowSize());
            }
            ImGuiApi.End();
            ImGuiApi.PopStyleVar(2);
        }

        private static void HandleBallInput(bool clicked, Vector2 size, float displayWidth, float displayHeight)
        {
            var mouse = ImGuiApi.GetMousePos();
            if (ImGuiApi.IsItemActivated())
            {
                _ballPressPos = mouse;
                _ballGrabOffset = mouse - _ballPos;
                _ballDragMoved = false;
            }

            if (ImGuiApi.IsItemActive() && ImGuiApi.IsMouseDown(ImGuiMouseButton.Left))
            {
                if (!_ballDragMoved && (mouse - _ballPressPos).Length() > TerrariaTheme.Px(BallDragThreshold))
                    _ballDragMoved = true;

                if (_ballDragMoved)
                {
                    _ballPos = mouse - _ballGrabOffset;
                    ClampBall(displayWidth, displayHeight, size);
                }
            }

            if (!clicked) return;
            if (!_ballDragMoved) _menuOpen = !_menuOpen;
            _ballDragMoved = false;
        }

        private static void ClampBall(float displayWidth, float displayHeight, Vector2 size)
        {
            _ballPos = new Vector2(
                Math.Clamp(_ballPos.X, 0f, MathF.Max(0f, displayWidth - size.X)),
                Math.Clamp(_ballPos.Y, 0f, MathF.Max(0f, displayHeight - size.Y)));
        }

        private static void DrawBallSurface(ImDrawListPtr dl, Vector2 min, Vector2 max, bool hovered)
        {
            TerrariaTheme.Fill(dl, min, max, TerrariaTheme.Alpha(TerrariaTheme.Hex(0x1A2036), 0.92f));
            if (hovered)
                TerrariaTheme.Fill(dl, min, max, TerrariaTheme.Alpha(TerrariaTheme.Gold, 0.10f));

            TerrariaTheme.Bevel(dl, min, max, TerrariaTheme.Px(DesignBorder), TerrariaTheme.Px(DesignBorder));
            TerrariaTheme.Outline(dl, min, max, TerrariaTheme.Border, TerrariaTheme.Px(DesignBorder));
            if (hovered)
            {
                var inset = TerrariaTheme.Px(2f);
                TerrariaTheme.Outline(dl, min + new Vector2(inset), max - new Vector2(inset),
                    TerrariaTheme.Alpha(TerrariaTheme.Gold, 0.60f), TerrariaTheme.Px(1f));
            }

            var color = hovered ? TerrariaTheme.GoldBright : TerrariaTheme.TextMain;
            var left = min.X + TerrariaTheme.Px(10f);
            var right = max.X - TerrariaTheme.Px(10f);
            var thickness = TerrariaTheme.Px(2f);
            for (var i = 0; i < 3; i++)
            {
                var y = min.Y + TerrariaTheme.Px(12f + i * 7f);
                dl.AddLine(new Vector2(left, y + TerrariaTheme.Px(1f)),
                    new Vector2(right, y + TerrariaTheme.Px(1f)), TerrariaTheme.U32(TerrariaTheme.Border),
                    thickness + TerrariaTheme.Px(1f));
                dl.AddLine(new Vector2(left, y), new Vector2(right, y), TerrariaTheme.U32(color), thickness);
            }
        }

        private static void RegisterHitRect(Vector2 pos, Vector2 size)
        {
            float scrW = Screen.width, scrH = Screen.height;
            var (dw, dh) = ImGuiNativeApi.DisplaySize;
            if (dw <= 0 || dh <= 0 || scrW <= 0 || scrH <= 0) return;
            ImGuiTouchRouter.AddWindowRect(pos.X, pos.Y, size.X, size.Y, scrW / dw, scrH / dh, scrH);
        }

        private static bool InRect(Vector2 point, Vector2 min, Vector2 max)
            => point.X >= min.X && point.X <= max.X && point.Y >= min.Y && point.Y <= max.Y;

        private enum TitleIcon
        {
            Close,
            FoldLeft,
            FoldRight,
        }
    }
}
