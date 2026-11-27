using System;
using System.Collections.Generic;
using Pylon.Mod;
using Pylon.ModLoader.ImGuiHost;
using CheatMenu.Features;

namespace CheatMenu.UI;

/// <summary>
/// 唯一顶层页。侧栏只占一项；页内两级导航：原版 Tab（分类胶囊）→ 原版子页 → 卡片。
/// 分类只显示「已有功能」的项，与原版「空 Tab 省略」一致；当前只移植了「主要」页。
/// </summary>
public sealed class CheatRootTab : IModImGuiTab
{
    private readonly record struct CategoryMeta(string Title, string Caps, string Desc);

    private static readonly CategoryMeta[] Meta =
    [
        new("主要", "MAIN", "玩家 · 角色 · 自动钓鱼 · Buff 生成"),
        new("世界", "WORLD", "地图 · 时间 · 事件 · 编辑"),
        new("实体", "ENTITIES", "NPC · 弹幕 · 生成"),
        new("物品", "ITEMS", "物品生成 · 修改器"),
        new("杂项", "MISC", "成就 · 漏洞 · 音乐"),
        new("网络", "NETWORK", "数据包抓取 · 内容浏览"),
        new("设置", "SETTINGS", "界面 · 安全 · Mod"),
        new("视觉", "VISUALS", "ESP · HUD · 弹道"),
        new("检查器", "INSPECTOR", "物品 · NPC · 玩家 · 弹幕 · 图格"),
    ];

    private readonly List<CheatCategory> _categories = [];
    private readonly List<string> _subPages = [];
    private readonly int[] _subIndex = new int[Meta.Length];

    private int _categoryIndex;
    private string[] _categoryLabels = [];
    private string[] _subLabels = [];
    private int _cachedVersion = int.MinValue;
    private int _cachedSubVersion = int.MinValue;
    private CheatCategory _cachedSubCategory = (CheatCategory)(-1);

    public string Title => "作弊菜单";

    public void Draw()
    {
        ModernUI.BeginPage();

        if (!CheatFeatureHost.Installed)
        {
            ModernUI.PageHead("作弊菜单", "CHEAT MENU", "正在装配，等待内容就绪");
            ModernUI.DimText("作弊模块尚未装配（等待内容就绪）");
            return;
        }

        RefreshCategories();
        if (_categories.Count == 0)
        {
            ModernUI.PageHead("作弊菜单", "CHEAT MENU", "没有可用的功能");
            ModernUI.DimText("没有装配任何功能");
            return;
        }

        ModernUI.PageHead("作弊菜单", "CHEAT MENU", "与原版 Tab 一一对应 · 按原版工具移植");

        var categoryIndex = _categoryIndex;
        if (ModernUI.Seg("##cheatcategory", ref categoryIndex, _categoryLabels))
        {
            ModScrollArea.StopInertia();
            _categoryIndex = categoryIndex;
        }
        _categoryIndex = Math.Clamp(_categoryIndex, 0, _categories.Count - 1);

        var category = _categories[_categoryIndex];
        RefreshSubPages(category);

        var meta = Meta[(int)category];
        var subPage = "主要";
        if (_subLabels.Length > 1)
        {
            var subIndex = _subIndex[(int)category];
            if (ModernUI.Seg("##cheatsub", ref subIndex, _subLabels))
            {
                ModScrollArea.StopInertia();
                _subIndex[(int)category] = subIndex;
            }
            _subIndex[(int)category] = Math.Clamp(_subIndex[(int)category], 0, _subLabels.Length - 1);
            subPage = _subLabels[_subIndex[(int)category]];
        }
        else if (_subLabels.Length == 1)
        {
            subPage = _subLabels[0];
        }

        ModernUI.DimText(meta.Title + " · " + meta.Desc);
        ModernUI.StatusRow();

        CheatFeatureHost.DrawCategory(category, subPage);

        // 主菜单里也可能开着这一页；那时没有 PostUpdateEverything，靠这里兜底写盘。
        CheatFeatureHost.Flush();
    }

    private void RefreshCategories()
    {
        if (_cachedVersion == CheatFeatureHost.RegistryVersion && _categoryLabels.Length == _categories.Count) return;
        _cachedVersion = CheatFeatureHost.RegistryVersion;

        CheatFeatureHost.CollectCategories(_categories);

        if (_categoryLabels.Length != _categories.Count)
            _categoryLabels = new string[_categories.Count];
        for (var i = 0; i < _categories.Count; i++)
            _categoryLabels[i] = Meta[(int)_categories[i]].Title;

        _categoryIndex = Math.Clamp(_categoryIndex, 0, Math.Max(_categories.Count - 1, 0));
    }

    private void RefreshSubPages(CheatCategory category)
    {
        if (_cachedSubCategory == category && _cachedSubVersion == CheatFeatureHost.RegistryVersion) return;
        _cachedSubCategory = category;
        _cachedSubVersion = CheatFeatureHost.RegistryVersion;

        CheatFeatureHost.CollectSubPages(category, _subPages);

        if (_subLabels.Length != _subPages.Count)
            _subLabels = new string[_subPages.Count];
        for (var i = 0; i < _subPages.Count; i++)
            _subLabels[i] = _subPages[i];

        _subIndex[(int)category] = Math.Clamp(_subIndex[(int)category], 0, Math.Max(_subPages.Count - 1, 0));
    }
}