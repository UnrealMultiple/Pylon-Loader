using System;
using System.Collections.Generic;
using System.Numerics;
using Il2CppMicrosoft.Xna.Framework;
using Pylon.NaviteAPI.ImGui;
using Pylon.ModLoader.ImGuiHost;
using Terraria;
using CheatMenu.Infra;
using Game = CheatMenu.Infra.Game;
using XnaColor = Il2CppMicrosoft.Xna.Framework.Graphics.Color;

namespace CheatMenu.Features;

/// <summary>
/// 物品修改器。1:1 对应原版 <c>ItemModifierTool</c>（物品 · 物品修改器）：
/// 显示当前手持物品，输入即应用（堆叠/伤害/大小/击退/弹幕/使用帧/动画/墙/图格/样式/
/// 回复/颜色）；前缀走游戏原版 Item.ResetPrefix + Item.Prefix（哥布林重铸同款）。
/// </summary>
public sealed class ItemModifierFeature : CheatFeature
{
    // 编辑态（与应用态分离：切换物品时从物品同步进来）
    private Item _lastItem;
    private bool _editInitialized;

    private int _prefix, _stack, _damage, _shoot, _useTime, _useAnimation;
    private int _createWall, _createTile, _placeStyle, _healLife, _healMana;
    private int _colorR = 255, _colorG = 255, _colorB = 255, _colorA = 255;
    private float _scale, _knockBack;

    private string[] _prefixNames;
    private int _prefixSelection;

    public override string Id => "item.modifier";
    public override string Title => "物品修改器";
    public override CheatCategory Category => CheatCategory.Item;
    public override string SubPage => "物品修改器";

