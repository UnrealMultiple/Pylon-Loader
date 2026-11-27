using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Pylon.ModLoader.Content;
using Pylon.NaviteAPI.ImGui;
using Pylon.ModLoader.ImGuiHost;
using Terraria;
using Terraria.ID;
using CheatMenu.Infra;
using Game = CheatMenu.Infra.Game;

namespace CheatMenu.Features;

/// <summary>
/// Mod 物品目录。对应原版 <c>ModItemsTool</c>（物品 · Mod 物品）：
/// 原版列 JS Mod（ModScript）注册的 mod 物品；Pylon 侧对应的是 ModContent
/// 注册的 ModItem（type ≥ ItemID.Count），按 Mod 分组展示，点击生成。
/// </summary>
public sealed class ModItemsFeature : CheatFeature
{
    private sealed class Entry
    {
        public int Type;
        public string Mod;
        public string Name;
    }

    private string _search = string.Empty;
    private string _lastSearch = null;
    private List<Entry> _filtered = new();
    private static List<Entry> _cache;
    private static bool _cacheDirty = true;

    public override string Id => "item.moditems";
    public override string Title => "Mod 物品目录";
    public override CheatCategory Category => CheatCategory.Item;
    public override string SubPage => "Mod 物品";

    /// <summary>内容注册变化时置脏（安装完成时机不确定，懒加载本身也兜底）。</summary>
    public static void Invalidate() => _cacheDirty = true;

    public override void DrawMenu()
    {
        var cfg = CheatFeatureHost.Config;

        ModernUI.BeginCard("Mod 物品目录", "MOD ITEMS");

        ModernUI.BeginRow();
        ModernUI.RowLabel("搜索");
        var search = _search ?? string.Empty;
        if (ModernUI.TextInput("##moditemssearch", ref search))
            _search = search ?? string.Empty;
        ModernUI.EndRow();
        ModernUI.DimText("按物品名 / Mod 名 / 类型 ID 搜索");

        var stack = (float)(cfg?.ItemGiveStack ?? 1);
        ModernUI.BeginRow();
        ModernUI.RowLabel("数量");
        ModernUI.EndRow();
        if (ModernUI.RowSlider("##modgivestack", "", "1-9999", ref stack, 1f, 9999f, "0"))
        {
            cfg.ItemGiveStack = (int)MathF.Round(Math.Clamp(stack, 1f, 9999f));
            CheatFeatureHost.MarkDirty();
        }

        UpdateFilteredList();
        ModernUI.DimText($"共 {_filtered.Count} 个 mod 物品（type ≥ ItemID.Count）");

        var scrollHeight = ImGui.GetFontSize() * 14f;
        ModScrollArea.Begin("##moditems_scroll", new System.Numerics.Vector2(0f, scrollHeight));

        string lastMod = null;
        foreach (var e in _filtered)
        {
            if (e.Mod != lastMod)
            {
                ImGui.SeparatorText(e.Mod);
                lastMod = e.Mod;
            }
            var label = $"{e.Name}  ##{e.Type}";
            if (ImGui.Button(label, new System.Numerics.Vector2(ImGui.GetContentRegionAvail().X, 0f)))
                GiveItem(e.Type);
        }

        ModScrollArea.End();
        ModernUI.EndCard();
    }

    // ---------------- 数据 ----------------

    /// <summary>枚举 Pylon 注册的全部 ModItem。Registry internal，走反射取 ContentOf 列表。</summary>
    private static List<Entry> CollectModItems()
    {
        var result = new List<Entry>();
        try
        {
            // ModContent.GetInstance<ModItem>() 只取单个类；这里要全量列表。
            var registryField = typeof(ModContent).Assembly.GetType("Pylon.ModLoader.Content.Registration.ContentRegistry");
            var instanceProp = registryField?.GetProperty("Instances", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var registryObj = registryField?.GetProperty("Registry", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(null);
            var instances = instanceProp?.GetValue(registryObj) as IEnumerable<ModType>;
            if (instances is null) return result;

            foreach (var item in instances)
            {
                if (item is not ModItem modItem || modItem.Item is null) continue;
                result.Add(new Entry
                {
                    Type = modItem.Item.type,
                    Mod = modItem.Mod?.Name ?? "?",
                    Name = string.IsNullOrEmpty(modItem.DisplayName) ? modItem.Name : modItem.DisplayName,
                });
            }
        }
        catch
        {
            // 注册表结构变化：空列表兜底
        }
        return result;
    }

    private void UpdateFilteredList()
    {
        if (_cacheDirty || _cache is null)
        {
            _cache = CollectModItems();
            _cacheDirty = false;
        }

        var lower = (_search ?? string.Empty).ToLowerInvariant();
        if (lower == _lastSearch) return;
        _lastSearch = lower;

        _filtered.Clear();
        foreach (var item in _cache)
        {
            if (lower.Length == 0)
            {
                _filtered.Add(item);
                continue;
            }
            if ((item.Mod?.ToLowerInvariant().Contains(lower) ?? false) ||
                (item.Name?.ToLowerInvariant().Contains(lower) ?? false) ||
                item.Type.ToString().Contains(lower))
            {
                _filtered.Add(item);
            }
        }
    }

    private static void GiveItem(int type)
    {
        if (type <= 0) return;
        var player = Game.LocalPlayer;
        if (player is null) return;

        var stack = CheatFeatureHost.Config?.ItemGiveStack ?? 1;
        Item.NewItem(Projectile.GetNoneSource(), player.Center, type, stack, 0,
            NewItemOwnership.None, default, null, false);
    }
}
