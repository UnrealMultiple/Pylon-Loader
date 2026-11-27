using System;
using System.Collections.Generic;
using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader.ImGuiHost;
using Terraria;
using CheatMenu.Infra;
using Game = CheatMenu.Infra.Game;
using XnaVector2 = Il2CppMicrosoft.Xna.Framework.Vector2;
using ImVec2 = System.Numerics.Vector2;

namespace CheatMenu.Features;

/// <summary>
/// NPC 检查器。1:1 对应原版 <c>InspectorNpcTool</c>（检查器 · NPC）：
/// 活动 NPC 列表 + 图标 + 生命/防御/属性 + 速度方向箭头，支持传送与击杀。
/// </summary>
public sealed class InspectorNpcFeature : CheatFeature
{
    private int _index;
    private readonly List<(int Index, string Label)> _entries = new();

    public override string Id => "inspector.npc";
    public override string Title => "NPC检查器";
    public override CheatCategory Category => CheatCategory.Inspector;
    public override string SubPage => "NPC";

    public override void DrawMenu()
    {
        var npcs = Main.npc;
        if (npcs is null)
        {
            ModernUI.BeginCard("NPC检查器", "NPC");
            ModernUI.DimText("NPC数组未加载");
            ModernUI.EndCard();
            return;
        }

        ModernUI.BeginCard("NPC检查器", "NPC");

        _entries.Clear();
        for (var i = 0; i < npcs.Length; i++)
        {
            var n = npcs[i];
            if (n is null || !n.active) continue;
            var type = n.type;
            var entryName = NpcName(type);
            _entries.Add((i, $"[{i}] {entryName} (类型 {type}){(n.boss ? " [Boss]" : string.Empty)}"));
        }

        InspectorCommon.DrawSelectList("##npcSelect", _entries, ref _index);

        if (_index < 0 || _index >= npcs.Length || npcs[_index] is not { active: true } npc)
        {
            ModernUI.DimText("请在上方选择一个活动 NPC");
            ModernUI.EndCard();
            return;
        }

        var pos = npc.position;
        var vel = npc.velocity;
        var speed = vel.Length();
        var name = NpcName(npc.type);

        InspectorCommon.DrawEntityIcon(IconStore.Kind.Npc, npc.type, 96f);
        ModernUI.Text($"NPC[{_index}] {name} (类型 {npc.type})");
        InfoRow("生命", $"{npc.life}/{npc.lifeMax}");
        InfoRow("防御", npc.defense.ToString());
        InfoRow("友好", npc.friendly ? "是" : "否", "城镇", npc.townNPC ? "是" : "否", "Boss", npc.boss ? "是" : "否");
        InfoRow("位置", $"{pos.X:F1}, {pos.Y:F1}");
        InfoRow("速度", $"{vel.X:F1}, {vel.Y:F1} ({speed:F1})");
        InfoRow("速度方向", string.Empty);
        InspectorCommon.DrawVelocityArrow(new ImVec2(vel.X, vel.Y));

        ModernUI.BeginRow();
        if (ModernUI.GhostButton("传送到此NPC"))
            Game.LocalPlayer?.Teleport(new XnaVector2(pos.X / 16f, pos.Y / 16f), 0, 0);
        if (ModernUI.GhostButton("击杀NPC"))
            npc.StrikeNPCNoInteraction(npc.life + npc.defense, 0f, 0);
        ModernUI.EndRow();

        ModernUI.EndCard();
    }

    private static string NpcName(int type)
    {
        try
        {
            var name = Lang.GetNPCNameValue(type);
            return string.IsNullOrEmpty(name) ? "?" : name;
        }
        catch
        {
            return "?";
        }
    }

    private static void InfoRow(string label, string value, string label2 = null, string value2 = null, string label3 = null, string value3 = null)
    {
        ModernUI.BeginRow();
        ModernUI.RowLabel(label, value);
        if (label2 is not null) ModernUI.RowLabel(label2, value2, 70f);
        if (label3 is not null) ModernUI.RowLabel(label3, value3, 70f);
        ModernUI.EndRow();
    }
}
