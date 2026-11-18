using Pylon.NaviteAPI.ImGui;
using UnityEngine;

namespace Pylon.ModLoader.ImGuiHost;

/// <summary>
/// 把 Unity 的触摸喂给 ImGui。
/// </summary>
internal static class ImGuiInput
{
    private const float WheelGain = 0.01f;

    private static int _fingerId = -1;
    private static bool _down;
    private static float _lastY;
    private static bool _dragging;

    internal static void Feed()
    {
        if (!ImGuiContext.Created) return;
        var io = ImGui.GetIO();

        int count;
        try { count = Input.touchCount; }
        catch (Exception) { return; }

        if (!TryPickTouch(count, out var touch))
        {
            ReleaseIfDown(io);
            return;
        }

        if (touch.phase is TouchPhase.Ended or TouchPhase.Canceled)
        {
            ReleaseIfDown(io);
            return;
        }

        var (x, y) = Map(touch.position);
        io.AddMousePosEvent(x, y);
        if (!_down)
        {
            io.AddMouseButtonEvent(0, true);
            _down = true;
            _dragging = false;
            _lastY = y;
        }

        if (ImGuiNative.igIsAnyItemActive() == 0)
        {
            var dy = y - _lastY;
            if (_dragging && Math.Abs(dy) > 0.5f)
                io.AddMouseWheelEvent(0f, dy * WheelGain);
            if (Math.Abs(dy) > 2f) _dragging = true;
        }
        _lastY = y;
    }

    private static bool TryPickTouch(int count, out Touch touch)
    {
        touch = default;
        if (count <= 0) { _fingerId = -1; return false; }

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
        _dragging = false;
    }

    private static (float X, float Y) Map(Vector2 position)
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
