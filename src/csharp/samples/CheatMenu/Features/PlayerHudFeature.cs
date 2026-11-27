using System;
using System.Collections.Generic;
using System.Numerics;
using Pylon.NaviteAPI.ImGui;
using Pylon.ModLoader.ImGuiHost;
using Terraria;
using CheatMenu.Infra;
using XnaVector2 = Il2CppMicrosoft.Xna.Framework.Vector2;

namespace CheatMenu.Features;

/// <summary>
/// 玩家 HUD。1:1 对应原版 <c>PlayerHudTool</c>（视觉）：
/// 玩家头顶面板 = 手持武器图标 + 血量/魔力/防御（图标取 MT2 items），缩放/头部偏移可调。
/// 绘制走 ImGui 背景绘制列表（<see cref="ModOverlay"/>，等价原版
/// <c>ImGui::GetBackgroundDrawList()</c>），**不再**用游戏 SpriteBatch 界面层。
/// 坐标换算：头顶世界像素 → <see cref="WorldEdit.WorldToScreen"/>
/// （XNAUnityRunner.AdjustWorldForRealScreen）→ 覆盖层物理像素，与 ImGui <c>DisplaySize</c> 同空间；
/// 面板内的尺寸/字号都是覆盖层物理像素，由 cfg.HudScale 等比缩放（与原版一致）。
/// </summary>
public sealed class PlayerHudFeature : CheatFeature
{
    private const string OverlayKey = "CheatMenu/playerhud";

    // 1.0× 基准尺寸（覆盖层物理像素，与原版 PlayerHudTool 的 IconSize/FontSize/Spacing 一致）
    private const float BaseIconSize = 40f;
    private const float BaseFontSize = 30f;
    private const float BaseSpacing = 8f;

    private const string Slash = "/";

    public override string Id => "visual.hud";
    public override string Title => "玩家 HUD";
    public override CheatCategory Category => CheatCategory.Visual;
    public override string SubPage => "主要";

    public override void Install() => RegisterOverlay(OverlayKey, DrawHud);

    public override void DrawMenu()
    {
        var cfg = CheatFeatureHost.Config;
        if (cfg is null) return;

        ModernUI.BeginCard("玩家 HUD", "PLAYER HUD");

        RowToggle("##hudself", "绘制自身玩家", cfg.HudShowSelf, v => cfg.HudShowSelf = v);
        RowToggle("##hudothers", "绘制其他玩家", cfg.HudShowOthers, v => cfg.HudShowOthers = v);
        RowToggle("##hudweapon", "手持武器", cfg.HudShowWeapon, v => cfg.HudShowWeapon = v);
        RowToggle("##hudhealth", "血量", cfg.HudShowHealth, v => cfg.HudShowHealth = v);
        RowToggle("##hudmana", "魔力", cfg.HudShowMana, v => cfg.HudShowMana = v);
        RowToggle("##huddef", "防御", cfg.HudShowDefense, v => cfg.HudShowDefense = v);

        var scale = cfg.HudScale;
        if (ModernUI.RowSlider("##hudscale", "缩放比例", "0.4-3.0", ref scale, 0.4f, 3f, "×0.00"))
        {
            cfg.HudScale = Math.Clamp(scale, 0.4f, 3f);
            CheatFeatureHost.MarkDirty();
        }

        var headOffset = cfg.HudHeadOffset;
        if (ModernUI.RowSlider("##hudheadoff", "头部偏移", "0-300", ref headOffset, 0f, 300f, "0"))
        {
            cfg.HudHeadOffset = Math.Clamp(headOffset, 0f, 300f);
            CheatFeatureHost.MarkDirty();
        }

        ModernUI.EndCard();
    }

    // ==================== 常驻绘制（ImGui 背景层） ====================

