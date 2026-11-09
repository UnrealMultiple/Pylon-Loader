using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.UI;
using OnMain = On.Terraria.Main;


namespace Pylon.ModLoader;

/// <summary>
///     物品提示管线：把游戏算出来的行变成 List&lt;TooltipLine&gt;，让作者改，再按原顺序写回游戏的数组；
///     绘制期包住原版，让 Pre/PostDrawTooltip* 能生效。
/// </summary>
internal sealed class TooltipPipeline : ILoaderModule
{
    /// <summary>行级钩子对某一行的改动：下一帧取行时套上（游戏已排版，本帧改不回去）。</summary>
    private sealed class Override
    {
        public bool Hidden;
        public string? Text;
        public Color? Color;
    }

    private readonly Dictionary<string, Override> _overrides = new(StringComparer.Ordinal);

    private List<TooltipLine> _lines = [];

    /// <summary>有 GlobalItem 接了改行钩子（对所有物品生效）；ModItem 没有钩子掩码，按类型单独查。</summary>
    private bool _anyGlobalTooltip;


    public static TooltipPipeline Instance { get; } = new();

    public string Name => "tooltip";

    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        OnMain.MouseText_DrawItemTooltip_GetLinesInfo += GetLinesInfo;
        OnMain.MouseText_DrawItemTooltip += DrawTooltip;
        Log.Info("[tooltip] 已挂（取行 + 绘制期）");
        return true;
    }

    /// <summary>安装期一次（内容装载完）。</summary>
    internal void Build()
    {
        _anyGlobalTooltip = GlobalItemRegistry.AnyWith(HookId.ModifyTooltips);
    }

    #region 取行

    private void GetLinesInfo(OnMain.orig_MouseText_DrawItemTooltip_GetLinesInfo orig, Item item,
                              ref int yoyoLogo, ref int researchLine, ref int materialsLine, float oldKB,
                              ref int numLines, Il2CppStringArray toolTipLine,
                              Il2CppStructArray<bool> preFixLine, Il2CppStructArray<bool> badPreFixLine,
                              ref int setBonusLine, ref Color setBonusColour, ref int sharedLine)
    {
        // 调用前的 numLines 决定第 0 行是不是游戏自己放进去的名字：
        // 手机版会带着 numLines = 1、第 0 行是物品名进来；从 0 开始时名字要自己补，且不写回。
        var first = numLines;


        orig(item, ref yoyoLogo, ref researchLine, ref materialsLine, oldKB, ref numLines, toolTipLine, preFixLine,
            badPreFixLine, ref setBonusLine, ref setBonusColour, ref sharedLine);

        // 原版物品一个字节都不动：只有 mod 物品，或有 GlobalItem 声明了改行时才介入。
        if (item is null || item.type <= 0) return;
        if (!_anyGlobalTooltip && ItemOf(item) is null) return;

        var texts = toolTipLine;
        var prefix = preFixLine;
        var bad = badPreFixLine;
        if (texts is null || prefix is null || bad is null) return;

        try
        {
            Rewrite(item, texts, prefix, bad, first, ref numLines, ref yoyoLogo,
                ref researchLine, ref materialsLine, ref setBonusLine, ref setBonusColour, ref sharedLine);
        }
        catch (Exception e)
        {
            ContentErrors.Report("[tooltip] 取行失败：" + Safe.Describe(e));
        }
    }

    private void Rewrite(Item item, Il2CppStringArray texts, Il2CppStructArray<bool> modifiers,
                         Il2CppStructArray<bool> bad, int first, ref int numLines, ref int yoyoLogo,
                         ref int researchLine, ref int materialsLine, ref int setBonusLine,
                         ref Color setBonusColour, ref int sharedLine)
    {
        if (texts is null || modifiers is null || bad is null) return;

        var capacity = Math.Min(texts.Length, Math.Min(modifiers.Length, bad.Length));
        var count = Math.Min(numLines, capacity);
        if (count <= 0) return;

        var roles = new Dictionary<int, string>
        {
            [yoyoLogo] = "logo",
            [researchLine] = "research",
            [materialsLine] = "materials",
            [setBonusLine] = "setBonus",
            [sharedLine] = "shared",
        };

        var lines = Build(item, texts, modifiers, bad, first > 0, count, roles);

        WriteBack(lines, texts, modifiers, bad, capacity, ref numLines, ref yoyoLogo, ref researchLine,
            ref materialsLine, ref setBonusLine, ref setBonusColour, ref sharedLine);

        _lines = lines;

    }

    private List<TooltipLine> Build(Item item, Il2CppStringArray texts, Il2CppStructArray<bool> modifiers,
                                    Il2CppStructArray<bool> bad, bool named, int count,
                                    Dictionary<int, string> roles)
    {
        var names = new TooltipNames(item);
        var lines = new List<TooltipLine>(count + 4);

        // 第 0 行永远是名字。游戏自己放进去的（named）沿用它并参与写回；
        // 从 0 开始时用 HoverName 补一行合成的，Role = "name" 表示不写回。
        var name = named && count > 0 ? texts[0] ?? "" : HoverName(item);
        lines.Add(new TooltipLine("Terraria", "ItemName", name) { Role = named ? "" : "name" });

        var used = new HashSet<string>(StringComparer.Ordinal) { "ItemName" };

        for (var i = named ? 1 : 0; i < count; i++)
        {
            var text = texts[i] ?? "";
            var modifier = modifiers[i];
            roles.TryGetValue(i, out var role);
            role ??= "";

            var lineName = names.Of(text, role, modifier);
            if (!used.Add(lineName))
            {
                var suffix = 1;
                while (!used.Add(lineName + suffix)) suffix++;
                lineName += suffix;
            }

            lines.Add(new TooltipLine("Terraria", lineName, text)
            {
                IsModifier = modifier,
                IsModifierBad = modifier && bad[i],
                OneDropLogo = role == "logo",
                Role = role,
            });
        }

        Modify(item, lines);
        ApplyOverrides(lines);

        for (var i = lines.Count - 1; i >= 0; i--)
        {
            if (!lines[i].Visible) lines.RemoveAt(i);
        }

        return lines;
    }

    private static string HoverName(Item item)
    {
        try
        {
            return item.HoverName ?? "";
        }
        catch (Exception e)
        {
            Safe.Report("tooltip.hover-name", e);
            return "";
        }
    }

    /// <summary>行级钩子在上一帧写下的改动（文字/颜色/隐藏）。</summary>
    private void ApplyOverrides(List<TooltipLine> lines)
    {
        if (_overrides.Count == 0) return;

        for (var i = lines.Count - 1; i >= 0; i--)
        {
            var line = lines[i];
            if (!_overrides.TryGetValue(line.FullName, out var over)) continue;

            if (over.Hidden)
            {
                line.Hide();
                continue;
            }

            if (over.Text is { } text) line.Text = text;
            if (over.Color is { } color) line.OverrideColor = color;
        }
    }

    /// <summary>Mod 物品与所有 GlobalItem 的改行；顺序固定（Mod 物品在前）。</summary>
    private void Modify(Item item, List<TooltipLine> lines)
    {
        if (ItemOf(item) is { } modItem)
        {
            try
            {
                modItem.ModifyTooltips(lines);
            }
            catch (Exception e)
            {
                ContentErrors.Report("[tooltip] " + modItem.Name + ".ModifyTooltips：" + Safe.Describe(e));
            }
        }

        var globals = GlobalItemRegistry.Of(item);
        for (var i = 0; i < globals.Count; i++)
        {
            var global = globals[i];

            try
            {
                global.ModifyTooltips(item, lines);
            }
            catch (Exception e)
            {
                ContentErrors.Report("[tooltip] " + global.Name + ".ModifyTooltips：" + Safe.Describe(e));
            }
        }
    }

    private static ModItem? ItemOf(Item item) => ModContent.Find(ContentKind.Item, item.type) as ModItem;

    private static void WriteBack(List<TooltipLine> lines, Il2CppStringArray texts, Il2CppStructArray<bool> modifiers,
                                  Il2CppStructArray<bool> bad, int capacity, ref int numLines, ref int yoyoLogo,
                                  ref int researchLine, ref int materialsLine, ref int setBonusLine,
                                  ref Color setBonusColour, ref int sharedLine)
    {
        yoyoLogo = researchLine = materialsLine = setBonusLine = sharedLine = -1;

        var writable = 0;
        for (var n = 0; n < lines.Count; n++)
        {
            if (lines[n].Role != "name") writable++;
        }

        var written = 0;

        for (var n = 0; n < lines.Count; n++)
        {
            var line = lines[n];

            // 合成名字只给作者看，不写回：游戏数组里那个位置是它自己的第一行。
            if (line.Role == "name") continue;
            if (written >= capacity) break;

            var i = written++;
            texts[i] = Tagged(line);
            modifiers[i] = line.IsModifier || line.IsModifierBad;
            bad[i] = line.IsModifierBad;

            if (line.OneDropLogo) yoyoLogo = i;

            switch (line.Role)
            {
                case "research": researchLine = i; break;
                case "materials": materialsLine = i; break;
                case "setBonus":
                    setBonusLine = i;
                    if (line.OverrideColor is { } bonus) setBonusColour = bonus;
                    break;
                case "shared": sharedLine = i; break;
            }
        }

        if (written < writable)
        {
            ContentErrors.Report($"[tooltip] {writable - written} 行放不下（上限 {capacity}），已截断");
        }

        numLines = written;
    }

    /// <summary>整行颜色转成游戏认得的 [c/RRGGBB:…] 标签。</summary>
    private static string Tagged(TooltipLine line)
    {
        var text = line.Text ?? "";
        return line.OverrideColor is { } color ? TooltipLine.ColorTag(text, color) : text;
    }

    #endregion

    #region 绘制期

    /// <summary>
    ///     包住原版绘制。契约只有一条：**除非作者明确要求不画，否则一定调用 orig**——
    ///     管线里任何失败都不能让游戏自己的 tooltip 消失。
    /// </summary>
    private void DrawTooltip(OnMain.orig_MouseText_DrawItemTooltip orig, Main self, Main.MouseTextCache info,
                             int rare, byte diff, int x, int y)
    {
        Item? item = null;
        List<TooltipLine>? lines = null;
        var go = true;
        var fx = x;
        var fy = y;

        try
        {
            item = Main.HoverItem;
            var candidate = _lines;

            // Wanted 门：原版物品不让任何作者回调介入。
            if (candidate.Count > 0 && item is not null && item.type > 0
                && (_anyGlobalTooltip || ItemOf(item) is not null))
            {
                lines = candidate;
                go = PreDrawTooltip(item, lines, ref fx, ref fy);
            }
        }
        catch (Exception e)
        {
            ContentErrors.Report("[tooltip] 绘制期前置失败：" + Safe.Describe(e));
            lines = null;
            go = true;
        }

        if (!go) return;

        orig(self, info, rare, diff, fx, fy);

        if (lines is null) return;

        try
        {
            var drawn = new DrawableTooltipLine[lines.Count];

            for (var i = 0; i < lines.Count; i++)
            {
                var drawable = new DrawableTooltipLine(lines[i], i, fx, fy + (int)(i * LineHeight), Color.White);
                drawn[i] = drawable;

                LineHook(item!, drawable);
            }

            PostDrawTooltip(item!, drawn);
        }
        catch (Exception e)
        {
            ContentErrors.Report("[tooltip] 绘制期回调失败：" + Safe.Describe(e));
        }
    }

    private bool PreDrawTooltip(Item item, List<TooltipLine> lines, ref int x, ref int y)
    {
        var readOnly = lines.AsReadOnly();

        if (ItemOf(item) is { } modItem)
        {
            try
            {
                if (!modItem.PreDrawTooltip(readOnly, ref x, ref y)) return false;
            }
            catch (Exception e)
            {
                ContentErrors.Report("[tooltip] " + modItem.Name + ".PreDrawTooltip：" + Safe.Describe(e));
            }
        }

        var globals = GlobalItemRegistry.Of(item);
        for (var i = 0; i < globals.Count; i++)
        {
            try
            {
                if (!globals[i].PreDrawTooltip(item, readOnly, ref x, ref y)) return false;
            }
            catch (Exception e)
            {
                ContentErrors.Report("[tooltip] PreDrawTooltip 出错：" + Safe.Describe(e));
            }
        }

        return true;
    }

    /// <summary>行级钩子：改的 Text/Color/隐藏记下来，下一帧取行时套上。</summary>
    private void LineHook(Item item, DrawableTooltipLine line)
    {
        var yOffset = 0;
        var origin = new TooltipLine(line.Mod, line.Name, line.Text)
        {
            IsModifier = line.IsModifier,
            IsModifierBad = line.IsModifierBad,
            OneDropLogo = line.OneDropLogo,
            OverrideColor = line.OverrideColor,
        };

        var go = true;

        if (ItemOf(item) is { } modItem)
        {
            try
            {
                go = modItem.PreDrawTooltipLine(line, ref yOffset);
            }
            catch (Exception e)
            {
                ContentErrors.Report("[tooltip] " + modItem.Name + ".PreDrawTooltipLine：" + Safe.Describe(e));
            }
        }

        var globals = GlobalItemRegistry.Of(item);
        for (var i = 0; go && i < globals.Count; i++)
        {
            try
            {
                go = globals[i].PreDrawTooltipLine(item, line, ref yOffset);
            }
            catch (Exception e)
            {
                ContentErrors.Report("[tooltip] PreDrawTooltipLine 出错：" + Safe.Describe(e));
            }
        }

        if (!go || line.Text != origin.Text || line.OverrideColor != origin.OverrideColor || !line.Visible)
        {
            _overrides[origin.FullName] = new Override
            {
                Hidden = !line.Visible,
                Text = line.Text != origin.Text ? line.Text : null,
                Color = line.OverrideColor != origin.OverrideColor ? line.OverrideColor : null,
            };
        }
    }

    private void PostDrawTooltip(Item item, DrawableTooltipLine[] drawn)
    {
        var readOnly = drawn.AsReadOnly();

        if (ItemOf(item) is { } modItem)
        {
            try
            {
                modItem.PostDrawTooltip(readOnly);
            }
            catch (Exception e)
            {
                ContentErrors.Report("[tooltip] " + modItem.Name + ".PostDrawTooltip：" + Safe.Describe(e));
            }
        }

        var globals = GlobalItemRegistry.Of(item);
        for (var i = 0; i < globals.Count; i++)
        {
            try
            {
                globals[i].PostDrawTooltip(item, readOnly);
            }
            catch (Exception e)
            {
                ContentErrors.Report("[tooltip] PostDrawTooltip 出错：" + Safe.Describe(e));
            }
        }

        if (ItemOf(item) is not { } owner) return;

        for (var i = 0; i < drawn.Length; i++)
        {
            try
            {
                owner.PostDrawTooltipLine(drawn[i]);
            }
            catch (Exception e)
            {
                ContentErrors.Report("[tooltip] PostDrawTooltipLine 出错：" + Safe.Describe(e));
            }
        }
    }

    private static float LineHeight => 20f;

    #endregion
}
