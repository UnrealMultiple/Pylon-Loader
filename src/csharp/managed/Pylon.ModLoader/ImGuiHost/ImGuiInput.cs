using Pylon.NaviteAPI.ImGui;
using UnityEngine;
using Vector2 = System.Numerics.Vector2;

namespace Pylon.ModLoader.ImGuiHost;

/// <summary>
/// 把 Unity 的触摸喂给 ImGui：按触屏模式门控，同时暴露原始指针供滚动容器取用；
/// 滚动拖动期间指针钉在远处，让被拖过的控件自然失去悬停（松手不误触点击）。
/// </summary>
internal static class ImGuiInput
{
    private static int _fingerId = -1;
    private static bool _down;

    /// <summary>原始指针（触摸）当前位置；HasPointer=false 时调用方应回退到 ImGui 的鼠标状态。</summary>
    internal static bool HasPointer { get; private set; }

    internal static bool PointerDown { get; private set; }

    /// <summary>本帧指针刚按下（Began 边沿）；供窗口拖拽等需要"新按压"语义的手势使用。</summary>
    internal static bool PointerBegan { get; private set; }

    internal static Vector2 PointerPos { get; private set; }

    internal static bool PinPageScroll { get; set; }

    internal static bool PinNavScroll { get; set; }

    private static bool PinPointer => PinPageScroll || PinNavScroll;

    internal static void Feed()
    {
        PointerBegan = false;
        if (!ImGuiContext.Created) return;
        var io = ImGui.GetIO();
        if (UiSettings.TouchMode == 2 || !TouchCapable())
        {
            ReleaseIfDown(io);
            return;
        }

        int count;
        try { count = Input.touchCount; }
        catch (Exception) { ReleaseIfDown(io); return; }
        if (!TryPickTouch(count, out var touch) || touch.phase is TouchPhase.Ended or TouchPhase.Canceled)
        {
            ReleaseIfDown(io);
            return;
        }

        var (x, y) = Map(touch.position);
        HasPointer = true;
        PointerDown = true;
        PointerPos = new Vector2(x, y);

        if (_down && touch.phase == TouchPhase.Began)
        {
            io.AddMouseButtonEvent(0, false);
            _down = false;
        }
        var feedX = PinPointer ? ImGuiNativeApi.DisplaySize.Width + 4096f : x;
        var feedY = PinPointer ? ImGuiNativeApi.DisplaySize.Height + 4096f : y;
        if (!_down)
        {
            io.AddMousePosEvent(feedX, feedY);
            io.AddMouseButtonEvent(0, true);
            _down = true;
            PointerBegan = true;
            return;
        }
        io.AddMousePosEvent(feedX, feedY);
    }

    private static bool TouchCapable()
    {
        if (UiSettings.TouchMode == 1) return true;
        try { return Input.touchSupported; }
        catch (Exception) { return false; }
    }

    private static bool TryPickTouch(int count, out Touch touch)
    {
        touch = default;
        if (count <= 0) { _fingerId = -1; PointerDown = false; return false; }
        if (_fingerId >= 0)
        {
            for (var i = 0; i < count; i++)
            {
                var candidate = Input.GetTouch(i);
                if (candidate.fingerId == _fingerId) { touch = candidate; return true; }
            }
        }
        touch = Input.GetTouch(0);
        _fingerId = touch.fingerId;
        return true;
    }

    private static void ReleaseIfDown(ImGuiIOPtr io)
    {
        if (_down) { io.AddMouseButtonEvent(0, false); _down = false; }
        _fingerId = -1;
        PointerDown = false;
        HasPointer = false;
        PointerBegan = false;
        PinPageScroll = false;
        PinNavScroll = false;
    }

    private static (float X, float Y) Map(UnityEngine.Vector2 position)
    {
        ImGuiNativeApi.PixelSize(out var dw, out var dh);
        float iw = Screen.width;
        float ih = Screen.height;
        if (dw <= 0 || dh <= 0 || iw <= 0 || ih <= 0) return (position.x, position.y);
        if ((iw >= ih) == (dw >= dh))
            return (position.x * dw / iw, (ih - position.y) * dh / ih);
        return (position.y * dw / ih, (iw - position.x) * dh / iw);
    }
}
