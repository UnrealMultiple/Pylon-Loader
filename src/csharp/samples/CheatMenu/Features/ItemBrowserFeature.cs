using System;
using System.Collections.Generic;
using System.Numerics;
using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader.ImGuiHost;
using Pylon.NaviteAPI.ImGui;
using Terraria;
using Terraria.ID;
using Terraria.DataStructures;
using CheatMenu.Infra;
using Game = CheatMenu.Infra.Game;

namespace CheatMenu.Features;

/// <summary>
/// 物品生成器。1:1 对应原版 <c>ItemBrowserTool</c>（物品 · 物品生成）：
/// 搜索（名称/ID）+ 图标网格（MT2 的 items/Item_&lt;id&gt;.png），点击在玩家位置生成。
/// 列表范围取 ItemID.Count。
/// </summary>
public sealed class ItemBrowserFeature : CheatFeature
{
    private const float ButtonSize = 88f;
    private const float IconSize = 72f;

    private string _search = string.Empty;
    private string _lastSearch;
    private List<(int Id, string Name)> _itemNames;
    private readonly List<(int Id, string Name)> _filtered = new();
    private bool _scrollReset = true;

    public override string Id => "item.browser";
    public override string Title => "物品生成器";
    public override CheatCategory Category => CheatCategory.Item;
    public override string SubPage => "物品生成";

    public override void DrawMenu()
    {
        var cfg = CheatFeatureHost.Config;

        ModernUI.BeginCard("物品生成器", "ITEM BROWSER");

        ModernUI.BeginRow();
        ModernUI.RowLabel("搜索");
        var search = _search ?? string.Empty;
        if (ModernUI.TextInput("##itemsearch", ref search))
            _search = search ?? string.Empty;
        ModernUI.EndRow();
        ModernUI.DimText("按物品名称或 ID 搜索");

        BuildItemList();
        UpdateFilteredList();

        ModernUI.BeginRow();
        ModernUI.RowLabel("匹配", $"{_filtered.Count} 项");
        ModernUI.EndRow();

        var stack = (float)(cfg?.ItemGiveStack ?? 1);
        ModernUI.BeginRow();
        ModernUI.RowLabel("数量");
        ModernUI.EndRow();
        if (ModernUI.RowSlider("##givestack", "", "1-9999", ref stack, 1f, 9999f, "0"))
        {
            cfg.ItemGiveStack = (int)MathF.Round(Math.Clamp(stack, 1f, 9999f));
            CheatFeatureHost.MarkDirty();
        }

        // 图标网格（滚动区，点击生成）
        var scrollHeight = ImGui.GetFontSize() * 13f;
        ModScrollArea.Begin("##itembrowser_scroll", new System.Numerics.Vector2(0f, scrollHeight),
            reset: _scrollReset);
        _scrollReset = false;
        try
        {
            IconGrid.DrawVirtualGrid(_filtered, ButtonSize, IconSize, DrawItemIcon,
                static (id, _) => GiveItem(id));
        }
        finally { ModScrollArea.End(); }

        ModernUI.EndCard();
    }

    private void DrawItemIcon(int id, string name)
    {
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        if (IconStore.TryGet(IconStore.Kind.Item, id, out var texture, out var iw, out var ih))
            IconGrid.DrawIconContained(texture, iw, ih, min, max);
    }

    // ---------------- 列表 ----------------

    private void BuildItemList()
    {
        if (_itemNames is not null) return;

        // 列表不依赖图标：全量扫描 1..ItemID.Count，名称拿不到也保留格子（图标/点击不依赖名称）。
        _itemNames = [];
        var maxId = ItemID.Count;
        if (maxId <= 0) maxId = 10000;
        for (var id = 1; id < maxId; id++)
        {
            string name = null;
            try
            {
                name = Lang.GetItemNameValue(id);
            }
            catch
            {
                name = null;
            }
            _itemNames.Add((id, string.IsNullOrEmpty(name) ? "Item " + id : name));
        }
        _lastSearch = null;
    }

    private void UpdateFilteredList()
    {
        var lower = (_search ?? string.Empty).ToLowerInvariant();
        if (lower == _lastSearch && _filtered.Count > 0) return;
        _lastSearch = lower;
        _scrollReset = true;   // 结果集变了，列表滚回顶部

        _filtered.Clear();
        if (_itemNames is null) return;
        foreach (var (id, name) in _itemNames)
        {
            if (lower.Length == 0 ||
                name.ToLowerInvariant().Contains(lower) ||
                id.ToString().Contains(lower))
            {
                _filtered.Add((id, name));
            }
        }
    }

    // ---------------- 生成 ----------------

    /// <summary>原版 GiveItem：掉落到玩家位置（联网自动同步服务器）。</summary>
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
