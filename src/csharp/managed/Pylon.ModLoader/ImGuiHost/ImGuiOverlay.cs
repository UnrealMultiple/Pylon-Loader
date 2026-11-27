using System.Numerics;
using Pylon.ModLoader.UI;
using Pylon.NaviteAPI.ImGui;
using UnityEngine;
using Vector2 = System.Numerics.Vector2;
using Vector4 = System.Numerics.Vector4;

namespace Pylon.ModLoader.ImGuiHost;

/// <summary>
/// ImGui 覆盖层的驱动与菜单骨架：980×720 现代窗口、壁纸氛围层、52px 标题栏、176px 侧栏、
/// 页面滚动与交错入场、开合动画（缩回悬浮球/弹性弹出）与悬浮球。
/// </summary>
internal static class ImGuiOverlay
{
    private const string MenuTitle = "Pylon Mod";

    private const float DesignWindowWidth = 980f;
    private const float DesignWindowHeight = 720f;
    private const float DesignTitleHeight = 52f;
    private const float DesignNavWidth = 176f;
    private const float DesignButtonSize = 32f;
    private const float DesignBrandSize = 30f;
    private const float DesignNavIcon = 20f;
    private const float DesignNavItemH = 54f;
    private const float DesignBallSize = 42f;
    private const float DesignBallRounding = 13f;
    private const float DesignBallMargin = 20f;
    private const float DesignBallDragThreshold = 5f;
    private const float WindowDragThreshold = 8f;
    private const float WindowWidthRatio = 0.88f;
    private const float WindowHeightRatio = 0.84f;
    private const float ShadowAlpha = 0.16f;

    private enum WinAnim
    {
        Open,
        Closing,
        Closed,
        Opening,
    }

    private static long _frameStamp;
    private static bool _started;
    private static string? _lastPendingReason;
    private static bool _menuOpen = true;
    private static int _activeTab;
    private static float _pageShownAt = float.NegativeInfinity;
    private static bool _frameOpen;
    private static WinAnim _winAnim = WinAnim.Open;
    private static float _animStart;
    private static Vector2 _animFrom;

    private static bool _layoutReady;
    private static bool _windowDragging;
    private static bool _windowPressPending;
    private static Vector2 _windowPressPos;
    private static Vector2 _windowPos;
    private static Vector2 _windowGrabOffset;

    private static bool _navExpanded = true;
    private static float _navWidth = -1f;
    private static float _navVelocity;
    private static float _markerTop;
    private static float _markerHeight = -1f;
    private static float _markerVelTop;
    private static float _markerVelHeight;

    private static bool _ballPlaced;
    private static Vector2 _ballPos;
    private static Vector2 _ballPressPos;
    private static Vector2 _ballGrabOffset;
    private static bool _ballDragMoved;

    private static readonly Dictionary<int, string> NavIds = [];
    private static readonly Dictionary<int, string> PageIds = [];
    private static int _lastFps = -1;
    private static string? _fpsText;
    private static float _chipAnchorX = -1f;
    private static float _chipTextTop;
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
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                ModUI.Logger?.Error("未处理异常：" + e.ExceptionObject +
                    (e.IsTerminating ? "（进程将退出）" : ""));
            LogPage.Hook();
            ImGuiTouchRouter.RequestInstall();
            ModUI.Logger?.Info("ImGui 覆盖层已初始化（GLES 后端）");
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
        _lastPendingReason = null;
        _frameOpen = false;
        _layoutReady = false;
        _windowDragging = false;
        _navExpanded = true;
        _navWidth = -1f;
        _navVelocity = 0f;
        _markerHeight = -1f;
        _menuOpen = true;
        _winAnim = WinAnim.Open;
        _pageShownAt = float.NegativeInfinity;
        _ballPlaced = false;
        _ballDragMoved = false;
        _navScroll = 0f;



