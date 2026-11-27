using System;
using Pylon.ModLoader.ImGuiHost;
using Terraria;
using CheatMenu.Infra;
using Game = CheatMenu.Infra.Game;

namespace CheatMenu.Features;

/// <summary>
/// 生成设置。1:1 对应原版 <c>NPCSpawnSettingsTool</c>（实体 · 生成设置）：
/// 原版 hook <c>NPC.GetSpawnRate</c>（private + out 参数，MonoMod 不生成代理），
/// 这里改 hook 公共入口 <c>NPC.SpawnNPC()</c>：进入前强制把 spawnRate/maxSpawns
/// 两个静态字段写成配置值，spawn 决策读到的就是覆盖值，行为等价。
/// </summary>
public sealed class NpcSpawnSettingsFeature : CheatFeature
{
    public override string Id => "entity.spawnsettings";
    public override string Title => "生成设置";
    public override CheatCategory Category => CheatCategory.Physical;
    public override string SubPage => "生成设置";

    public override void Install()
    {
        On.Terraria.NPC.SpawnNPC += HookSpawnNpc;
    }

    private static void HookSpawnNpc(On.Terraria.NPC.orig_SpawnNPC orig)
    {
        var cfg = CheatFeatureHost.Config;
        if (cfg is { ModifySpawn: true })
        {
            NPC.spawnRate = cfg.DefaultSpawnRate;
            NPC.maxSpawns = cfg.DefaultMaxSpawns;
        }
        orig();
    }

    public override void DrawMenu()
    {
        var cfg = CheatFeatureHost.Config;
        if (cfg is null) return;

        ModernUI.BeginCard("生成设置", "SPAWN SETTINGS");

        var rate = (float)cfg.DefaultSpawnRate;
        if (ModernUI.RowSlider("##spawnrate", "生成延迟", "1-1000", ref rate, 1f, 1000f, "0"))
        {
            cfg.DefaultSpawnRate = (int)Math.Clamp(rate, 1f, 1000f);
            CheatFeatureHost.MarkDirty();
        }

        var maxSpawns = (float)cfg.DefaultMaxSpawns;
        if (ModernUI.RowSlider("##maxspawns", "生成阈值", "0-200", ref maxSpawns, 0f, 200f, "0"))
        {
            cfg.DefaultMaxSpawns = (int)Math.Clamp(maxSpawns, 0f, 200f);
            CheatFeatureHost.MarkDirty();
        }

        RowToggle("##modifyspawn", "保持修改", cfg.ModifySpawn, v => cfg.ModifySpawn = v);
        ModernUI.DimText("开启后强制覆盖怪物的生成延迟与数量上限");

        ModernUI.EndCard();
    }

    private static void RowToggle(string id, string label, bool value, Action<bool> set)
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
