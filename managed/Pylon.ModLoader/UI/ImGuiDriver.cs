using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Pylon.ModLoader.UI;

internal static class ImGuiDriver
{
    [DllImport("pylon", EntryPoint = "PylonImgui_Start")] private static extern int Start();
    [DllImport("pylon", EntryPoint = "PylonImgui_Stop")] private static extern void Stop();
    [DllImport("pylon", EntryPoint = "PylonImgui_IsInitialized")] private static extern int IsInitializedNative();
    [DllImport("pylon", EntryPoint = "PylonImgui_DisplaySize")] private static extern void DisplaySizeNative(ref float w, ref float h);
    [DllImport("pylon", EntryPoint = "PylonImgui_BeginFrame")] internal static extern void BeginFrame();
    [DllImport("pylon", EntryPoint = "PylonImgui_EndFrame")] internal static extern void EndFrame();
    [DllImport("pylon", EntryPoint = "PylonImgui_CaptureUiScaleBase")] private static extern void CaptureUiScaleBaseNative();
    [DllImport("pylon", EntryPoint = "PylonImgui_SetUiScale")] private static extern void SetUiScaleNative(float scale);
    [DllImport("pylon", EntryPoint = "PylonImgui_GetUiScale")] private static extern float GetUiScaleNative();

    private const int KeyBackspace = 523;
    private const int KeyEnter = 525;
    private const int KeyEscape = 526;

    /// <summary>上一帧按下、本帧要补抬起的键</summary>
    private static int _pendingKeyUp;

    private static void SendKeyDownUp(int imguiKey)
    {
        if (_pendingKeyUp != 0) Pylon.ImGui.ImGuiNative.igAddKeyEvent(_pendingKeyUp, 0);
        Pylon.ImGui.ImGuiNative.igAddKeyEvent(imguiKey, 1);
        _pendingKeyUp = imguiKey;
    }


    private static bool _started;

    public static bool Initialized => IsInitializedNative() != 0;
    public static bool Visible { get; set; } = true;

    public static (float Width, float Height) DisplaySize
    {
        get
        {
            float w = 0, h = 0;
            DisplaySizeNative(ref w, ref h);
            return (w, h);
        }
    }

    internal static void EnsureStarted()
    {
        if (_started) return;
        _started = true;
        try
        {
            if (Start() == 0)
            {
                ModUI.Logger?.Error("ImGui 悬浮窗启动失败");
                _started = false;
            }
            else ModUI.Logger?.Info("ImGui 悬浮窗已启动");
        }
        catch (Exception e)
        {
            ModUI.Logger?.Error("ImGui 启动异常：" + ModUI.Flatten(e));
            _started = false;
        }
    }

    internal static void Shutdown()
    {
        if (!_started) return;
        _started = false;
        try { Stop(); } catch { }
    }


    private static bool _themeApplied;
    private static bool _scaleBaseCaptured;

    internal static void EnsureThemeApplied()
    {
        if (_themeApplied) return;
        if (!Initialized) return;
        _themeApplied = true;
        try
        {
            Pylon.ImGui.ModernTheme.Apply();
            CaptureUiScaleBaseNative();
            _scaleBaseCaptured = true;
            ApplyUiScale(UiSettings.Scale);
        }
        catch (Exception e) { ModUI.Logger?.Warn("主题应用失败：" + ModUI.Flatten(e)); }
    }

    /// <summary>整体缩放 ImGui（字体 + 控件度量）。设置页与启动恢复都走这里。</summary>
    internal static void ApplyUiScale(float scale)
    {
        if (!Initialized) return;
        try
        {
            if (!_scaleBaseCaptured)
            {
                CaptureUiScaleBaseNative();
                _scaleBaseCaptured = true;
            }
            SetUiScaleNative(scale);
        }
        catch (Exception e)
        {
            ModUI.Logger?.Warn("应用 UI 比例失败：" + ModUI.Flatten(e));
        }
    }

    internal static float CurrentUiScale
    {
        get
        {
            try { return Initialized ? GetUiScaleNative() : UiSettings.Scale; }
            catch { return UiSettings.Scale; }
        }
    }


