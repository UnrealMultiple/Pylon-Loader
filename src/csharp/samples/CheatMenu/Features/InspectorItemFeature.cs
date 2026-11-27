using System;
using System.Collections.Generic;
using System.Numerics;
using Pylon.ModLoader.ImGuiHost;
using Terraria;
using CheatMenu.Infra;
using Game = CheatMenu.Infra.Game;
using ImVec2 = System.Numerics.Vector2;

namespace CheatMenu.Features;

/// <summary>
/// 物品检查器。1:1 对应原版 <c>InspectorItemTool</c>（检查器 · 物品）：
/// 掉落物列表 + 图标 + 堆叠/位置/速度，支持传送与移除。
/// </summary>
public sealed class InspectorItemFeature : CheatFeature
{
    private int _index;
    private readonly List<(int Index, string Label)> _entries = new();

    public override string Id => "inspector.item";
    public override string Title => "物品检查器";
    public override CheatCategory Category => CheatCategory.Inspector;
    public override string SubPage => "物品";

    public override void DrawMenu()
    {
        var items = Main.item;
        if (items is null)
        {
            ModernUI.BeginCard("物品检查器", "ITEMS");
            ModernUI.DimText("物品数组未加载");
            ModernUI.EndCard();
            return;
        }

        ModernUI.BeginCard("物品检查器", "ITEMS");

        _entries.Clear();
        for (var i = 0; i < items.Length; i++)
        {
            var it = items[i];
            if (it is null || !it.active) continue;
            var type = it.type;
            var name = SafeName(type);
            _entries.Add((i, $"[{i}] {name} ({type})"));
        }

        if (InspectorCommon.DrawSelectList("##itemSelect", _entries, ref _index))
        {
        }

        if (_index < 0 || _index >= items.Length || items[_index] is not { active: true } item)
        {
            ModernUI.DimText("请在上方选择一个活动物品");
            ModernUI.EndCard();
            return;
        }

        var pos = item.position;
        var vel = item.velocity;
        var speed = vel.Length();

        InspectorCommon.DrawEntityIcon(IconStore.Kind.Item, item.type, 96f);
        ModernUI.Text($"物品[{_index}] \"{SafeName(item.type)}\" ({item.type})");
        ModernUI.BeginRow();
        ModernUI.RowLabel("堆叠", item.stack.ToString());
        ModernUI.EndRow();
        ModernUI.BeginRow();
        ModernUI.RowLabel("位置", $"{pos.X:F1}, {pos.Y:F1}");
        ModernUI.EndRow();
        ModernUI.BeginRow();
        ModernUI.RowLabel("速度", $"{vel.X:F1}, {vel.Y:F1} ({speed:F1})");
        ModernUI.EndRow();

        ModernUI.BeginRow();
        if (ModernUI.GhostButton("传送到此物品"))
            Game.LocalPlayer?.Teleport(new Il2CppMicrosoft.Xna.Framework.Vector2(pos.X / 16f, pos.Y / 16f), 0, 0);
        if (ModernUI.GhostButton("移除物品"))
        {
            item.type = 0;
            item.stack = 0;
        }
        ModernUI.EndRow();

        ModernUI.EndCard();
    }

    private static string SafeName(int type)
    {
        try
        {
            var name = Lang.GetItemNameValue(type);
            return string.IsNullOrEmpty(name) ? "?" : name;
        }
        catch
        {
            return "?";
        }
    }
}