    /// <summary>
    /// 绘制体：每帧 ImGui 帧内调用。热路径约定——两个绘制开关都关时立即返回；
    /// 数值/文本走缓存，稳态不分配（详见 <see cref="NumberText"/>）。
    /// </summary>
    private static void DrawHud(ImDrawListPtr dl)
    {
        var cfg = CheatFeatureHost.Config;
        if (cfg is null) return;
        if (!cfg.HudShowSelf && !cfg.HudShowOthers) return;   // 开关关闭：第一行就返回
        if (Main.gameMenu || Main.mapFullscreen) return;      // 主菜单 / 全屏地图：不绘制

        var players = Main.player;
        if (players is null) return;

        var myPlayer = Main.myPlayer;
        var font = ImGui.GetFont();                            // 覆盖层当前字体（每帧有效）

        // 文本裁剪区 = 背景绘制列表当前的裁剪矩形（和 ImDrawList.AddText 的默认行为一致）。
        // 同一个值给所有玩家复用，避免每帧重复取。
        var clipMin = dl.GetClipRectMin();
        var clipMax = dl.GetClipRectMax();
        var clip = new Vector4(clipMin.X, clipMin.Y, clipMax.X, clipMax.Y);

        foreach (var player in players)
        {
            if (player is not { active: true }) continue;
            var isSelf = player.whoAmI == myPlayer;
            if (isSelf && !cfg.HudShowSelf) continue;
            if (!isSelf && !cfg.HudShowOthers) continue;

            // 玩家头顶：碰撞箱顶部中央（position 是碰撞箱左上角）→ 世界像素 → 覆盖层屏幕
            var headWorld = new XnaVector2(
                player.position.X + player.width * 0.5f,
                player.position.Y);
            var head = WorldEdit.WorldToScreen(headWorld);

            DrawHudForPlayer(dl, font, clip, cfg, player, ImVec(head));
        }
    }