    internal static void FeedTouchFromUnity()
    {
        if (!Initialized) return;
        int count;
        try { count = Input.touchCount; }
        catch { return; }
        if (count <= 0)
        {
            ReleaseTouch();
            return;
        }

        float scrW, scrH, dispW, dispH;
        try
        {
            scrW = Screen.width;
            scrH = Screen.height;
            dispW = 0; dispH = 0;
            DisplaySizeNative(ref dispW, ref dispH);
            if (scrW <= 0 || scrH <= 0 || dispW <= 0 || dispH <= 0) return;
        }
        catch { return; }

        var touch = Input.GetTouch(0);
        var phase = touch.phase;
        var down = phase != TouchPhase.Ended && phase != TouchPhase.Canceled;

        if (!_touchDown && down && phase != TouchPhase.Began) _ignoreTouchUntilRelease = true;
        if (_ignoreTouchUntilRelease)
        {
            if (!down) _ignoreTouchUntilRelease = false;
            ReleaseTouch();
            return;
        }

        if (!down)
        {
            ReleaseTouch();
            return;
        }

        var scaleX = dispW / scrW;
        var scaleY = dispH / scrH;
        var pos = touch.position;
        float x = pos.x * scaleX;
        float y = (scrH - pos.y) * scaleY;

        Pylon.ImGui.ImGuiNative.igAddMousePosEvent(x, y);
        if (!_touchDown) Pylon.ImGui.ImGuiNative.igAddMouseButtonEvent(0, 1);
        _touchDown = true;

        if (_touchDragging && Pylon.ImGui.ImGuiNative.igIsWindowMoving() == 0)
        {
            float dy = y - _lastTouchY;
            if (dy != 0)
            {
                float wheel = Math.Clamp(dy * 0.01f, -0.5f, 0.5f);
                if (wheel != 0) Pylon.ImGui.ImGuiNative.igAddMouseWheelEvent(0, wheel);
            }
        }
        _touchDragging = true;
        _lastTouchX = x;
        _lastTouchY = y;
    }

    /// <summary>把"手指已抬起"补进 ImGui；触摸计数归零时也必须发，否则鼠标会卡在按下状态。</summary>
    private static void ReleaseTouch()
    {
        if (!_touchDown) return;
        _touchDown = false;
        _touchDragging = false;
        try { Pylon.ImGui.ImGuiNative.igAddMouseButtonEvent(0, 0); }
        catch { }
    }

    private static float _lastTouchX, _lastTouchY;
    private static bool _touchDown;
    private static bool _touchDragging;
    private static bool _ignoreTouchUntilRelease;

    private static TouchScreenKeyboard _keyboard;
    private static string _syncedText = ""; 
    private static int _closeCooldown;      
    private const int CloseCooldownFrames = 60;

    /// <summary>每帧驱动（游戏线程，ModUI.Update 里调）。</summary>
    internal static void UpdateKeyboard()
    {
        if (!Initialized) return;

        if (_pendingKeyUp != 0)
        {
            Pylon.ImGui.ImGuiNative.igAddKeyEvent(_pendingKeyUp, 0);
            _pendingKeyUp = 0;
        }

        int want;
        try { want = Pylon.ImGui.ImGuiNative.igGetIO_WantTextInput(); }
        catch { return; }

        bool active = _keyboard != null && _keyboard.active;

        if (active) Pylon.ImGui.ImGuiNative.igSetKeyboardActive(1);

        if (_keyboard != null && !active)
        {
            ClearFocus();
            CloseKeyboard();
        }

        if (_closeCooldown > 0)
            _closeCooldown = want == 0 ? 0 : _closeCooldown - 1;

        if (want != 0)
        {
            if (active) SyncKeyboardText();
            else if (_closeCooldown == 0) OpenKeyboard();
        }
        else if (_keyboard != null)
        {
            ClearFocus();
            CloseKeyboard();
        }
    }

    /// <summary>把键盘文本增量同步进 ImGui 输入框（键盘 = 输入框的编辑视图）。</summary>
    private static void SyncKeyboardText()
    {
        var activeBuffer = Pylon.ImGui.ImGui.ActiveInputBuffer;
        if (activeBuffer != _syncedText)
        {
            try { _keyboard.text = activeBuffer; } catch { }
            _syncedText = activeBuffer;
            return;
        }

        string current;
        try { current = _keyboard.text ?? ""; }
        catch { return; }
        if (current == _syncedText) return;

        int common = 0;
        int max = Math.Min(_syncedText.Length, current.Length);
        while (common < max && _syncedText[common] == current[common]) common++;

        if (_syncedText.Length > common)
        {
            SendKeyDownUp(KeyBackspace);
            _syncedText = _syncedText.Substring(0, _syncedText.Length - 1);
            return;
        }

        if (current.Length > common)
        {
            try { Pylon.ImGui.ImGuiNative.igAddInputCharactersUTF8(current.Substring(common)); } catch { }
            _syncedText = current;
        }
    }

    private static void OpenKeyboard()
    {
        try
        {
            _keyboard = TouchScreenKeyboard.Open(
                Pylon.ImGui.ImGui.ActiveInputBuffer,
                TouchScreenKeyboardType.Default, false, false, false, false, "", 0);
            _syncedText = Pylon.ImGui.ImGui.ActiveInputBuffer;
            Pylon.ImGui.ImGuiNative.igSetKeyboardActive(1);   // 键盘活跃 C++ 跳过 surface 重建
        }
        catch { }
    }

    private static void CloseKeyboard()
    {
        Pylon.ImGui.ImGuiNative.igSetKeyboardActive(0);   // 键盘收起守卫结束
        _keyboard = null;
        _syncedText = "";
        _closeCooldown = CloseCooldownFrames;
    }

    private static void ClearFocus()
    {
        try { Pylon.ImGui.ImGuiNative.igClearActiveID(); } catch { }
    }
}