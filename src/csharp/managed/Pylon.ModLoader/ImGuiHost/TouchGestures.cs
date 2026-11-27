using System.Numerics;
using Pylon.NaviteAPI.ImGui;

namespace Pylon.ModLoader.ImGuiHost;

internal enum TouchGesturePhase { Idle, PendingTap, ScrollCandidate, Scrolling, ControlCaptured, Released }
internal enum GestureOwnerKind { None, PopupScrolling, PageScrolling, NavScrolling, SliderDragging, ControlCaptured }

/// <summary>One arbiter for a complete finger sequence. Native ImGui owns every scroll position.</summary>
internal static class TouchGestures
{
    private sealed record Control(uint Id, Vector2 Min, Vector2 Max);
    private static List<Control> _controls = new(), _nextControls = new();
    internal static TouchGesturePhase Phase { get; private set; }
    internal static GestureOwnerKind OwnerKind { get; private set; }
    internal static uint OwnerId { get; private set; }
    private static bool _cancelClick;
    internal static bool SuppressClick => _cancelClick;
    private static Vector2 _down, _last;
    private static ScrollSurface? _candidate;
    private static uint _slider;
    private static float _velocity, _idleTime;
    private static uint _inertiaOwner;
    private static float _pendingWheel;

    // Velocity uses the same sign as the pixel wheel delta, including after release.
    private const float MaxFlingSpeed = 2600f;
    private const float MinFlingSpeed = 260f;