        UiTheme.ExtraFade = 1f;
        UiScroll.Reset();
        ModernUI.ResetTransient();
        ModOverlay.Reset();
        ReportedFrameErrors.Clear();
        BrokenTabs.Clear();
        try { ImGuiContext.Destroy(); }
        catch (Exception) { }
    }

    internal static void Update()
    {
        if (!Visible) return;
        EnsureStarted();
        if (!_started) return;

        try
        {
            if (ImGuiNativeApi.IsReady() == 0)
            {
                LogPendingState();
                return;
            }
            if (ImGuiNativeApi.BeginFrame() == 0) return;

            var io = ImGui.GetIO();
            var (pw, ph) = ImGuiNativeApi.DisplaySize;
            io.DisplaySize = new Vector2(pw, ph);
            var stamp = System.Diagnostics.Stopwatch.GetTimestamp();
            io.DeltaTime = _frameStamp == 0 ? 1f / 60f :
                Math.Clamp((float)((stamp - _frameStamp) / (double)System.Diagnostics.Stopwatch.Frequency), .001f, .1f);
            _frameStamp = stamp;
            ImGuiContext.ApplyScale(UiSettings.Scale);
            // Submit platform input before NewFrame: ImGui consumes its input
            // queue during NewFrame. Feeding it afterwards made IsMouseDown/
            // IsWindowHovered one frame behind the raw touch gesture used by
            // UiScroll, which caused missed releases and scroll ownership races.
            ImGuiTouchRouter.BeginFrame();
            ImGuiInput.Feed();
            UiScroll.BeginFrame();
            ImGui.NewFrame();
            TouchGestures.AfterNewFrame();
            _frameOpen = true;
            ImGuiContext.EnsureFontSizes();
            Motion.Tick(io.DeltaTime);
            UiTheme.Tick();
            Draw();
            ModOverlay.DrawAll();
            UiScroll.EndFrame();
            TouchGestures.EndFrame();
            ImGui.Render();
            _frameOpen = false;
            ImGuiNativeApi.EndFrame();
            ImGuiInputText.EndFrame();
            SettleTextures();
        }
        catch (Exception e)
        {
            CloseBrokenFrame();
            ReportFrameErrorOnce(e);
        }
    }

    internal static void RequestThemeSwitch(int index)
    {
        if (index == UiTheme.Current) return;
        UiTheme.Current = index;
        UiSettings.Theme = index;
        UiSettings.Save();
        ModernUI.Toast("已应用主题：" + UiTheme.Palette.Cn);
        ModUI.Logger?.Info("[UiSettings] theme → " + UiTheme.Palette.Id);
    }

    private static void CloseBrokenFrame()
    {
        if (_frameOpen)
        {
            _frameOpen = false;
            try { ImGui.Render(); }
            catch (Exception) { try { ImGui.EndFrame(); } catch (Exception) { } }
        }
        try { ImGuiNativeApi.EndFrame(); } catch (Exception) { }
    }

    private static void LogPendingState()
    {
        const string why = "内核 GLES 后端尚未在渲染线程初始化（等第一次 eglSwapBuffers）";
        if (_lastPendingReason == why) return;
        _lastPendingReason = why;
        ModUI.Logger?.Warn("覆盖层未接管：" + why);
    }

    private static void ReportFrameErrorOnce(Exception e)
    {
        var key = e.GetType().FullName + "|" + e.Message;
        if (!ReportedFrameErrors.Add(key)) return;
        var where = (e.StackTrace ?? "").Replace("\r", "").Replace("\n", " | ");
        ModUI.Logger?.Warn($"ImGui 帧异常：{e.GetType().Name}: {e.Message} @ {where}");
    }

    // 每个纹理在首次出现“待上传”时记下当时已上屏帧数；只有渲染线程又画完至少一帧
    // （该帧的快照必然带着这份像素）才回 OK。用更新次数计数是不安全的：更新频率与
    // swap 频率并不相等，提前回 OK 会让部分字形永远没有像素。
    private static readonly Dictionary<int, ulong> TexPendingSince = [];

    private static void SettleTextures()
    {
        try
        {
            var textures = ImGui.GetPlatformIO().Textures;
            var drawn = ImGuiNativeApi.DrawnFrames();
            var live = new HashSet<int>();
            for (var i = 0; i < textures.Size; i++)
            {
                var tex = textures[i];
                live.Add(tex.UniqueID);
                if (tex.Status is not (ImTextureStatus.WantCreate or ImTextureStatus.WantUpdates))
                {
                    TexPendingSince.Remove(tex.UniqueID);
                    continue;
                }
                if (!TexPendingSince.TryGetValue(tex.UniqueID, out var baseline))
                {
                    TexPendingSince[tex.UniqueID] = drawn;
                    continue;
                }
                if (drawn > baseline)
                {
                    tex.Status = ImTextureStatus.OK;
                    TexPendingSince.Remove(tex.UniqueID);
                }
            }
            foreach (var key in TexPendingSince.Keys.Where(k => !live.Contains(k)).ToList())
                TexPendingSince.Remove(key);
        }
        catch (Exception)
        {
        }
    }

    private static void Draw()
    {
        _chipAnchorX = -1f;
        var tabs = Pylon.Mod.ImGuiTabRegistry.All;
        if (_activeTab < 0 || _activeTab > tabs.Count + 1)
        {
            _activeTab = 0;
        }
        if (ModernUI.PageStart < 0f)
        {
            ModernUI.PageStart = Motion.Time;
            _pageShownAt = Motion.Time;
        }
        if (!_menuOpen && _winAnim == WinAnim.Open) StartWindowAnim(WinAnim.Closing);
        if (_menuOpen && (_winAnim == WinAnim.Closed || _winAnim == WinAnim.Closing)) StartWindowAnim(WinAnim.Opening);

        if (_winAnim != WinAnim.Closed) DrawMenu(tabs);
        DrawBall();
        ModernUI.DrawToast();
        DrawFps();
    }

    private static void StartWindowAnim(WinAnim anim)
    {
        _winAnim = anim;
        _animStart = Motion.Time;
        _animFrom = _ballPos;
    }

    private static void DrawMenu(IReadOnlyList<Pylon.Mod.IModImGuiTab> tabs)
    {
        var (displayWidth, displayHeight) = ImGuiNativeApi.DisplaySize;
        if (displayWidth <= 0 || displayHeight <= 0) return;

        var size = WindowSize(displayWidth, displayHeight);
        EnsureWindowPosition(displayWidth, displayHeight, size);

        var rect = new Rect(_windowPos, size);
        var alpha = 1f;
        if (_winAnim == WinAnim.Closing)
        {
            var t = Motion.Enabled ? Motion.InCubic(Motion.Progress(_animStart, Motion.CloseSeconds)) : 1f;
            rect = RectLerp(rect, new Rect(_animFrom, new Vector2(TerrariaTheme.Px(DesignBallSize))), t);
            alpha = 1f - t;
            if (t >= 1f) _winAnim = WinAnim.Closed;
        }
        else if (_winAnim == WinAnim.Opening)
        {
            var t = Motion.Enabled ? Motion.Progress(_animStart, Motion.OpenSeconds) : 1f;
            var k = Motion.OutBack(t, 1.3f);
            rect = RectLerp(new Rect(_animFrom, new Vector2(TerrariaTheme.Px(DesignBallSize))), rect, k);
            alpha = Math.Clamp(t * 2.5f, 0f, 1f);
            if (t >= 1f) _winAnim = WinAnim.Open;
        }
        else if (_winAnim == WinAnim.Closed)
        {
            return;
        }

        UiTheme.ExtraFade = alpha;
        ImGui.SetNextWindowPos(rect.Min, ImGuiCond.Always);
        ImGui.SetNextWindowSize(rect.Size, ImGuiCond.Always);
        TerrariaTheme.PushVec2(ImGuiStyleVar.WindowPadding, Vector2.Zero);

        var open = true;
        const ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse |
            ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBackground |
            ImGuiWindowFlags.NoBringToFrontOnFocus;

        if (ImGui.Begin(MenuTitle, ref open, flags))
            DrawMenuBody(tabs, rect);
        ImGui.End();
        ImGui.PopStyleVar(2);
        UiTheme.ExtraFade = 1f;

        if (!open) _menuOpen = false;
    }

    private static void DrawMenuBody(IReadOnlyList<Pylon.Mod.IModImGuiTab> tabs, Rect rect)
    {
        var dl = ImGui.GetWindowDrawList();
        var radius = TerrariaTheme.Px(UiTheme.Radius);
        var panelOpacity = UiTheme.PanelOpacity;

        var win0 = UiTheme.Alpha(UiTheme.Win0, UiTheme.Win0.W * panelOpacity);
        var win1 = UiTheme.Alpha(UiTheme.Win1, UiTheme.Win1.W * panelOpacity);
        ModernUI.GradientRounded(dl, rect.Min, rect.Max, win0, win1, radius, 180f);
        AmbientWallpaper.Draw(dl, rect.Min, rect.Max, radius);
        dl.AddLine(rect.Min + new Vector2(radius, TerrariaTheme.Px(1f)),
            new Vector2(rect.Max.X - radius, rect.Min.Y + TerrariaTheme.Px(1f)),
            TerrariaTheme.U32(UiTheme.Alpha(UiTheme.CardHi, UiTheme.CardHi.W * 0.8f)), TerrariaTheme.Px(1f));

        var titleBottom = DrawTitleBar(dl, rect);
        var navTarget = _navExpanded ? TerrariaTheme.Px(DesignNavWidth) : 0f;
        AnimateNav(navTarget);
        var showNav = _navWidth > 0.5f;
        if (showNav)
        {
            DrawSidebar(tabs, rect, titleBottom);
        }

        var contentMin = new Vector2(rect.Min.X + (showNav ? _navWidth : 0f), titleBottom);
        var contentSize = new Vector2(rect.Max.X - contentMin.X, rect.Max.Y - titleBottom);
        if (contentSize.X > 1f && contentSize.Y > 1f)
            DrawContent(tabs, contentMin, contentSize);

        RegisterHitRect(rect.Min, rect.Size);
    }

    private static float DrawTitleBar(ImDrawListPtr dl, Rect rect)
    {
        var height = TerrariaTheme.Px(DesignTitleHeight);
        var bottom = rect.Min.Y + height;
        dl.AddLine(new Vector2(rect.Min.X, bottom - TerrariaTheme.Px(1f)),
            new Vector2(rect.Max.X, bottom - TerrariaTheme.Px(1f)),
            TerrariaTheme.U32(UiTheme.Hairline), TerrariaTheme.Px(1f));

        var button = TerrariaTheme.Px(DesignButtonSize);
        var buttonY = rect.Min.Y + (height - button) * 0.5f;
        var pad = TerrariaTheme.Px(12f);
        var foldMin = new Vector2(rect.Min.X + pad, buttonY);
        var closeMin = new Vector2(rect.Max.X - pad - button, buttonY);
        var foldMax = foldMin + new Vector2(button, button);
        var closeMax = closeMin + new Vector2(button, button);

        HandleWindowDrag(rect, foldMin, foldMax, closeMin, closeMax);

        if (TitleButton("##fold", foldMin, foldMax, _navExpanded ? ModernUI.Icon.ChevronLeft : ModernUI.Icon.ChevronRight,
                false)) 
        {
            _navExpanded = !_navExpanded;
        }
        if (TitleButton("##close", closeMin, closeMax, ModernUI.Icon.Close, true))
        {
            _menuOpen = false;
        }

        var markSize = TerrariaTheme.Px(DesignBrandSize);
        var markMin = new Vector2(foldMax.X + pad, rect.Min.Y + (height - markSize) * 0.5f);
        var markMax = markMin + new Vector2(markSize, markSize);
        ModernUI.GradientRounded(dl,
            markMin + new Vector2(0f, TerrariaTheme.Px(3f)),
            markMax + new Vector2(0f, TerrariaTheme.Px(10f)),
            UiTheme.Glow, UiTheme.Alpha(UiTheme.Glow, 0f), TerrariaTheme.Px(9f), 180f);
        ModernUI.GradientRounded(dl, markMin, markMax, UiTheme.Accent, UiTheme.Accent2, TerrariaTheme.Px(9f), 150f);
        var markInset = (markSize - TerrariaTheme.Px(17f)) * 0.5f;
        ModernUI.DrawIcon(dl, markMin + new Vector2(markInset), markMax - new Vector2(markInset),
            ModernUI.Icon.Star, new Vector4(1f, 1f, 1f, 1f), 1.8f);

        var nameSize = ModernUI.Measure("Pylon Mod", TerrariaTheme.Px(16f), 0.06f);
        var nameY = rect.Min.Y + (height - nameSize.Y) * 0.5f;
        ModernUI.DrawText(dl, new Vector2(markMax.X + TerrariaTheme.Px(10f), nameY),
            TerrariaTheme.Px(16f), UiTheme.Text, "Pylon Mod", null, 0.06f);
        var capsX = markMax.X + TerrariaTheme.Px(10f) + nameSize.X + TerrariaTheme.Px(2f);
        ModernUI.DrawCaps(dl, new Vector2(capsX, nameY + (nameSize.Y - ModernUI.CapsHeight(9f)) * 0.5f),
            TerrariaTheme.Px(9f), UiTheme.Text3, "MOD LOADER");

        var chip = UiTheme.Palette.Chip;
        var chipSize = TerrariaTheme.Px(11f);
        var chipTextSize = ModernUI.Measure(chip, chipSize);
        var chipMin = new Vector2(closeMin.X - TerrariaTheme.Px(12f) - chipTextSize.X - TerrariaTheme.Px(24f),
            rect.Min.Y + (height - chipTextSize.Y - TerrariaTheme.Px(8f)) * 0.5f);
        var chipMax = new Vector2(closeMin.X - TerrariaTheme.Px(12f),
            chipMin.Y + chipTextSize.Y + TerrariaTheme.Px(8f));
        dl.AddRectFilled(chipMin, chipMax, TerrariaTheme.U32(UiTheme.Field), TerrariaTheme.Px(20f));
        dl.AddRect(chipMin, chipMax, TerrariaTheme.U32(UiTheme.FieldStroke), TerrariaTheme.Px(20f));
        ModernUI.DrawText(dl, new Vector2(chipMin.X + TerrariaTheme.Px(12f),
            chipMin.Y + TerrariaTheme.Px(4f)), chipSize, UiTheme.Text2, chip);
        _chipAnchorX = chipMin.X;
        _chipTextTop = chipMin.Y + TerrariaTheme.Px(4f);
        return bottom;
    }

    private static bool TitleButton(string id, Vector2 min, Vector2 max, ModernUI.Icon icon, bool danger)
    {
        ImGui.SetCursorScreenPos(min);
        var clicked = ImGui.InvisibleButton(id, max - min, ImGuiButtonFlags.EnableNav);
        var hovered = ImGui.IsItemHovered() || ImGui.IsItemFocused();
        var dl = ImGui.GetWindowDrawList();
        if (hovered)
        {
            var bg = danger ? new Vector4(1f, 90f / 255f, 90f / 255f, 0.14f) : UiTheme.Soft;
            dl.AddRectFilled(min, max, TerrariaTheme.U32(bg), TerrariaTheme.Px(10f));
        }
        var iconInset = (max.X - min.X - TerrariaTheme.Px(16f)) * 0.5f;
        var iconMin = min + new Vector2(iconInset);
        var iconMax = max - new Vector2(iconInset);
        var color = danger && hovered
            ? new Vector4(1f, 122f / 255f, 122f / 255f, 1f)
            : hovered ? UiTheme.Accent : UiTheme.Text2;
        ModernUI.DrawIcon(dl, iconMin, iconMax, icon, color, 2f);
        return clicked;
    }

    private static void HandleWindowDrag(Rect rect, Vector2 foldMin, Vector2 foldMax, Vector2 closeMin, Vector2 closeMax)
    {
        if (_winAnim != WinAnim.Open) return;
        var useRaw = ImGuiInput.HasPointer;
        var mouse = useRaw ? ImGuiInput.PointerPos : ImGui.GetMousePos();
        var down = useRaw ? ImGuiInput.PointerDown : ImGui.IsMouseDown(ImGuiMouseButton.Left);
        var pressed = useRaw ? ImGuiInput.PointerBegan : ImGui.IsMouseClicked(ImGuiMouseButton.Left);
        var titleMin = new Vector2(rect.Min.X, rect.Min.Y);
        var titleMax = new Vector2(rect.Max.X, rect.Min.Y + TerrariaTheme.Px(DesignTitleHeight));

        if (!_windowDragging && !_windowPressPending && pressed &&
            InRect(mouse, rect.Min, rect.Max) && InRect(mouse, titleMin, titleMax) &&
            !InRect(mouse, foldMin, foldMax) && !InRect(mouse, closeMin, closeMax))
        {
            _windowPressPending = true;
            if (useRaw) TouchGestures.CaptureControl(ImGui.GetID("##windowdrag"));
            _windowPressPos = mouse;
        }

        if (_windowPressPending && !down)
        {
            _windowPressPending = false;
        }

        if (!_windowDragging && _windowPressPending &&
            (mouse - _windowPressPos).Length() > TerrariaTheme.Px(WindowDragThreshold))
        {
            _windowPressPending = false;
            _windowDragging = true;
            _windowGrabOffset = _windowPressPos - rect.Min;
            _windowPos = mouse - _windowGrabOffset;
        }

        if (!_windowDragging) return;
        if (!down)
        {
            _windowDragging = false;
            return;
        }
        _windowPos = mouse - _windowGrabOffset;
    }

    private static void AnimateNav(float target)
    {
        if (_navWidth < 0f)
        {
            _navWidth = target;
            return;
        }
        Motion.Spring(ref _navWidth, ref _navVelocity, target, ImGui.GetIO().DeltaTime, 130f, 19f);
    }

    private static void DrawSidebar(IReadOnlyList<Pylon.Mod.IModImGuiTab> tabs, Rect rect, float titleBottom)
    {
        var dl = ImGui.GetWindowDrawList();
        var panelOpacity = UiTheme.PanelOpacity;
        var navMin = new Vector2(rect.Min.X, titleBottom);
        var navMax = new Vector2(rect.Min.X + _navWidth, rect.Max.Y);
        var side0 = UiTheme.Alpha(UiTheme.Side0, UiTheme.Side0.W * panelOpacity);
        var side1 = UiTheme.Alpha(UiTheme.Side1, UiTheme.Side1.W * panelOpacity);
        ModernUI.GradientRoundedCorners(dl, navMin, navMax, side0, side1,
            TerrariaTheme.Px(UiTheme.Radius), 180f, ImDrawFlags.RoundCornersBottomLeft);
        dl.AddLine(new Vector2(navMax.X - TerrariaTheme.Px(1f), navMin.Y),
            new Vector2(navMax.X - TerrariaTheme.Px(1f), navMax.Y),
            TerrariaTheme.U32(UiTheme.Hairline), TerrariaTheme.Px(1f));
        if (_navWidth < TerrariaTheme.Px(120f)) return;

        var logoSize = TerrariaTheme.Px(34f);
        var logoY = navMin.Y + TerrariaTheme.Px(20f) + Motion.Bob(Motion.LogoBobSeconds, TerrariaTheme.Px(Motion.LogoBobAmplitude));
        var logoMin = new Vector2(navMin.X + (_navWidth - logoSize) * 0.5f, logoY);
        dl.AddCircleFilled(logoMin + new Vector2(logoSize * 0.5f), logoSize * 0.5f + TerrariaTheme.Px(6f),
            TerrariaTheme.U32(UiTheme.Glow));
        ModernUI.DrawIcon(dl, logoMin, logoMin + new Vector2(logoSize, logoSize), ModernUI.Icon.Star,
            UiTheme.Accent, 1.4f);
        var capsY = logoMin.Y + logoSize + TerrariaTheme.Px(6f);
        var caps = "PYLON";
        var capsWidth = ModernUI.MeasureCaps(caps, 9f).X;
        ModernUI.DrawCaps(dl, new Vector2(navMin.X + (_navWidth - capsWidth) * 0.5f, capsY),
            TerrariaTheme.Px(9f), UiTheme.Text3, caps);

        var itemsTop = capsY + ModernUI.CapsHeight(9f) + TerrariaTheme.Px(18f);
        var itemLeft = navMin.X + TerrariaTheme.Px(10f);
        var itemWidth = _navWidth - TerrariaTheme.Px(20f);
        var itemHeight = TerrariaTheme.Px(DesignNavItemH);
        var itemCount = tabs.Count + 2;
        var footY = navMax.Y - TerrariaTheme.Px(14f) - TerrariaTheme.Px(11f);
        var areaBottom = footY - TerrariaTheme.Px(20f);
        ImGui.SetCursorScreenPos(new Vector2(navMin.X, itemsTop));
        UiScroll.Begin("##navigation", new Vector2(navMax.X - navMin.X, MathF.Max(1f, areaBottom - itemsTop)),
            Vector2.Zero, priority: UiScroll.NavigationPriority);
        try
        {
        _navScroll = ImGui.GetScrollY();

        ImGui.PushClipRect(new Vector2(navMin.X, itemsTop), new Vector2(navMax.X, areaBottom), true);

        var selectedTop = itemsTop;
        var index = 0;
        DrawNavItem(itemLeft, itemsTop + index * itemHeight - _navScroll, itemWidth, itemHeight,
            "设置", "APPEARANCE", ModernUI.Icon.Sliders, _activeTab == 0, 0);
        if (_activeTab == 0) { selectedTop = itemsTop + index * itemHeight; }
        index++;
        for (var i = 0; i < tabs.Count; i++)
        {
            var top = itemsTop + index * itemHeight;
            DrawNavItem(itemLeft, top - _navScroll, itemWidth, itemHeight, SafeTitle(tabs[i]), "MOD",
                ModernUI.Icon.Sparkle, _activeTab == i + 1, i + 1);
            if (_activeTab == i + 1) { selectedTop = top; }
            index++;
        }
        var logTop = itemsTop + index * itemHeight;
        DrawNavItem(itemLeft, logTop - _navScroll, itemWidth, itemHeight, "运行日志", "RUNTIME LOG",
            ModernUI.Icon.Terminal, _activeTab == tabs.Count + 1, tabs.Count + 1);
        if (_activeTab == tabs.Count + 1) { selectedTop = logTop; }

        var selectedHeight = itemHeight;
        if (_markerHeight < 0f)
        {
            _markerTop = selectedTop - _navScroll;
            _markerHeight = selectedHeight;
        }
        else
        {
            var dt = ImGui.GetIO().DeltaTime;
            Motion.Spring(ref _markerTop, ref _markerVelTop, selectedTop - _navScroll, dt, 210f, 16f);
            Motion.Spring(ref _markerHeight, ref _markerVelHeight, selectedHeight, dt, 210f, 16f);
        }
        var markerMin = new Vector2(navMin.X + TerrariaTheme.Px(6f), _markerTop);
        var markerMax = new Vector2(markerMin.X + TerrariaTheme.Px(4f), _markerTop + _markerHeight);
        ModernUI.GradientRounded(dl, markerMin, markerMax, UiTheme.Accent2, UiTheme.Accent, TerrariaTheme.Px(2f), 180f);
        dl.AddRectFilled(markerMin - new Vector2(TerrariaTheme.Px(2f)), markerMax + new Vector2(TerrariaTheme.Px(2f)),
            TerrariaTheme.U32(UiTheme.Glow), TerrariaTheme.Px(2f));

        ImGui.PopClipRect();
        ImGui.SetCursorPosY(itemCount * itemHeight);
        ImGui.Dummy(Vector2.Zero);
        }
        finally { UiScroll.End(); }

        dl.AddLine(new Vector2(navMin.X, footY - TerrariaTheme.Px(14f)),
            new Vector2(navMax.X, footY - TerrariaTheme.Px(14f)),
            TerrariaTheme.U32(UiTheme.Hairline), TerrariaTheme.Px(1f));
        ModernUI.DrawText(dl, new Vector2(itemLeft, footY), TerrariaTheme.Px(11f), UiTheme.Text3, "Pylon Mod v0.5");
        var dot = new Vector2(navMax.X - TerrariaTheme.Px(21f), footY + TerrariaTheme.Px(7f));
        dl.AddCircleFilled(dot, TerrariaTheme.Px(3.5f), TerrariaTheme.U32(UiTheme.Good));
        dl.AddCircleFilled(dot, TerrariaTheme.Px(6f), TerrariaTheme.U32(UiTheme.Alpha(UiTheme.Good, 0.3f)));
    }

    private static float _navScroll;

    private static void DrawNavItem(float left, float top, float width, float height,
        string cn, string caps, ModernUI.Icon icon, bool selected, int tabIndex)
    {
        ImGui.SetCursorScreenPos(new Vector2(left, top));
        ImGui.PushID(NavId(tabIndex));
        try
        {
            var clicked = ImGui.InvisibleButton("##nav", new Vector2(width, height), ImGuiButtonFlags.EnableNav);
            var hovered = ImGui.IsItemHovered() || ImGui.IsItemFocused();
            var dl = ImGui.GetWindowDrawList();
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            if (hovered)
                dl.AddRectFilled(min, max, TerrariaTheme.U32(UiTheme.Soft), TerrariaTheme.Px(12f));
            var iconMin = min + new Vector2(TerrariaTheme.Px(14f), (height - TerrariaTheme.Px(DesignNavIcon)) * 0.5f);
            var iconMax = iconMin + new Vector2(TerrariaTheme.Px(DesignNavIcon), TerrariaTheme.Px(DesignNavIcon));
            var iconInset = TerrariaTheme.Px(1f);
            ModernUI.DrawIcon(dl, iconMin + new Vector2(iconInset), iconMax - new Vector2(iconInset), icon,
                selected ? UiTheme.Accent : UiTheme.Text2, 1.7f);
            var textX = iconMax.X + TerrariaTheme.Px(11f);
            var cnSize = ModernUI.Measure(cn, TerrariaTheme.Px(14f), 0.04f);
            var enY = min.Y + (height - cnSize.Y - TerrariaTheme.Px(2f) - ModernUI.CapsHeight(9f)) * 0.5f;
            ModernUI.DrawText(dl, new Vector2(textX, enY), TerrariaTheme.Px(14f),
                selected ? UiTheme.Accent : hovered ? UiTheme.Text : UiTheme.Text2, cn, null, 0.04f);
            var enColor = selected ? UiTheme.Alpha(UiTheme.Accent, 0.75f) : UiTheme.Text3;
            ModernUI.DrawCaps(dl, new Vector2(textX, enY + cnSize.Y + TerrariaTheme.Px(2f)),
                TerrariaTheme.Px(9f), enColor, caps);
            if (clicked) ActivateTab(tabIndex);
        }
        finally
        {
            ImGui.PopID();
        }
    }

    private static void DrawContent(IReadOnlyList<Pylon.Mod.IModImGuiTab> tabs, Vector2 min, Vector2 size)
    {
        ImGui.SetCursorScreenPos(min);
        var reset = _scrollResetTab != _activeTab;
        _scrollResetTab = _activeTab;
        UiScroll.Begin("##page" + _activeTab, size,
            new Vector2(TerrariaTheme.Px(30f), TerrariaTheme.Px(26f)), reset);
        try
        {
            var pageIn = Motion.Enabled
                ? 1f - Motion.OutCubic(Motion.Progress(_pageShownAt, Motion.PageInDuration))
                : 0f;
            if (pageIn > 0f) ImGui.SetCursorPosY(ImGui.GetCursorPosY() + pageIn * TerrariaTheme.Px(12f));
            ModernUI.BeginPage();
            if (_activeTab == 0)
            {
                SettingsPage.Draw();
            }
            else if (_activeTab == tabs.Count + 1)
            {
                LogPage.Draw();
            }
            else
            {
                DrawModTab(tabs[_activeTab - 1]);
            }
            ImGui.Dummy(new Vector2(0f, TerrariaTheme.Px(34f)));
        }
        finally
        {
            UiScroll.End();
        }
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
            BrokenTabs.Add(tab);
            ReportFrameErrorOnce(e);
            DrawTabError(tab, e);
        }
        catch (Exception e)
        {
            BrokenTabs.Add(tab);
            ReportFrameErrorOnce(e);
        }
    }

    private static bool IsTabLoadFailure(Exception e) =>
        e is TypeLoadException or MissingMethodException or MissingFieldException
            or FileNotFoundException or FileLoadException or BadImageFormatException;

    private static void DrawTabError(Pylon.Mod.IModImGuiTab tab, Exception? error)
    {
        ModernUI.Text($"{SafeTitle(tab)} 与当前 Pylon.NaviteAPI 不兼容。");
        ModernUI.DimText("请使用当前版本的 Pylon.NaviteAPI 重新编译该 Mod。");
        if (error != null) ModernUI.DimText($"{error.GetType().Name}: {error.Message}");
    }

    private static string SafeTitle(Pylon.Mod.IModImGuiTab tab)
    {
        try { return string.IsNullOrEmpty(tab.Title) ? "Mod" : tab.Title; }
        catch (Exception) { return "Mod"; }
    }

    private static string NavId(int index) =>
        NavIds.TryGetValue(index, out var navId) ? navId : NavIds[index] = "nav" + index;

    private static int _scrollResetTab = -1;

    private static void ActivateTab(int tab)
    {
        if (_activeTab == tab) return;
        TouchGestures.StopInertia();
        _activeTab = tab;
        _pageShownAt = Motion.Time;
        ModernUI.PageStart = Motion.Time;
    }

    private static void DrawBall()
    {
        var (displayWidth, displayHeight) = ImGuiNativeApi.DisplaySize;
        if (displayWidth <= 0 || displayHeight <= 0) return;

        var size = new Vector2(TerrariaTheme.Px(DesignBallSize));
        if (!_ballPlaced)
        {
            _ballPlaced = true;
            _ballPos = new Vector2(TerrariaTheme.Px(DesignBallMargin),
                (displayHeight - size.Y) * 0.5f);
        }
        ClampBall(displayWidth, displayHeight, size);

        var bob = Motion.Bob(Motion.BallBobSeconds, TerrariaTheme.Px(Motion.BallBobAmplitude));
        var drawPos = _ballPos + new Vector2(0f, bob);
        ImGui.SetNextWindowPos(drawPos, ImGuiCond.Always);
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);
        TerrariaTheme.PushVec2(ImGuiStyleVar.WindowPadding, Vector2.Zero);

        var open = true;
        const ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoFocusOnAppearing |
            ImGuiWindowFlags.NoBringToFrontOnFocus;

        if (ImGui.Begin("##pylonBall", ref open, flags))
        {
            var clicked = ImGui.InvisibleButton("##ball", size,
                ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.EnableNav);
            HandleBallInput(clicked, size, displayWidth, displayHeight, drawPos);
            DrawBallSurface(ImGui.GetWindowDrawList(), drawPos, drawPos + size,
                ImGui.IsItemHovered() || ImGui.IsItemActive());
            RegisterHitRect(drawPos, size);
        }
        ImGui.End();
        ImGui.PopStyleVar(2);
    }

    private static void HandleBallInput(bool clicked, Vector2 size, float displayWidth, float displayHeight,
        Vector2 drawPos)
    {
        var mouse = ImGui.GetMousePos();
        if (ImGui.IsItemActivated())
        {
            _ballPressPos = mouse;
            _ballGrabOffset = mouse - _ballPos;
            _ballDragMoved = false;
        }

        if (ImGui.IsItemActive() && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            if (!_ballDragMoved && (mouse - _ballPressPos).Length() > TerrariaTheme.Px(DesignBallDragThreshold))
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
        var rounding = TerrariaTheme.Px(DesignBallRounding);
        ModernUI.GradientRounded(dl, min, max,
            UiTheme.Alpha(UiTheme.Win0, 0.92f),
            UiTheme.Alpha(UiTheme.Win1, 0.95f), rounding, 180f);
        dl.AddRect(min, max,
            TerrariaTheme.U32(hovered
                ? UiTheme.Alpha(UiTheme.Accent, 0.55f)
                : UiTheme.Alpha(UiTheme.Hairline, MathF.Max(0.16f, UiTheme.Hairline.W))),
            rounding);
        if (hovered)
        {
            dl.AddRect(min - new Vector2(TerrariaTheme.Px(4f)), max + new Vector2(TerrariaTheme.Px(4f)),
                TerrariaTheme.U32(UiTheme.Alpha(UiTheme.Accent, 0.35f)),
                rounding + TerrariaTheme.Px(4f));
        }
        if (!_menuOpen)
        {
            var pulse = Motion.Cycle(Motion.PulseSeconds);
            var spread = TerrariaTheme.Px(7f) * MathF.Sin(MathF.PI * pulse);
            var ring = min - new Vector2(TerrariaTheme.Px(5f) + spread);
            var ringMax = max + new Vector2(TerrariaTheme.Px(5f) + spread);
            dl.AddRect(ring, ringMax,
                TerrariaTheme.U32(UiTheme.Alpha(UiTheme.Accent, 0.55f * (1f - pulse))),
                rounding + TerrariaTheme.Px(5f));
        }
        var barW = TerrariaTheme.Px(18f);
        var barH = TerrariaTheme.Px(2f);
        var barLeft = min.X + (max.X - min.X - barW) * 0.5f;
        var barTop = min.Y + (max.Y - min.Y) * 0.5f - barH * 1.5f - TerrariaTheme.Px(4f);
        for (var i = 0; i < 3; i++)
        {
            dl.AddRectFilled(new Vector2(barLeft, barTop + i * (barH + TerrariaTheme.Px(4f))),
                new Vector2(barLeft + barW, barTop + i * (barH + TerrariaTheme.Px(4f)) + barH),
                TerrariaTheme.U32(UiTheme.Text2), TerrariaTheme.Px(1f));
        }
    }

    private static long _fpsStamp;
    private static float _fpsAvg = 60f;

    private static void DrawFps()
    {
        if (!UiSettings.ShowFps) return;
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var freq = (float)System.Diagnostics.Stopwatch.Frequency;
        if (_fpsStamp != 0)
        {
            var dt = (now - _fpsStamp) / freq;
            if (dt > 0.0001f && dt < 1f) _fpsAvg += (1f / dt - _fpsAvg) * 0.12f;
        }
        _fpsStamp = now;
        var fps = (int)(_fpsAvg + 0.5f);
        if (fps != _lastFps)
        {
            _lastFps = fps;
            _fpsText = fps + " FPS";
        }
        if (_chipAnchorX < 0f) return;
        var size = TerrariaTheme.Px(12f);
        var text = _fpsText ?? string.Empty;
        var textSize = ModernUI.Measure(text, size);
        var x = _chipAnchorX - TerrariaTheme.Px(14f) - textSize.X;
        var y = _chipTextTop;
        var dl = ImGui.GetForegroundDrawList();
        ModernUI.DrawText(dl, new Vector2(x, y), size, UiTheme.Good, text);
    }

    private static Vector2 WindowSize(float displayWidth, float displayHeight)
    {
        var width = MathF.Min(displayWidth * WindowWidthRatio, TerrariaTheme.Px(DesignWindowWidth));
        var height = MathF.Min(displayHeight * WindowHeightRatio, TerrariaTheme.Px(DesignWindowHeight));
        return new Vector2(MathF.Min(width, displayWidth), MathF.Min(height, displayHeight));
    }

    private static void EnsureWindowPosition(float displayWidth, float displayHeight, Vector2 size)
    {
        if (!_layoutReady)
        {
            _layoutReady = true;
            _windowPos = new Vector2((displayWidth - size.X) * 0.5f, (displayHeight - size.Y) * 0.5f);
        }
        var maxX = MathF.Max(0f, displayWidth - size.X);
        var maxY = MathF.Max(0f, displayHeight - size.Y);
        _windowPos = new Vector2(
            Math.Clamp(_windowPos.X, 0f, maxX),
            Math.Clamp(_windowPos.Y, 0f, maxY));
    }

    private static Rect RectLerp(Rect a, Rect b, float t) => new(
        Vector2.Lerp(a.Min, b.Min, t),
        Vector2.Lerp(a.Size, b.Size, t));

    private readonly record struct Rect(Vector2 Min, Vector2 Size)
    {
        public Vector2 Max => Min + Size;
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
}
