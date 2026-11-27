using System.Numerics;

namespace Pylon.ModLoader.ImGuiHost;

internal sealed record ScrollSurface(uint Id, Vector2 Min, Vector2 Max, float ScrollY,
    float ScrollMaxY, int Priority, uint? ParentSurface, int Depth, float FontSize, bool Popup)
{
    internal bool Contains(Vector2 p) => p.X >= Min.X && p.Y >= Min.Y && p.X < Max.X && p.Y < Max.Y;
    internal bool IsScrollable => ScrollMaxY > 0f;
    internal float WheelStep => MathF.Max(1f, MathF.Floor(MathF.Min(5f * FontSize, (Max.Y - Min.Y) * .67f)));
}

/// <summary>Double-buffered frame snapshots: DOWN hit-tests the last completed layout.</summary>
internal sealed class ScrollSurfaceRegistry
{
    private Dictionary<uint, ScrollSurface> _previous = new();
    private Dictionary<uint, ScrollSurface> _current = new();
    internal IEnumerable<ScrollSurface> Surfaces => _previous.Values;
    internal bool HasPopup => _previous.Values.Any(s => s.Popup);
    internal void BeginFrame() => _current.Clear();
    internal void EndFrame() => (_previous, _current) = (_current, _previous);
    internal void Clear() { _previous.Clear(); _current.Clear(); }
    internal ScrollSurface? Find(uint id) => _previous.GetValueOrDefault(id);
    internal void Register(uint id, Vector2 pos, Vector2 size, float y, float max, int priority,
        uint? parent, float fontSize, bool popup)
    {
        var depth = 0;
        var end = pos + size;
        if (parent.HasValue && _current.TryGetValue(parent.Value, out var p))
        {
            pos = Vector2.Max(pos, p.Min);
            end = Vector2.Min(end, p.Max);
            depth = p.Depth + 1;
            priority = Math.Max(priority, p.Priority);
            popup |= p.Popup;
        }
        _current[id] = new(id, pos, end, y, max, priority, parent, depth, fontSize, popup);
    }
    internal void UpdateScroll(uint id, float y, float max)
    {
        if (_current.TryGetValue(id, out var s)) _current[id] = s with { ScrollY = y, ScrollMaxY = max };
    }
    internal ScrollSurface? Hit(Vector2 pos)
    {
        // Popup presence blocks all lower surfaces, including outside-click close gestures.
        return _previous.Values.Where(s => s.Contains(pos) && (!HasPopup || s.Popup))
            .OrderByDescending(s => s.Priority).ThenByDescending(s => s.Depth)
            .FirstOrDefault();
    }
}
