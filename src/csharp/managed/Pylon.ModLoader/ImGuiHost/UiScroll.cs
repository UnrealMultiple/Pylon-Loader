using System.Numerics;
using Pylon.NaviteAPI.ImGui;

namespace Pylon.ModLoader.ImGuiHost;

/// <summary>
/// 滚动容器：自己持有权威滚动位置（拖动/惯性都在自己的状态上推进），每帧一次性
/// SetScrollY 写给内核；不读拖动中的 GetScrollY，避免与内核滚动状态互相打架产生回弹。
/// 滑条/下拉等横向拖动控件按住时锁定不滚动；拖动期间指针钉远防误触。
/// </summary>
internal static class UiScroll
{
    private sealed class State
    {
        public float Pos;
        public float Vel;
        public float LastDelta;
        public float LastY;
        public float PressedX;
        public float PressedY;
        public bool Pressed;
        public bool Dragging;
        public bool Raw;
    }

    private const float DragThreshold = 8f;
    private const float FlingFriction = 4.5f;
    private const float FlingMaxSpeed = 3600f;
    private const float FlingStop = 20f;

    private static readonly Dictionary<uint, State> States = [];
    private static State? _activeState;
    private static bool _hasActive;

    internal static bool DragLock { get; private set; }
    private static bool _prevDragLock;

    internal static void KeepDragLock(bool active)
    {
        if (active) DragLock = true;
    }

    internal static void Begin(string id, Vector2 size, Vector2 pad, bool reset = false)
    {
        _prevDragLock = DragLock;
        DragLock = false;
        var key = ImGui.GetID(id);
        if (!States.TryGetValue(key, out var st))
        {
            st = new State();
            States[key] = st;
        }
        _activeState = st;
        _hasActive = true;

        TerrariaTheme.PushVec2(ImGuiStyleVar.WindowPadding, pad);
        TerrariaTheme.PushVec2(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
        ImGui.BeginChild(id, size, ImGuiChildFlags.AlwaysUseWindowPadding,
            ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoScrollbar);
        if (reset) st.Pos = 0f;
        Advance(st);
        TickTouch(st);
        ImGui.SetScrollY(st.Pos);
    }

    private static void Advance(State st)
    {
        var max = ImGui.GetScrollMaxY();
        var dt = MathF.Max(ImGui.GetIO().DeltaTime, 0.001f);
        if (!st.Dragging && !st.Pressed)
        {
            if (MathF.Abs(st.Vel) > FlingStop)
            {
                st.Pos += st.Vel * dt;
                st.Vel *= MathF.Exp(-FlingFriction * dt);
            }
            else
            {
                st.Vel = 0f;
                st.Pos = ImGui.GetScrollY();
            }
        }
        st.Pos = Math.Clamp(st.Pos, 0f, max);
    }

    private static void TickTouch(State st)
    {
        var max = ImGui.GetScrollMaxY();
        var dt = MathF.Max(ImGui.GetIO().DeltaTime, 0.001f);
        var useRaw = ImGuiInput.HasPointer;
        var mouse = st.Raw ? ImGuiInput.PointerPos : ImGui.GetMousePos();
        var down = st.Raw ? ImGuiInput.PointerDown || ImGui.IsMouseDown(ImGuiMouseButton.Left)
                          : ImGui.IsMouseDown(ImGuiMouseButton.Left);
        if (st.Raw && !ImGuiInput.HasPointer)
        {
            ImGuiInput.PinPageScroll = false;
            if (!st.Dragging && !st.Pressed) return;
        }
        var hovered = ImGui.IsWindowHovered();
        ImGuiInput.PinPageScroll = useRaw && st.Dragging;

        if (st.Dragging)
        {
            var released = !ImGui.IsMouseDown(ImGuiMouseButton.Left);
            if (released)
            {
                st.Dragging = false;
                st.Vel = Math.Clamp(st.Vel, -FlingMaxSpeed, FlingMaxSpeed);
                return;
            }
            var dy = mouse.Y - st.LastY;
            st.LastDelta = dy;
            st.LastY = mouse.Y;
            st.Pos = Math.Clamp(st.Pos - dy, 0f, max);
            var v = -dy / dt;
            st.Vel = MathF.Abs(dy) < 0.01f ? st.Vel * 0.6f : st.Vel + (v - st.Vel) * 0.45f;
            return;
        }

        if (down && hovered && !_prevDragLock)
        {
            if (!st.Pressed)
            {
                st.Pressed = true;
                st.Raw = useRaw;
                st.PressedX = mouse.X;
                st.PressedY = mouse.Y;
                st.LastY = mouse.Y;
                st.Vel = 0f;
            }
            else if (MathF.Abs(mouse.X - st.PressedX) + MathF.Abs(mouse.Y - st.PressedY) > TerrariaTheme.Px(DragThreshold))
            {
                st.Dragging = true;
                st.LastY = mouse.Y;
                st.Vel = 0f;
            }
            return;
        }

        st.Pressed = false;
    }

    internal static void End()
    {
        if (_hasActive && _activeState != null)
        {
            var st = _activeState;
            var max = ImGui.GetScrollMaxY();
            if (max > 1f)
            {
                var dl = ImGui.GetWindowDrawList();
                var pad = ImGui.GetStyle().WindowPadding.Y;
                var height = ImGui.GetWindowHeight() - pad * 2f;
                var x = ImGui.GetWindowPos().X + ImGui.GetWindowWidth() - TerrariaTheme.Px(6f);
                var y0 = ImGui.GetWindowPos().Y + pad;
                var thumbH = MathF.Max(height * height / (height + max), TerrariaTheme.Px(28f));
                var y = y0 + (height - thumbH) * Math.Clamp(st.Pos / max, 0f, 1f);
                var col = st.Dragging || MathF.Abs(st.Vel) > 1f ? UiTheme.Accent : UiTheme.FieldStroke;
                dl.AddRectFilled(new Vector2(x, y), new Vector2(x + TerrariaTheme.Px(3f), y + thumbH),
                    TerrariaTheme.U32(col), TerrariaTheme.Px(2f));
            }
        }
        _hasActive = false;
        _activeState = null;
        ImGui.EndChild();
        ImGui.PopStyleVar(4);
    }
}
