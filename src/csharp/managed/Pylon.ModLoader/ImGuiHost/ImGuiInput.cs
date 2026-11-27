using Pylon.NaviteAPI.ImGui;
using UnityEngine;
using Vector2 = System.Numerics.Vector2;

namespace Pylon.ModLoader.ImGuiHost;

/// <summary>Single-pointer platform bridge. All deltas use the same mapped screen coordinates as MousePos.</summary>
internal static class ImGuiInput
{
    private static int _fingerId = -1;
    private static bool _down;
    internal static bool HasPointer { get; private set; }
    internal static bool PointerDown { get; private set; }
    internal static bool PointerBegan { get; private set; }
    internal static bool PointerEnded { get; private set; }
    internal static Vector2 PointerPos { get; private set; }

    internal static void Feed()
    {
        PointerBegan = PointerEnded = false;
        HasPointer = _down;
        if (!ImGuiContext.Created) return;
        var io = ImGui.GetIO();
        TouchGestures.BeginFrame();
        if (UiSettings.TouchMode == 2 || !TouchCapable())
        {
            Release(io, canceled: true);
            TouchGestures.FeedWheel(io, io.DeltaTime);
            return;
        }
        try
        {
            if (TryPickTouch(out var touch))
            {
                var pos = Map(touch.position);
                HasPointer = true;
                PointerPos = pos;
                io.AddMouseSourceEvent(ImGuiMouseSource.TouchScreen);
                io.AddMousePosEvent(pos.X, pos.Y);
                if (!_down)
                {
                    _down = PointerDown = PointerBegan = true;
                    _fingerId = touch.fingerId;
                    TouchGestures.Down(pos);
                    io.AddMouseButtonEvent(0, true);
                }
                else if (touch.phase is TouchPhase.Ended or TouchPhase.Canceled)
                {
                    TouchGestures.Move(pos, io.DeltaTime);
                    Release(io, touch.phase == TouchPhase.Canceled);
                }
                else TouchGestures.Move(pos, io.DeltaTime);
            }
            else Release(io, canceled: true);
        }
        catch (Exception)
        {
            Release(io, canceled: true);
        }
        TouchGestures.FeedWheel(io, io.DeltaTime);
    }

    private static bool TouchCapable()
    {
        if (UiSettings.TouchMode == 1) return true;
        try { return Input.touchSupported; }
        catch (Exception) { return false; }
    }
    private static bool TryPickTouch(out Touch touch)
    {
        touch = default;
        var count = Input.touchCount;
        for (var i = 0; i < count; i++)
        {
            var candidate = Input.GetTouch(i);
            // A lost/ended active finger is never replaced by another finger in this sequence.
            // When idle, accept the first live finger even if we missed its Began frame:
            // short, fast swipes sometimes arrive already Moved, and requiring Began made
            // those gestures vanish (the "sometimes scrolling doesn't respond" report).
            if (_down ? candidate.fingerId == _fingerId
                      : candidate.phase is TouchPhase.Began or TouchPhase.Moved or TouchPhase.Stationary)
            { touch = candidate; return true; }
        }
        return false;
    }
    private static void Release(ImGuiIOPtr io, bool canceled)
    {
        if (_down)
        {
            PointerEnded = true;
            TouchGestures.Up(canceled);
            io.AddMouseButtonEvent(0, false);
            _down = false;
        }
        _fingerId = -1;
        PointerDown = false;
        // HasPointer remains true for the release frame: consumers must not fall back to a mouse UP.
    }
    internal static Vector2 Map(UnityEngine.Vector2 position)
    {
        ImGuiNativeApi.PixelSize(out var dw, out var dh);
        float iw = Screen.width, ih = Screen.height;
        if (dw <= 0 || dh <= 0 || iw <= 0 || ih <= 0) return new(position.x, ih - position.y);
        if ((iw >= ih) == (dw >= dh))
            return new(position.x * dw / iw, (ih - position.y) * dh / ih);
        return new(position.y * dw / ih, (iw - position.x) * dh / iw);
    }
}
