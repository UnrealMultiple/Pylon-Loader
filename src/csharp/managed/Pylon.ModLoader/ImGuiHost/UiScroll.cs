using System.Numerics;
using Pylon.NaviteAPI.ImGui;

namespace Pylon.ModLoader.ImGuiHost;

/// <summary>Registers native ImGui windows. Only TouchGestures arbitrates input; scopes never poll fingers.</summary>
internal static class UiScroll
{
    internal const int NavigationPriority = 10;
    internal const int PagePriority = 20;
    internal const int PopupPriority = 100;

    private readonly record struct Scope(uint Id, bool Child);
    private static readonly Stack<Scope> Scopes = new();
    internal static readonly ScrollSurfaceRegistry Registry = new();

    internal static void BeginFrame() => Registry.BeginFrame();
    internal static void EndFrame() => Registry.EndFrame();

    internal static void Reset()
    {
        Scopes.Clear();
        Registry.Clear();
        TouchGestures.Reset();
    }

    internal static void Begin(string id, Vector2 size, Vector2 pad, bool reset = false,
        bool popupSurface = false, int priority = PagePriority)
    {
        TerrariaTheme.PushVec2(ImGuiStyleVar.WindowPadding, pad);
        TerrariaTheme.PushVec2(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
        ImGui.BeginChild(id, size, ImGuiChildFlags.AlwaysUseWindowPadding,
            ImGuiWindowFlags.NoSavedSettings);
        BeginSurface(popupSurface ? PopupPriority : priority, child: true);
        if (reset)
        {
            TouchGestures.StopInertia();
            // An explicit content/page reset, never a per-frame authoritative scroll position.
            ImGui.SetScrollY(0f);
        }
    }

    internal static void BeginSurface(int priority = PopupPriority, bool child = false)
    {
        var id = ImGuiNativeApi.CurrentWindowId();
        uint? parent = priority >= PopupPriority || Scopes.Count == 0 ? null : Scopes.Peek().Id;
        Registry.Register(id, ImGui.GetWindowPos(), ImGui.GetWindowSize(),
            ImGui.GetScrollY(), ImGui.GetScrollMaxY(), priority, parent,
            ImGui.GetFontSize(), priority >= PopupPriority);
        Scopes.Push(new Scope(id, child));
    }

    internal static void EndSurface()
    {
        var scope = Scopes.Pop();
        Registry.UpdateScroll(scope.Id, ImGui.GetScrollY(), ImGui.GetScrollMaxY());
    }

    internal static void End()
    {
        EndSurface();
        ImGui.EndChild();
        ImGui.PopStyleVar(4); // PushVec2 pushes separate X and Y entries.
    }

    // Compatibility for third-party mods: capture the last active control, not a global drag lock.
    internal static void KeepDragLock(bool active)
    {
        if (active) TouchGestures.CaptureControl(ImGui.GetItemID());
    }
}