    public override void DrawMenu()
    {
        var item = Game.LocalPlayer?.HeldItem;
        if (item is null)
        {
            ModernUI.BeginCard("物品修改器", "ITEM MODIFIER");
            ModernUI.DimText("先拿起一件物品");
            ModernUI.EndCard();
            return;
        }

        if (!_editInitialized || _lastItem != item) SyncEditFromItem(item);

        ModernUI.BeginCard("物品修改器", "ITEM MODIFIER");

        // ---- 头部：图标 + 名称 ----
        var iconSize = new System.Numerics.Vector2(48f, 48f);
        ImGui.Dummy(iconSize);
        if (IconStore.TryGet(IconStore.Kind.Item, item.type, out var tex, out var iw, out var ih))
            IconGrid.DrawIconContained(tex, iw, ih, ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        ImGui.SameLine();
        ModernUI.Text($"当前物品: {ItemName(item, out var typeName)}");
        ModernUI.BeginRow();
        ModernUI.RowLabel("物品类型", item.type.ToString());
        ModernUI.EndRow();

        // ---- 前缀（下拉 + 重铸） ----
        EnsurePrefixesLoaded();
        if (_prefixNames is null)
        {
            var manual = _prefix;
            if (IntRow("##modprefix", "前缀", manual, out manual, "0=清除 / -2=重铸"))
            {
                _prefix = manual;
                ApplyPrefix(item, manual);
            }
        }
        else
        {
            _prefixSelection = Math.Clamp(_prefix, 0, _prefixNames.Length - 1);
            ModernUI.BeginRow();
            ModernUI.RowLabel("前缀");
            if (ModernUI.ComboField("##prefixcombo", _prefixNames, ref _prefixSelection))
            {
                _prefix = _prefixSelection;
                ApplyPrefix(item, _prefix);
            }
            ModernUI.EndRow();
            if (ModernUI.GhostButton("重铸（随机前缀）", default, ModernUI.Icon.None, rightAlign: true))
                ApplyPrefix(item, -2);
        }

        // ---- 属性（输入即应用） ----
        var stack = _stack;
        if (IntRow("##modstack", "堆叠", stack, out stack)) { _stack = stack; ApplyAll(item); }
        var damage = _damage;
        if (IntRow("##moddamage", "伤害", damage, out damage)) { _damage = damage; ApplyAll(item); }
        var scale = _scale;
        if (ModernUI.RowSlider("##modscale", "大小", "0-5", ref scale, 0f, 5f, "0.00")) { _scale = scale; ApplyAll(item); }
        var knock = _knockBack;
        if (ModernUI.RowSlider("##modknock", "击退", "0-100", ref knock, 0f, 100f, "0.0")) { _knockBack = knock; ApplyAll(item); }
        var shoot = _shoot;
        if (IntRow("##modshoot", "弹幕", shoot, out shoot)) { _shoot = shoot; ApplyAll(item); }
        var useTime = _useTime;
        if (IntRow("##modusetime", "使用帧", useTime, out useTime)) { _useTime = useTime; ApplyAll(item); }
        var useAnim = _useAnimation;
        if (IntRow("##moduseanim", "使用动画", useAnim, out useAnim)) { _useAnimation = useAnim; ApplyAll(item); }
        var wall = _createWall;
        if (IntRow("##modwall", "创建墙", wall, out wall)) { _createWall = wall; ApplyAll(item); }
        var tile = _createTile;
        if (IntRow("##modtile", "创建图格", tile, out tile)) { _createTile = tile; ApplyAll(item); }
        var style = _placeStyle;
        if (IntRow("##modplace", "创建样式", style, out style)) { _placeStyle = style; ApplyAll(item); }
        var healLife = _healLife;
        if (IntRow("##modheallife", "回复生命", healLife, out healLife)) { _healLife = healLife; ApplyAll(item); }
        var healMana = _healMana;
        if (IntRow("##modhealmana", "回复魔力", healMana, out healMana)) { _healMana = healMana; ApplyAll(item); }

        var r = _colorR;
        if (IntRow("##modcr", "颜色R", r, out r)) { _colorR = r; ApplyAll(item); }
        var g = _colorG;
        if (IntRow("##modcg", "颜色G", g, out g)) { _colorG = g; ApplyAll(item); }
        var b = _colorB;
        if (IntRow("##modcb", "颜色B", b, out b)) { _colorB = b; ApplyAll(item); }
        var a = _colorA;
        if (IntRow("##modca", "颜色A", a, out a)) { _colorA = a; ApplyAll(item); }

        ModernUI.EndCard();
    }

    private static string ItemName(Item item, out string fallback)
    {
        fallback = null;
        try
        {
            var name = Lang.GetItemNameValue(item.type);
            fallback = name;
            return name;
        }
        catch
        {
            return "无";
        }
    }

    // ---------------- 应用 ----------------

    private void ApplyAll(Item item)
    {
        if (item is null) return;
        item.stack = _stack;
        item.damage = _damage;
        item.scale = _scale;
        item.knockBack = _knockBack;
        item.shoot = _shoot;
        item.useTime = _useTime;
        item.useAnimation = _useAnimation;
        item.createWall = _createWall;
        item.createTile = _createTile;
        item.placeStyle = _placeStyle;
        item.healLife = _healLife;
        item.healMana = _healMana;
        item.color = new XnaColor(
            Math.Clamp(_colorR, 0, 255) / 255f,
            Math.Clamp(_colorG, 0, 255) / 255f,
            Math.Clamp(_colorB, 0, 255) / 255f,
            Math.Clamp(_colorA, 0, 255) / 255f);
    }

    /// <summary>照哥布林重铸：先恢复基础属性，再按指定前缀结算（0 = 清除；-2 = 随机重铸）。</summary>
    private static void ApplyPrefix(Item item, int prefixWeWant)
    {
        if (item is null) return;
        item.ResetPrefix();
        if (prefixWeWant != 0) item.Prefix(prefixWeWant);
    }

    private void SyncEditFromItem(Item item)
    {
        _lastItem = item;
        _editInitialized = true;
        _prefix = item.prefix;
        _stack = item.stack;
        _damage = item.damage;
        _shoot = item.shoot;
        _useTime = item.useTime;
        _useAnimation = item.useAnimation;
        _createWall = item.createWall;
        _createTile = item.createTile;
        _placeStyle = item.placeStyle;
        _healLife = item.healLife;
        _healMana = item.healMana;
        _scale = item.scale;
        _knockBack = item.knockBack;
        _colorR = (int)(item.color.R * 255f);
        _colorG = (int)(item.color.G * 255f);
        _colorB = (int)(item.color.B * 255f);
        _colorA = (int)(item.color.A * 255f);
    }

    // ---------------- 前缀名（Lang.prefix 懒加载，index 0 = None） ----------------

    private void EnsurePrefixesLoaded()
    {
        if (_prefixNames is not null) return;
        try
        {
            var prefixes = Lang.prefix;
            if (prefixes is null || prefixes.Length == 0) return;

            var names = new string[prefixes.Length];
            for (var i = 0; i < prefixes.Length; i++)
            {
                var text = prefixes[i]?.Value;
                names[i] = string.IsNullOrEmpty(text) ? "Prefix " + i : text;
            }
            if (names.Length > 0) names[0] = "None";
            _prefixNames = names;
        }
        catch
        {
            _prefixNames = null;   // 退回手动输入
        }
    }

    // ---------------- 小控件 ----------------

    private static bool IntRow(string id, string label, int value, out int result, string hint = null)
    {
        result = value;
        var text = value.ToString();
        ModernUI.BeginRow();
        ModernUI.RowLabel(label, hint);
        var changed = ModernUI.TextInput(id, ref text);
        ModernUI.EndRow();
        if (!changed || !int.TryParse(text, out var parsed)) return false;
        result = parsed;
        return true;
    }
}