    internal static void BeginFrame()
    {
        if (Phase == TouchGesturePhase.Released && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            Phase = TouchGesturePhase.Idle;
            _cancelClick = false;
            OwnerKind = GestureOwnerKind.None;
            OwnerId = 0;
            if (_inertiaOwner == 0) ImGuiNativeApi.SetTouchScrollOwner(0);
        }
        _nextControls.Clear();
        _pendingWheel = 0f;
    }
    internal static void AfterNewFrame()
    {
        if (!_cancelClick || ImGui.GetIO().MouseSource != ImGuiMouseSource.TouchScreen) return;
        ImGuiNativeApi.CancelTouchClick();
        // Selectable may use release-only activation without an ActiveId. Consume that release too.
        ImGui.GetIO().MouseReleased[0] = false;
        ImGui.GetIO().MouseClicked[0] = false;
    }
    internal static void EndFrame() => (_controls, _nextControls) = (_nextControls, _controls);
    internal static void RegisterSlider()
        => _nextControls.Add(new(ImGui.GetItemID(), ImGui.GetItemRectMin(), ImGui.GetItemRectMax()));
    internal static bool SliderMayChange(uint id)
        => !ImGuiInput.HasPointer || (Phase == TouchGesturePhase.ControlCaptured && _slider == id);
    internal static void CaptureControl(uint id)
    {
        if (Phase != TouchGesturePhase.PendingTap) return;
        Phase = TouchGesturePhase.ControlCaptured;
        OwnerId = id;
        OwnerKind = GestureOwnerKind.ControlCaptured;
    }
    internal static void Down(Vector2 pos)
    {
        StopInertia();
        _cancelClick = false;
        Phase = TouchGesturePhase.PendingTap;
        OwnerKind = GestureOwnerKind.None;
        OwnerId = 0;
        _down = _last = pos;
        _velocity = _idleTime = 0f;
        _candidate = UiScroll.Registry.Hit(pos);
        // Empty nested surfaces may pass ownership to a scrollable ancestor at DOWN only.
        while (_candidate is { IsScrollable: false, ParentSurface: not null })
            _candidate = UiScroll.Registry.Find(_candidate.ParentSurface.Value);
        _slider = _controls.LastOrDefault(c => pos.X >= c.Min.X && pos.Y >= c.Min.Y &&
            pos.X < c.Max.X && pos.Y < c.Max.Y && (!UiScroll.Registry.HasPopup || _candidate?.Popup == true))?.Id ?? 0;
    }
    internal static void Move(Vector2 pos, float dt)
    {
        var displacement = pos - _down;
        var dy = pos.Y - _last.Y;
        _last = pos;
        dt = Math.Clamp(dt, .001f, .1f);
        if (MathF.Abs(dy) < .1f) _idleTime += dt; else _idleTime = 0f;
        if (Phase == TouchGesturePhase.PendingTap && displacement.Length() > TerrariaTheme.Px(8f))
        {
            if (_slider != 0 && MathF.Abs(displacement.X) > MathF.Abs(displacement.Y))
            {
                Phase = TouchGesturePhase.ControlCaptured;
                OwnerKind = GestureOwnerKind.SliderDragging;
                OwnerId = _slider;
            }
            else
            {
                _cancelClick = true;
                Phase = TouchGesturePhase.ScrollCandidate;
                ImGuiNativeApi.CancelTouchClick();
                if (_candidate is { IsScrollable: true } surface)
                {
                    Phase = TouchGesturePhase.Scrolling;
                    OwnerId = surface.Id;
                    OwnerKind = surface.Popup ? GestureOwnerKind.PopupScrolling :
                        surface.Priority == UiScroll.NavigationPriority ? GestureOwnerKind.NavScrolling : GestureOwnerKind.PageScrolling;
                    ImGuiNativeApi.SetTouchScrollOwner(OwnerId);
                    // Do NOT replay the whole pending displacement here: that is the visible
                    // "jumps down" the moment a swipe is recognized. Scrolling starts from
                    // this frame's own delta, so content stays glued to the finger.
                    dy = 0f;
                }
            }
        }
        if (Phase != TouchGesturePhase.Scrolling) return;
        ImGuiNativeApi.CancelTouchClick();
        var owner = UiScroll.Registry.Find(OwnerId);
        if (owner == null) { _velocity = 0f; return; }
        // Touch wheel units are pixels: the native touch path treats 1 unit as 1 pixel.
        _pendingWheel += dy;
        var speed = Math.Clamp(dy / dt, -MaxFlingSpeed, MaxFlingSpeed);
        _velocity += (speed - _velocity) * (1f - MathF.Exp(-dt / .04f));
    }
    internal static void Up(bool canceled)
    {
        _cancelClick |= canceled;
        if (SuppressClick) ImGuiNativeApi.CancelTouchClick();
        var fling = canceled || Phase != TouchGesturePhase.Scrolling || _idleTime > 0.06f
            ? 0f
            : _velocity;
        if (MathF.Abs(fling) >= MinFlingSpeed)
        {
            _velocity = fling;
            _inertiaOwner = OwnerId;
        }
        else
        {
            _inertiaOwner = 0;
            _velocity = 0f;
        }
        // The release frame can still contain a final pixel delta. Keep its owner
        // until the next BeginFrame, after NewFrame has consumed that wheel event.
        Phase = TouchGesturePhase.Released;
        _slider = 0;
        // Keep release ownership until it has been submitted; never retarget this UP.
    }
    internal static void FeedWheel(ImGuiIOPtr io, float dt)
    {
        if (_inertiaOwner != 0 && Phase != TouchGesturePhase.Released)
        {
            var s = UiScroll.Registry.Find(_inertiaOwner);
            if (s == null || (UiScroll.Registry.HasPopup && !s.Popup) || MathF.Abs(_velocity) < 12f ||
                (_velocity > 0f && s.ScrollY <= 0f) || (_velocity < 0f && s.ScrollY >= s.ScrollMaxY))
                StopInertia();
            else
            {
                ImGuiNativeApi.SetTouchScrollOwner(s.Id);
                _pendingWheel += _velocity * dt;
                _velocity *= MathF.Exp(-3.8f * dt);
            }
        }
        if (_pendingWheel != 0f)
        {
            io.AddMouseSourceEvent(ImGuiMouseSource.TouchScreen);
            io.AddMouseWheelEvent(0f, _pendingWheel);
        }
    }
    internal static void StopInertia()
    {
        _inertiaOwner = 0;
        _velocity = 0f;
        ImGuiNativeApi.SetTouchScrollOwner(0);
    }
    internal static void Reset()
    {
        StopInertia();
        Phase = TouchGesturePhase.Idle;
        OwnerKind = GestureOwnerKind.None;
        OwnerId = 0;
        _controls.Clear();
        _nextControls.Clear();
    }
}
