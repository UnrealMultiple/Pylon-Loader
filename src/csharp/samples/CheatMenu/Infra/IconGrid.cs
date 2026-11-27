using System;
using System.Collections.Generic;
using System.Numerics;
using Pylon.NaviteAPI.ImGui;

namespace CheatMenu.Infra;

public static class IconGrid
{
    private const float Gap = 4f;

    private static readonly Dictionary<int, string> IdLabels = new();
    private static readonly Dictionary<uint, int> GridColumns = new();

    public static void DrawVirtualGrid(IReadOnlyList<(int Id, string Name)> items,
        float buttonSize, float iconSize, Action<int, string> drawIcon, Action<int, string> onClick,
        bool showTooltip = false)
    {
        if (items is null || items.Count == 0) return;

        var availWidth = ImGui.GetContentRegionAvail().X;
        var columns = Math.Max(1, (int)((availWidth + Gap) / (buttonSize + Gap)));
        var rows = (items.Count + columns - 1) / columns;
        var rowHeight = buttonSize + Gap;
        var totalHeight = rows * rowHeight - Gap;

        var key = ImGui.GetID("##virtual_grid_layout");
        if (GridColumns.TryGetValue(key, out var oldColumns) && oldColumns != columns)
            ImGui.SetScrollY(0f); // Explicit reflow only, not a scrolling controller.
        GridColumns[key] = columns;

        var start = ImGui.GetCursorPos();
        var screenStart = ImGui.GetCursorScreenPos();
        var windowMin = ImGui.GetWindowPos();
        var windowMax = windowMin + ImGui.GetWindowSize();
        // Visible interval relative to this grid, not absolute child ScrollY.
        var visibleTop = MathF.Max(0f, windowMin.Y - screenStart.Y);
        var visibleBottom = MathF.Max(0f, windowMax.Y - screenStart.Y);
        var firstRow = Math.Clamp((int)(visibleTop / rowHeight) - 1, 0, rows);
        var lastRow = Math.Clamp((int)(visibleBottom / rowHeight) + 2, firstRow, rows);

        for (var row = firstRow; row < lastRow; row++)
        {
            for (var col = 0; col < columns; col++)
            {
                var index = row * columns + col;
                if (index >= items.Count) break;
                ImGui.SetCursorPos(start + new Vector2(col * (buttonSize + Gap), row * rowHeight));
                var (id, name) = items[index];
                if (ButtonWithIcon(id, name, buttonSize, iconSize, drawIcon, showTooltip))
                    onClick?.Invoke(id, name);
            }
        }

        // Always submit an item at the full content bottom, including the last visible row.
        // SetCursorPos alone does not extend ImGui's CursorMaxPos.
        ImGui.SetCursorPos(start);
        ImGui.Dummy(new Vector2(availWidth, totalHeight));
    }
    public static bool ButtonWithIcon(int id, string name, float buttonSize, float iconSize,
        Action<int, string> drawIcon, bool showTooltip = false)
    {
        ImGui.PushID(LabelFor(id));
        var clicked = ImGui.Button(string.Empty, new Vector2(buttonSize, buttonSize));
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        ImGui.PopID();

        drawIcon?.Invoke(id, name);

        if (showTooltip && ImGui.IsMouseHoveringRect(min, max))
            ImGui.SetTooltip($"{id} {name}");
        return clicked;
    }

    private static string LabelFor(int id)
    {
        if (IdLabels.TryGetValue(id, out var label)) return label;
        label = id.ToString();
        IdLabels[id] = label;
        return label;
    }

    public static void DrawIconContained(ulong texture, int iconW, int iconH, Vector2 min, Vector2 max)
    {
        if (texture == 0 || iconW <= 0 || iconH <= 0) return;

        var boxW = max.X - min.X;
        var boxH = max.Y - min.Y;
        var scale = MathF.Min(boxW / iconW, boxH / iconH);
        var drawW = iconW * scale;
        var drawH = iconH * scale;
        var x0 = min.X + (boxW - drawW) * 0.5f;
        var y0 = min.Y + (boxH - drawH) * 0.5f;
        ImGui.GetWindowDrawList().AddImage(texture,
            new Vector2(x0, y0), new Vector2(x0 + drawW, y0 + drawH),
            new Vector2(0f, 0f), new Vector2(1f, 1f), 0xFFFFFFFFu);
    }
}