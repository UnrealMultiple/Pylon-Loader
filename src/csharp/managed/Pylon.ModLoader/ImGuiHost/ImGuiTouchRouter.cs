using UnityEngine;

namespace Pylon.ModLoader.ImGuiHost;

/// <summary>
/// 触摸穿透拦截：注册本帧菜单窗口矩形
/// </summary>
internal static class ImGuiTouchRouter
{
    private readonly record struct Rect(float X, float Y, float W, float H);

    private static readonly List<Rect> Rects = [];
    internal static float Kx = 1f, Ky = 1f, ScrH = 1f;

    private static bool _installed;

    private static readonly Dictionary<int, bool> Captured = new();

    internal static void BeginFrame()
    {
        // Snapshot ownership at DOWN, before rebuilding rectangles. Keep terminal fingers
        // until the next frame so game hooks observe the same owner for UP/CANCEL.
        var alive = new HashSet<int>();
        try
        {
            for (var i = 0; i < Input.touchCount; i++)
            {
                var touch = Input.GetTouch(i);
                alive.Add(touch.fingerId);
                if (touch.phase != TouchPhase.Began || Captured.ContainsKey(touch.fingerId)) continue;
                var pos = ImGuiInput.Map(touch.position);
                Captured[touch.fingerId] = UiScroll.Registry.HasPopup || HitWindowAt(pos.X, pos.Y);
            }
            foreach (var id in Captured.Keys.Where(id => !alive.Contains(id)).ToArray()) Captured.Remove(id);
        }
        catch (Exception) { }
        Rects.Clear();
    }

    internal static void AddWindowRect(float x, float y, float w, float h,
                                       float kx, float ky, float scrH)
    {
        if (w <= 0 || h <= 0) return;
        Kx = kx;
        Ky = ky;
        ScrH = scrH;
        Rects.Add(new Rect(x, y, w, h));
    }

    internal static bool HitWindowAt(float overlayX, float overlayY)
    {
        foreach (var r in Rects)
        {
            if (overlayX >= r.X && overlayX <= r.X + r.W &&
                overlayY >= r.Y && overlayY <= r.Y + r.H)
                return true;
        }
        return false;
    }

    internal static void RequestInstall()
    {
        if (_installed) return;
        _installed = true;
        On.CursorManager.ShouldIgnoreForWorld += ShouldIgnoreForWorld;
        On.CursorManager.ShouldIgnoreTouch += ShouldIgnoreTouch;
    }

    private static bool ShouldIgnoreForWorld(On.CursorManager.orig_ShouldIgnoreForWorld orig,
                                             CursorManager self, Vector2 position)
        => orig(self, position) || HitWindowAt(position.x / Kx, (ScrH - position.y) / Ky);

    private static bool ShouldIgnoreTouch(On.CursorManager.orig_ShouldIgnoreTouch orig,
                                          CursorManager self, int touchIndex)
    {
        var result = orig(self, touchIndex);
        return result || (self is not null && TouchHitsWindow(self, touchIndex));
    }

    private static bool TouchHitsWindow(CursorManager self, int touchIndex)
    {
        try
        {
            var touch = Input.GetTouch(touchIndex);
            if (Captured.TryGetValue(touch.fingerId, out var captured)) return captured;
            var pos = ImGuiInput.Map(touch.position);
            var owner = UiScroll.Registry.HasPopup || HitWindowAt(pos.X, pos.Y);
            if (touch.phase == TouchPhase.Began) Captured[touch.fingerId] = owner;
            return owner;
        }
        catch (Exception) { return false; }
    }
}