    /// <summary>单个玩家的头顶面板：图标 + 数值，布局与原版 DrawHudForPlayer 一致。</summary>
    private static void DrawHudForPlayer(ImDrawListPtr dl, ImFontPtr font, Vector4 clip,
        CheatConfig cfg, Player player, Vector2 head)
    {
        var hp = player.statLife;
        var hpMax = player.statLifeMax2;          // 有效最大生命（含生命水晶/生命果）
        if (hpMax <= 0) hpMax = player.statLifeMax;
        var mana = player.statMana;
        var manaMax = player.statManaMax2;        // 有效最大魔力
        if (manaMax <= 0) manaMax = player.statManaMax;
        var defense = player.statDefense;
        var weaponId = player.HeldItem?.type ?? 0;

        // 条目集合用位掩码（0=武器 1=血 2=魔 3=防），避免每帧分配容器
        var mask = 0;
        if (cfg.HudShowWeapon) mask |= 1;
        if (cfg.HudShowHealth) mask |= 2;
        if (cfg.HudShowMana) mask |= 4;
        if (cfg.HudShowDefense) mask |= 8;
        if (mask == 0) return;

        // ---- 等比缩放后的实际尺寸（覆盖层物理像素） ----
        // TerrariaTheme.Px(1f) = 当前覆盖层 UI 比例：原来的 SpriteBatch 版本画在游戏 UI 空间，
        // 会被游戏 UI 比例放大；迁到物理像素后要乘一次 UI 比例，面板显观大小才和菜单/改动前一致。
        // cfg.HudScale 仍是用户倍率（菜单里显示 1.00 = 本机 UI 比例下的基准大小）。
        var s = cfg.HudScale * TerrariaTheme.Px(1f);
        var iconSize = BaseIconSize * s;
        var fontSize = BaseFontSize * s;
        var spacing = BaseSpacing * s;
        var headOffset = cfg.HudHeadOffset * s;
        var textGap = 4f * s;
        var pad = MathF.Max(6f, BaseSpacing) * s;

        // ---- 第一遍：量面板总宽（图标 + 可选文字），不分配 ----
        var count = 0;
        var totalW = 0f;
        for (var slot = 0; slot < 4; slot++)
        {
            if ((mask & (1 << slot)) == 0) continue;
            if (count++ > 0) totalW += spacing;
            totalW += iconSize;
            EntryText(slot, hp, hpMax, mana, manaMax, defense, out var a, out var b);
            if (a >= 0) totalW += textGap + TextWidth(a, b, fontSize);
        }

        var panelW = totalW + pad * 2f;
        var panelH = iconSize + pad * 2f;
        var panelX = head.X - panelW * 0.5f;
        var panelY = head.Y - headOffset - panelH;     // 面板底部距头顶 headOffset

        var bgCol = ToImU32(cfg.HudBackgroundColor);
        var borderCol = ToImU32(cfg.HudBorderColor);
        var textCol = ToImU32(cfg.HudTextColor);
        var dividerCol = ToImU32(cfg.HudDividerColor);
        // 阴影 = 纯黑 × 文字透明度的 0.8（原版同）：IM_COL32(0,0,0,alpha) = alpha << 24
        var shadowCol = ((uint)((cfg.HudTextColor >> 24) & 0xFF) * 8u / 10u) << 24;

        var pmin = new Vector2(panelX, panelY);
        var pmax = new Vector2(panelX + panelW, panelY + panelH);
        dl.AddRectFilled(pmin, pmax, bgCol, 8f * s);                             // 圆角 8×缩放，同原版
        dl.AddRect(pmin, pmax, borderCol, 8f * s, ImDrawFlags.None, 1.2f * s);   // 描边 1.2×缩放，同原版

        // ---- 第二遍：逐项绘制图标 + 数值，项间画分割线 ----
        var curX = panelX + pad;
        var cy = panelY + panelH * 0.5f;
        var first = true;
        for (var slot = 0; slot < 4; slot++)
        {
            if ((mask & (1 << slot)) == 0) continue;

            if (!first)
            {
                curX += spacing;
                var dx = curX - spacing * 0.5f;
                dl.AddLine(new Vector2(dx, panelY + pad), new Vector2(dx, panelY + panelH - pad), dividerCol, 1f);
            }
            first = false;

            // ---- 图标：MT2 纹理（IconStore 的 GL 纹理名可直接进 AddImage） ----
            var iconId = EntryIcon(slot, cfg, weaponId);
            if (IconStore.TryGet(IconStore.Kind.Item, iconId, out var texture, out var iw, out var ih)
                && texture != 0 && iw > 0 && ih > 0)
            {
                // 按原图宽高比等比缩放（contain），居中绘制
                var k = MathF.Min(iconSize / iw, iconSize / ih);
                var drawW = iw * k;
                var drawH = ih * k;
                var ix = curX + (iconSize - drawW) * 0.5f;
                var iy = cy - drawH * 0.5f;
                dl.AddImage(texture, new Vector2(ix, iy), new Vector2(ix + drawW, iy + drawH),
                    Vector2.Zero, Vector2.One, 0xFFFFFFFFu);
            }
            else
            {
                // 图标未就绪/不存在：占位框兜底（同原版）
                dl.AddRect(new Vector2(curX, cy - iconSize * 0.5f),
                    new Vector2(curX + iconSize, cy + iconSize * 0.5f),
                    borderCol, 3f * s, ImDrawFlags.None, MathF.Max(1f, s));
            }
            curX += iconSize;

            // ---- 数值：血/魔是 "当前/上限"，防御是单值 ----
            EntryText(slot, hp, hpMax, mana, manaMax, defense, out var a, out var b);
            if (a >= 0)
            {
                curX += textGap;
                DrawNumber(dl, font, clip, fontSize, ref curX, cy, textCol, shadowCol, a, b);
            }
        }
    }

    /// <summary>条目 → (图标物品 id)；武器条目用玩家手持物品。</summary>
    private static int EntryIcon(int slot, CheatConfig cfg, int weaponId) => slot switch
    {
        0 => weaponId,
        1 => cfg.HudHealthIconId,
        2 => cfg.HudManaIconId,
        3 => cfg.HudDefenseIconId,
        _ => 0,
    };

