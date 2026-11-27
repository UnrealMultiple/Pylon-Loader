using System.Numerics;

namespace Pylon.ModLoader.ImGuiHost;

public static class ModScrollArea
{
    private static Vector2 Pad => new(TerrariaTheme.Px(12f), TerrariaTheme.Px(10f));

    public static void Begin(string id, Vector2 size, bool reset = false)
        => UiScroll.Begin(id, size, Pad, reset);

    public static void End() => UiScroll.End();

    /// <summary>Cancel outstanding inertia when a mod changes the content of an existing page.</summary>
    public static void StopInertia() => TouchGestures.StopInertia();

    public static void KeepDragLock(bool active) => UiScroll.KeepDragLock(active);
}