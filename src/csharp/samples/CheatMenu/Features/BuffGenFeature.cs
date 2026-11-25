using System;
using System.Collections.Generic;
using Pylon.ModLoader.ImGuiHost;
using Terraria;
using CheatMenu.Infra;

namespace CheatMenu.Features;

/// <summary>
/// Buff 生成。1:1 对应原版 <c>BuffBrowserTool</c>：
/// 名称/ID 搜索、可调时长（秒）、匹配列表，点击即给自己加 Buff。
/// 原版用 APK 图标网格；图标桥接完成前先用文字按钮，行为不变。
/// </summary>
public sealed class BuffGenFeature : CheatFeature
{
    private const int MaxBuffs = 388;
    private const int MaxButtons = 60;

    private static readonly List<int> BuffIds = new();
    private static string[] _names;
    private static bool _built;

    private readonly List<int> _filtered = new();
    private string _search = string.Empty;
    private string _builtSearch;
    private string _secondsText;

    public override string Id => "misc.buffgen";
    public override string Title => "Buff生成";
    public override CheatCategory Category => CheatCategory.Main;
    public override string SubPage => "Buff生成";

    public override void DrawMenu()
    {
        var cfg = CheatFeatureHost.Config;
        if (cfg is null) return;

        EnsureBuilt();

        ModernUI.BeginCard("Buff生成", "BUFF BROWSER");

        ModernUI.TextInput("搜索", ref _search);
        ModernUI.DimText("按 Buff 名称或 ID 搜索");

        if (_secondsText is null) _secondsText = cfg.BuffSeconds.ToString();
        if (ModernUI.TextInput("时长（秒）", ref _secondsText))
        {
            if (int.TryParse(_secondsText, out var seconds))
            {
                if (seconds < 1) seconds = 60;
                cfg.BuffSeconds = Math.Clamp(seconds, 1, 3600);
                CheatFeatureHost.MarkDirty();
            }
        }

        RebuildFiltered();

        ModernUI.DimText($"匹配 {_filtered.Count} 项");
        var shown = 0;
        for (var i = 0; i < _filtered.Count && shown < MaxButtons; i++)
        {
            var id = _filtered[i];
            var name = _names[id] ?? string.Empty;
            if (!ModernUI.GhostButton($"{id}  {name}")) continue;
            GiveBuff(id, cfg.BuffSeconds);
            ModernUI.ShowStatus($"已获得 Buff {id} {name}");
        }
        if (_filtered.Count > MaxButtons) ModernUI.DimText($"仅显示前 {MaxButtons} 项，继续输入以缩小范围");

        ModernUI.EndCard();
    }

    private static void GiveBuff(int type, int seconds)
    {
        if (type <= 0) return;
        var player = Game.LocalPlayer;
        if (player is null) return;
        player.AddBuff(type, Math.Max(seconds, 1) * 60, false);
    }

    private static void EnsureBuilt()
    {
        if (_built) return;
        _built = true;

        _names = new string[MaxBuffs + 1];
        for (var id = 1; id <= MaxBuffs; id++)
        {
            string name;
            try { name = Lang.GetBuffName(id) ?? string.Empty; }
            catch { name = string.Empty; }
            if (string.IsNullOrEmpty(name)) continue;
            _names[id] = name;
            BuffIds.Add(id);
        }
    }

    private void RebuildFiltered()
    {
        var lower = (_search ?? string.Empty).Trim().ToLowerInvariant();
        if (string.Equals(_builtSearch, lower, StringComparison.Ordinal)) return;
        _builtSearch = lower;

        _filtered.Clear();
        for (var i = 0; i < BuffIds.Count; i++)
        {
            var id = BuffIds[i];
            if (lower.Length == 0)
            {
                _filtered.Add(id);
                continue;
            }
            if ((_names[id] ?? string.Empty).ToLowerInvariant().Contains(lower) ||
                id.ToString().Contains(lower))
            {
                _filtered.Add(id);
            }
        }
    }
}