    /// <summary>条目 → 数值（a = 当前值/单值；b ≥ 0 时是 "a/b"，b &lt; 0 表示单值）。</summary>
    private static void EntryText(int slot, int hp, int hpMax, int mana, int manaMax, int defense,
        out int a, out int b)
    {
        switch (slot)
        {
            case 1: a = hp; b = hpMax; return;
            case 2: a = mana; b = manaMax; return;
            case 3: a = defense; b = -1; return;
            default: a = -1; b = -1; return;   // 武器条目只有图标
        }
    }

    // ==================== 文本（尺寸、度量、缓存） ====================

    /// <summary>
    /// 数值文本缓存：int → 字符串。稳态零分配——同一数值只在首次出现时分配一次；
    /// 上限 4096 条，溢出清空重来（避免长时间游戏后字典无限长大）。
    /// </summary>
    private static readonly Dictionary<int, string> NumberCache = new(64);

    private static string NumberText(int value)
    {
        if (NumberCache.TryGetValue(value, out var text)) return text;
        if (NumberCache.Count >= 4096) NumberCache.Clear();
        text = value.ToString();
        NumberCache[value] = text;
        return text;
    }

    /// <summary>
    /// 目标字号下的文本尺寸：<see cref="ImGui.CalcTextSize"/> 按覆盖层当前字号度量，
    /// 再按 fontSize/当前字号 线性缩放（与原版 font-&gt;CalcTextSizeA(fontSize, ...) 等价）。
    /// </summary>
    private static Vector2 TextSize(string text, float fontSize)
    {
        var baseSize = ImGui.GetFontSize();
        var scale = baseSize > 0f ? fontSize / baseSize : 1f;
        return ImGui.CalcTextSize(text) * scale;
    }

    /// <summary>数值整体宽度（a 或 "a/b"）。</summary>
    private static float TextWidth(int a, int b, float fontSize)
    {
        var w = TextSize(NumberText(a), fontSize).X;
        if (b < 0) return w;
        return w + TextSize(Slash, fontSize).X + TextSize(NumberText(b), fontSize).X;
    }

    /// <summary>画 "a" 或 "a/b"（阴影 + 正文），并把光标右移。</summary>
    private static void DrawNumber(ImDrawListPtr dl, ImFontPtr font, Vector4 clip,
        float fontSize, ref float x, float centerY, uint textCol, uint shadowCol, int a, int b)
    {
        DrawFragment(dl, font, clip, fontSize, ref x, centerY, textCol, shadowCol, NumberText(a));
        if (b < 0) return;
        DrawFragment(dl, font, clip, fontSize, ref x, centerY, textCol, shadowCol, Slash);
        DrawFragment(dl, font, clip, fontSize, ref x, centerY, textCol, shadowCol, NumberText(b));
    }

    private static void DrawFragment(ImDrawListPtr dl, ImFontPtr font, Vector4 clip,
        float fontSize, ref float x, float centerY, uint textCol, uint shadowCol, string text)
    {
        var size = TextSize(text, fontSize);
        var pos = new Vector2(x, centerY - size.Y * 0.5f);
        // 指定字号绘制：用 ImFont.RenderText —— 它就是 ImDrawList.AddText(font, size, ...)
        // 的内部实现（见 imgui_draw.cpp，两者最终都走 font->RenderText）。
        // 这里不用 AddText(font, size, ...) 是因为它的重载要 Vector4* 指针，
        // CheatMenu 工程没开 AllowUnsafeBlocks，调用会编译不过。
        // 先画 1px 偏移的阴影，再画正文（原版同样先阴影后正文）。
        font.RenderText(dl, fontSize, pos + new Vector2(1f, 1f), shadowCol, clip, text, null);
        font.RenderText(dl, fontSize, pos, textCol, clip, text, null);
        x += size.X;
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