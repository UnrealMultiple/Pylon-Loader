using System;
using Pylon.ModLoader.ImGuiHost;
using Terraria;
using CheatMenu.Infra;
using Game = CheatMenu.Infra.Game;

namespace CheatMenu.Features;

/// <summary>
/// 屠杀 NPC。1:1 对应原版 <c>NPCTool</c>（实体 · 屠杀NPC）：
/// 击杀敌方 / 全部 NPC 按钮 + 自动击杀开关。每击 = StrikeNPCNoInteraction + 28 号包（DamageNPC），
/// 先按 153 号包（NPCDebuffDamage）抬满生命绕过 TShock 生命校验。
/// </summary>
public sealed class ButcherNpcFeature : CheatFeature
{
    public override string Id => "entity.butcher";
    public override string Title => "屠杀 NPC";
    public override CheatCategory Category => CheatCategory.Physical;
    public override string SubPage => "屠杀NPC";

    public override void Tick()
    {
        var cfg = CheatFeatureHost.Config;
        if (cfg is { AutoButcherNpc: true }) ButcherAllHostile();
    }

    public override void DrawMenu()
    {
        var cfg = CheatFeatureHost.Config;
        if (cfg is null) return;

        ModernUI.BeginCard("屠杀 NPC", "NPC BUTCHER");

        ModernUI.BeginRow();
        if (ModernUI.GhostButton("击杀敌方NPC"))
        {
            ButcherAllHostile();
        }
        if (ModernUI.GhostButton("击杀所有NPC"))
        {
            ButcherAllHostile();
            ButcherAllFriendly();
        }
        ModernUI.EndRow();

        RowToggle("自动击杀敌对NPC", "##autobutcher", cfg.AutoButcherNpc, v => cfg.AutoButcherNpc = v);

        ModernUI.EndCard();
    }

    /// <summary>原版 ButcherAllHostileNPCs：active 且非 friendly 且非 388（雕像 ExcludeID？原版按 type!=388 过滤）。</summary>
    private static void ButcherAllHostile()
    {
        var npcs = Main.npc;
        if (npcs is null) return;
        for (var i = 0; i < npcs.Length; i++)
        {
            var npc = npcs[i];
            if (npc is null || !npc.active || npc.friendly || npc.type == 388) continue;
            ButcherBypassTShock(npc, -1);
            ButcherOne(npc, 1000, -1);
        }
    }

    private static void ButcherAllFriendly()
    {
        var npcs = Main.npc;
        if (npcs is null) return;
        for (var i = 0; i < npcs.Length; i++)
        {
            var npc = npcs[i];
            if (npc is null || !npc.active || !npc.friendly) continue;
            ButcherBypassTShock(npc, -1);
            ButcherOne(npc, 1000, -1);
        }
    }

    /// <summary>原版 ButcherNPC：命中数 = ceil((life + defense/2) / damage)，每击 Strike + 28 号包。</summary>
    private static void ButcherOne(NPC npc, int damage, int hitCount)
    {
        var trueHitCount = hitCount;
        if (hitCount == -1)
        {
            trueHitCount = (int)MathF.Ceiling(
                (npc.life + (int)MathF.Ceiling(npc.defense / 2f)) / (float)damage);
        }
        for (var j = 0; j < trueHitCount; j++)
        {
            npc.StrikeNPCNoInteraction(damage, 0f, 0);
            NetMessage.SendData(28, -1, -1, null, npc.whoAmI, damage, 0f, 0f, 0, 0, 0);
        }
    }

    /// <summary>原版 ButcherNPCBypassTShock：非 immortal 时按 SHRT_MAX 分片发 153 号包抬生命。</summary>
    private static void ButcherBypassTShock(NPC npc, int hitCount)
    {
        if (npc.immortal) return;

        if (hitCount < 0)
            hitCount = (int)MathF.Ceiling(npc.life / (float)short.MaxValue);
        for (var i = 0; i < hitCount; i++)
        {
            NetMessage.SendData(153, -1, -1, null, npc.whoAmI, short.MaxValue, 0f, 0f, 0, 0, 0);
        }
    }

    private static void RowToggle(string label, string id, bool value, Action<bool> set)
    {
        ModernUI.BeginRow();
        ModernUI.RowLabel(label);
        if (ModernUI.Toggle(id, value))
        {
            set(!value);
            CheatFeatureHost.MarkDirty();
        }
        ModernUI.EndRow();
    }
}
