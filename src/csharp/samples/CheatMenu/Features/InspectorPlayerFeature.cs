using System;
using System.Collections.Generic;
using System.Numerics;
using Il2CppMicrosoft.Xna.Framework;
using Pylon.NaviteAPI.ImGui;
using Pylon.ModLoader.ImGuiHost;
using Terraria;
using CheatMenu.Infra;
using Game = CheatMenu.Infra.Game;
using XnaVector2 = Il2CppMicrosoft.Xna.Framework.Vector2;
using ImVec2 = System.Numerics.Vector2;

namespace CheatMenu.Features;

/// <summary>
/// 玩家检查器。1:1 对应原版 <c>InspectorPlayerTool</c>（检查器 · 玩家）：
/// 玩家列表 + 属性 + 速度方向箭头 + 背包/盔甲图标网格（点击查看 tooltip）。
/// </summary>
public sealed class InspectorPlayerFeature : CheatFeature
{
    private int _index;
    private readonly List<(int Index, string Label)> _entries = new();

    public override string Id => "inspector.player";
    public override string Title => "玩家检查器";
    public override CheatCategory Category => CheatCategory.Inspector;
    public override string SubPage => "玩家";

    public override void DrawMenu()
    {
        var players = Main.player;
        if (players is null)
        {
            ModernUI.BeginCard("玩家检查器", "PLAYERS");
            ModernUI.DimText("玩家数组未加载");
            ModernUI.EndCard();
            return;
        }

        ModernUI.BeginCard("玩家检查器", "PLAYERS");

        _entries.Clear();
        for (var i = 0; i < players.Length; i++)
        {
            var p = players[i];
            if (p is null || !p.active) continue;
            var entryName = string.IsNullOrEmpty(p.name) ? "玩家" : p.name;
            _entries.Add((i, $"[{i}] {entryName}{(i == Main.myPlayer ? " [本机]" : string.Empty)}"));
        }

        InspectorCommon.DrawSelectList("##playerSelect", _entries, ref _index);

        if (_index < 0 || _index >= players.Length || players[_index] is not { active: true } player)
        {
            ModernUI.DimText("请在上方选择一个活动玩家");
            ModernUI.EndCard();
            return;
        }

        var pos = player.position;
        var vel = player.velocity;
        var speed = vel.Length();
        var name = string.IsNullOrEmpty(player.name) ? "玩家" : player.name;
        var heldType = player.HeldItem?.type ?? 0;

        ModernUI.Text($"玩家[{_index}] {name} (whoAmI={player.whoAmI}){(player.whoAmI == Main.myPlayer ? " [本机]" : string.Empty)}");
        InfoRow("生命", $"{player.statLife}/{player.statLifeMax2}");
        InfoRow("魔力", $"{player.statMana}/{player.statManaMax2}");
        InfoRow("防御", player.statDefense.ToString());
        InfoRow("手持物品", heldType.ToString());
        InfoRow("位置", $"{pos.X:F1}, {pos.Y:F1}");
        InfoRow("速度", $"{vel.X:F1}, {vel.Y:F1} ({speed:F1})");
        InfoRow("速度方向", string.Empty);
        InspectorCommon.DrawVelocityArrow(new ImVec2(vel.X, vel.Y));

        if (ModernUI.GhostButton("传送到此玩家", default, ModernUI.Icon.None, rightAlign: true))
            Game.LocalPlayer?.Teleport(new XnaVector2(pos.X / 16f, pos.Y / 16f), 0, 0);

        DrawInventorySection(player);

        ModernUI.EndCard();
    }

    private void DrawInventorySection(Player player)
    {
        ModernUI.SectionTitle("玩家背包");

        if (player.inventory is not null)
        {
            ModernUI.BeginRow();
            ModernUI.RowLabel("物品栏", $"{player.inventory.Length} 格");
            ModernUI.EndRow();
            DrawItemSlots(player.inventory, 10);
        }
        if (player.armor is not null)
        {
            ModernUI.BeginRow();
            ModernUI.RowLabel("盔甲/配饰", $"{player.armor.Length} 格");
            ModernUI.EndRow();
            DrawItemSlots(player.armor, 10);
        }
    }

    private void DrawItemSlots(Item[] items, int cols)
    {
        const float iconSize = 72f;
        for (var i = 0; i < items.Length; i++)
        {
            ImGui.PushID(i.ToString());
            var item = items[i];
            var type = item?.type ?? 0;
            var stack = item?.stack ?? 0;

            ImGui.Dummy(new ImVec2(iconSize, iconSize));
            var min = ImGui.GetItemRectMin();
            if (type > 0 &&
                IconStore.TryGet(IconStore.Kind.Item, type, out var tex, out var iw, out var ih))
            {
                var k = MathF.Min(iconSize / iw, iconSize / ih);
                var dw = iw * k;
                var dh = ih * k;
                ImGui.GetWindowDrawList().AddImage(tex,
                    new ImVec2(min.X + (iconSize - dw) * 0.5f, min.Y + (iconSize - dh) * 0.5f),
                    new ImVec2(min.X + (iconSize - dw) * 0.5f + dw, min.Y + (iconSize - dh) * 0.5f + dh),
                    new ImVec2(0f, 0f), new ImVec2(1f, 1f), 0xFFFFFFFFu);
            }
            ImGui.PopID();

            if (ImGui.IsItemHovered() && type > 0)
                ImGui.SetTooltip($"[{i}] {ItemName(type)} x{stack}");

            if ((i + 1) % cols != 0 && (i + 1) < items.Length)
                ImGui.SameLine();
        }
    }

    private static string ItemName(int type)
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

    private static void InfoRow(string label, string value)
    {
        ModernUI.BeginRow();
        ModernUI.RowLabel(label, value);
        ModernUI.EndRow();
    }
}
