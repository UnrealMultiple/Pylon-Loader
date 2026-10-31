using Pylon.ImGui;
using UnityEngine;
using ImGuiApi = Pylon.ImGui.ImGui;

namespace Pylon.ModLoader.UI;

/// <summary>
/// 把 Unity 的触摸喂给 ImGui。要点：
///  · 按 fingerId 跟踪同一根手指（不是固定 pointer 0）；
///  · 坐标按屏幕朝向做三支变换（竖屏 / 左横 / 右横）；
///  · 空白处纵向拖动合成滚轮，模拟列表滚动；
///  · 不用 io.WantCaptureMouse 决定穿透（它慢一帧，会双击）。
/// </summary>
internal static class ImGuiInput
{
    private const float WheelGain = 0.01f;

    private static int _fingerId = -1;
    private static bool _down;
    private static float _lastY;
    private static bool _dragging;

    /// <summary>在 NewFrame 之前调用。</summary>
    internal static void Feed()
    {
        if (!ImGuiContext.Created) return;
        var io = ImGuiApi.GetIO();

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

        // 没有控件被激活时，纵向拖动合成滚轮（列表/内容滚动）
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

    /// <summary>
    /// Unity 屏幕坐标（左下原点）→ 覆盖层像素坐标（左上原点）。
    /// 屏幕与表面横竖不一致时说明设备转了 90°，走旋转分支。
    /// </summary>
    private static (float X, float Y) Map(Vector2 position)
    {
        ImGuiNativeApi.PixelSize(out var dw, out var dh);
        float iw = Screen.width;
        float ih = Screen.height;
        if (dw <= 0 || dh <= 0 || iw <= 0 || ih <= 0) return (position.x, position.y);

        // 横竖方向是否一致（一致=不旋转，直接按比例映射并翻转 Y）
        if ((iw >= ih) == (dw >= dh))
            return (position.x * dw / iw, (ih - position.y) * dh / ih);

        // 旋转 90°：屏幕的 y 映射到表面的 x
        return (position.y * dw / ih, (iw - position.x) * dh / iw);
    }
}
