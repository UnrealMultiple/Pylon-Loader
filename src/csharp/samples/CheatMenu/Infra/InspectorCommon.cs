using System;
using System.Collections.Generic;
using System.Numerics;
using Pylon.NaviteAPI.ImGui;

namespace CheatMenu.Infra;

/// <summary>
/// Inspector 共用小工具：活动实体下拉选择列表、图标绘制。
/// 1:1 对应原版 <c>InspectorCommon.hpp</c>（Combo+Selectable 列表，适合触屏）。
/// </summary>
internal static class InspectorCommon
{
    public static bool DrawSelectList(string id, IReadOnlyList<(int Index, string Label)> entries,
        ref int selectedIndex)
    {
        if (entries.Count == 0) return false;

        var preview = 0;
        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].Index == selectedIndex)
            {
                preview = i;
                break;
            }
        }

        var changed = false;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.BeginCombo(id, entries[preview].Label))
        {
            for (var i = 0; i < entries.Count; i++)
            {
                var isSelected = entries[i].Index == selectedIndex;
                if (ImGui.Selectable(entries[i].Label, isSelected))
                {
                    selectedIndex = entries[i].Index;
                    changed = true;
                }
                if (isSelected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        return changed;
    }

    /// <summary>实体图标：先占位保持布局稳定，纹理就绪后等比缩放居中绘制。</summary>
    public static void DrawEntityIcon(IconStore.Kind kind, int typeId, float maxSize)
    {
        ImGui.Dummy(new Vector2(maxSize, maxSize));
        if (typeId <= 0) return;
        if (IconStore.TryGet(kind, typeId, out var tex, out var w, out var h) && w > 0 && h > 0)
        {
            var scale = MathF.Min(maxSize / w, maxSize / h);
            var dw = w * scale;
            var dh = h * scale;
            var min = ImGui.GetItemRectMin();
            var x0 = min.X + (maxSize - dw) * 0.5f;
            var y0 = min.Y + (maxSize - dh) * 0.5f;
            ImGui.GetWindowDrawList().AddImage(tex,
                new Vector2(x0, y0), new Vector2(x0 + dw, y0 + dh),
                new Vector2(0f, 0f), new Vector2(1f, 1f), 0xFFFFFFFFu);
        }
    }

    /// <summary>速度方向箭头（红色线 + 三角），画在当前文本行右侧。</summary>
    public static void DrawVelocityArrow(Vector2 velocity)
    {
        var len = velocity.Length();
        if (len <= 0f) return;

        var nx = velocity.X / len;
        var ny = velocity.Y / len;
        // Terraria: velocity.SafeNormalize(Zero).AngleTo(Zero) = atan2(-ny, -nx) + PI/2
        var angle = MathF.Atan2(-ny, -nx) + 1.5707963267948966f;

        var rmin = ImGui.GetItemRectMin();
        var rmax = ImGui.GetItemRectMax();
        var center = new Vector2(rmax.X, rmin.Y + (rmax.Y - rmin.Y) * 0.5f);
        var l = ImGui.GetTextLineHeight();

        var ca = MathF.Cos(angle);
        var sa = MathF.Sin(angle);
        Vector2 Tr(float x, float y) => new(
            center.X + x * ca - y * sa,
            center.Y + x * sa + y * ca);

        var head = Tr(0f, l * 0.5f);
        var tail = Tr(0f, -l * 0.5f);
        var t2 = Tr(l / 7f, l * 0.25f);
        var t3 = Tr(-l / 7f, l * 0.25f);

        var dl = ImGui.GetWindowDrawList();
        var red = 0xFF0000FFu;   // ABGR: 红
        dl.AddLine(head, tail, red, 1f);
        dl.AddTriangleFilled(head, t2, t3, red);
    }
}
