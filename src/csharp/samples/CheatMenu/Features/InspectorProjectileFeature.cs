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
/// 弹幕检查器。1:1 对应原版 <c>InspectorProjectileTool</c>（检查器 · 弹幕）：
/// 活动弹幕列表 + 图标 + 伤害/时间/拥有者 + 速度方向箭头，支持传送与移除。
/// </summary>
public sealed class InspectorProjectileFeature : CheatFeature
{
    private int _index;
    private readonly List<(int Index, string Label)> _entries = new();

    public override string Id => "inspector.projectile";
    public override string Title => "弹幕检查器";
    public override CheatCategory Category => CheatCategory.Inspector;
    public override string SubPage => "弹幕";

    public override void DrawMenu()
    {
        var projectiles = Main.projectile;
        if (projectiles is null)
        {
            ModernUI.BeginCard("弹幕检查器", "PROJECTILES");
            ModernUI.DimText("弹幕数组未加载");
            ModernUI.EndCard();
            return;
        }

        ModernUI.BeginCard("弹幕检查器", "PROJECTILES");

        _entries.Clear();
        for (var i = 0; i < projectiles.Length; i++)
        {
            var p = projectiles[i];
            if (p is null || !p.active) continue;
            var type = p.type;
            var entryName = ProjName(p);
            _entries.Add((i, $"[{i}] {entryName} (类型 {type}){(p.hostile ? " [敌]" : string.Empty)}"));
        }

        InspectorCommon.DrawSelectList("##projSelect", _entries, ref _index);

        if (_index < 0 || _index >= projectiles.Length ||
            projectiles[_index] is not { active: true } proj)
        {
            ModernUI.DimText("请在上方选择一个活动弹幕");
            ModernUI.EndCard();
            return;
        }

        var pos = proj.position;
        var vel = proj.velocity;
        var speed = vel.Length();
        var name = ProjName(proj);

        InspectorCommon.DrawEntityIcon(IconStore.Kind.Projectile, proj.type, 96f);
        ModernUI.Text($"弹幕[{_index}] {name} (类型 {proj.type})");
        InfoRow("伤害", $"{proj.damage}  敌对: {(proj.hostile ? "是" : "否")}  友好: {(proj.friendly ? "是" : "否")}");
        InfoRow("剩余时间", proj.timeLeft.ToString());
        InfoRow("拥有者", proj.owner.ToString());
        InfoRow("位置", $"{pos.X:F1}, {pos.Y:F1}");
        InfoRow("速度", $"{vel.X:F1}, {vel.Y:F1} ({speed:F1})");
        InfoRow("速度方向", string.Empty);
        InspectorCommon.DrawVelocityArrow(new ImVec2(vel.X, vel.Y));

        ModernUI.BeginRow();
        if (ModernUI.GhostButton("传送到此弹幕"))
            Game.LocalPlayer?.Teleport(new XnaVector2(pos.X / 16f, pos.Y / 16f), 0, 0);
        if (ModernUI.GhostButton("击杀弹幕"))
            proj.active = false;
        ModernUI.EndRow();

        ModernUI.EndCard();
    }

    private static string ProjName(Projectile proj)
    {
        try
        {
            var name = proj.Name;
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
