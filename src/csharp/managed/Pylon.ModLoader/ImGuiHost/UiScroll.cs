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
        public float LastY;
        public float PressedX;
        public float PressedY;
        public bool Pressed;
        public bool Dragging;
        public bool Raw;
        public readonly float[] SampleY = new float[SampleSlots];
        public readonly float[] SampleT = new float[SampleSlots];
        public int SampleHead;
        public int SampleLen;
    }

    private const float DragThreshold = 8f;
    private const float FlingFriction = 3.8f;
    private const float FlingMaxSpeed = 6000f;
    private const float FlingStop = 12f;
    private const int SampleSlots = 8;
    private const float SampleWindow = 0.1f;
    private const float SampleMinSpan = 0.02f;

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
            var now = (float)ImGui.GetTime();
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                st.Dragging = false;
                PushSample(st, now, mouse.Y);
                st.Vel = Math.Clamp(EstimateVelocity(st, now), -FlingMaxSpeed, FlingMaxSpeed);
                st.Pos = Math.Clamp(st.Pos + st.Vel * dt, 0f, max);
                return;
            }
            var dy = mouse.Y - st.LastY;
            st.LastY = mouse.Y;
            st.Pos = Math.Clamp(st.Pos - dy, 0f, max);
            PushSample(st, now, mouse.Y);
            var v = -dy / dt;
            st.Vel += (v - st.Vel) * (1f - MathF.Exp(-dt / 0.05f));
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
                st.SampleHead = 0;
                st.SampleLen = 0;
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

    private static void PushSample(State st, float t, float y)
    {
        st.SampleY[st.SampleHead] = y;
        st.SampleT[st.SampleHead] = t;
        st.SampleHead = (st.SampleHead + 1) % SampleSlots;
        if (st.SampleLen < SampleSlots) st.SampleLen++;
    }

    private static float EstimateVelocity(State st, float now)
    {
        var newest = (st.SampleHead + SampleSlots - 1) % SampleSlots;
        var oldest = -1;
        for (var j = 1; j < st.SampleLen; j++)
        {
            var idx = (st.SampleHead + 2 * SampleSlots - 1 - j) % SampleSlots;
            if (now - st.SampleT[idx] > SampleWindow) break;
            oldest = idx;
        }
        if (oldest < 0) return st.Vel;
        var span = now - st.SampleT[oldest];
        if (span < SampleMinSpan) return st.Vel;
        return -(st.SampleY[newest] - st.SampleY[oldest]) / span;
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
