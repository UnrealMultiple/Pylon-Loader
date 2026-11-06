using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.UI;
using OnMain = On.Terraria.Main;

namespace Pylon.ModLoader;

/// <summary>
/// 物品提示管线：把游戏算出来的行变成 List&lt;TooltipLine&gt;，让作者改，再写回去；绘制期只负责包住与回调。
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

    /// <summary>内容装载完成之前 <see cref="Rebuild"/> 不介入（作者的钩子依赖的东西还不存在）。</summary>
    private bool _contentReady;

    /// <summary>有 GlobalItem 接了改行钩子（对所有物品生效）；ModItem 没有钩子掩码，按类型单独查。</summary>
    private bool _anyGlobalTooltip;

    /// <summary>物品类型 → 这个类型要不要跑改行（懒增长；索引查询，重建路径上没有字典开销）。</summary>
    private bool[] _wanted = [];

    public static TooltipPipeline Instance { get; } = new();

    public string Name => "tooltip";

    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        OnMain.MouseText_DrawItemTooltip_GetLinesInfo += GetLinesInfo;
        OnMain.MouseText_DrawItemTooltip += DrawTooltip;
        On.Terraria.Item.RebuildTooltip += Rebuild;
        Log.Info("[tooltip] 已挂（取行 + 绘制期 + 非悬停读取路径）");
        return true;
    }

    /// <summary>安装期一次（内容装载完）：记下有没有 GlobalItem 接改行钩子，并按需增长类型表。</summary>
    internal void Build()
    {
        _anyGlobalTooltip = GlobalItemRegistry.AnyWith(HookId.ModifyTooltips);
        _wanted = [];
        _contentReady = true;
    }

    /// <summary>这个物品类型需不需要跑改行：有全局钩子就全都要，否则看它是不是 mod 物品。</summary>
    private bool Wanted(int type)
    {
        if (type < 0) return false;
        if (_anyGlobalTooltip) return true;

        if (type >= _wanted.Length)
        {
            var grown = new bool[type + 1];
            Array.Copy(_wanted, grown, _wanted.Length);

            for (var i = _wanted.Length; i < grown.Length; i++)
                grown[i] = ModContent.Find(ContentKind.Item, i) is ModItem;

            _wanted = grown;
        }

        return _wanted[type];
    }

    #region 取行

    private void GetLinesInfo(OnMain.orig_MouseText_DrawItemTooltip_GetLinesInfo orig, Item item,
                              ref int yoyoLogo, ref int researchLine, ref int materialsLine, float oldKB,
                              ref int numLines, Il2CppStringArray toolTipLine,
                              Il2CppStructArray<bool> preFixLine, Il2CppStructArray<bool> badPreFixLine,
                              ref int setBonusLine, ref Color setBonusColour, ref int sharedLine)
    {
        orig(item, ref yoyoLogo, ref researchLine, ref materialsLine, oldKB, ref numLines, toolTipLine, preFixLine,
            badPreFixLine, ref setBonusLine, ref setBonusColour, ref sharedLine);

        try
        {
            Rewrite(item, toolTipLine, preFixLine, badPreFixLine, ref numLines, ref yoyoLogo, ref researchLine,
                ref materialsLine, ref setBonusLine, ref sharedLine);
        }
        catch (Exception e)
        {
            ContentErrors.Report("[tooltip] 取行失败：" + Safe.Describe(e));
        }
    }

    private void Rewrite(Item item, Il2CppStringArray texts, Il2CppStructArray<bool> modifiers,
                         Il2CppStructArray<bool> bad, ref int numLines, ref int yoyoLogo, ref int researchLine,
                         ref int materialsLine, ref int setBonusLine, ref int sharedLine)
    {
        if (texts is null || modifiers is null || bad is null) return;

        var capacity = Math.Min(texts.Length, Math.Min(modifiers.Length, bad.Length));
        var count = Math.Min(numLines, capacity);
        if (count <= 0) return;

        var lines = Build(item, texts, modifiers, bad, count, yoyoLogo, researchLine, materialsLine, setBonusLine,
            sharedLine);

        WriteBack(lines, texts, modifiers, bad, capacity, ref numLines, ref yoyoLogo, ref researchLine,
            ref materialsLine, ref setBonusLine, ref sharedLine);

        _lines = lines;
    }

    private List<TooltipLine> Build(Item item, Il2CppStringArray texts, Il2CppStructArray<bool> modifiers,
                                    Il2CppStructArray<bool> bad, int count, int yoyoLogo, int researchLine,
                                    int materialsLine, int setBonusLine, int sharedLine)
    {
        var names = new TooltipNames(item);
        var lines = new List<TooltipLine>(count + 4);
        var used = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < count; i++)
        {
            var text = texts[i] ?? "";
            var role = RoleOf(i, yoyoLogo, researchLine, materialsLine, setBonusLine, sharedLine);
            var modifier = modifiers[i];
            var name = names.Of(text, role, modifier);

            if (!used.Add(name))
            {
                var suffix = 1;
                while (!used.Add(name + suffix)) suffix++;
                name += suffix;
            }

            lines.Add(new TooltipLine("Terraria", name, text)
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

    private static string RoleOf(int index, int yoyoLogo, int researchLine, int materialsLine, int setBonusLine,
                                 int sharedLine)
    {
        if (index == yoyoLogo) return "logo";
        if (index == researchLine) return "research";
        if (index == materialsLine) return "materials";
        if (index == setBonusLine) return "setBonus";
        if (index == sharedLine) return "shared";
        if (index == 0) return "name";

        return "";
    }

    /// <summary>Mod 物品与所有 GlobalItem 的改行；顺序固定（Mod 物品在前）。</summary>
    private void Modify(Item item, List<TooltipLine> lines)
    {
        if (ItemOf(item) is { } modItem)
        {
            try
            {
                modItem.ModifyTooltips(item, lines);
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

    private void WriteBack(List<TooltipLine> lines, Il2CppStringArray texts, Il2CppStructArray<bool> modifiers,
                           Il2CppStructArray<bool> bad, int capacity, ref int numLines, ref int yoyoLogo,
                           ref int researchLine, ref int materialsLine, ref int setBonusLine, ref int sharedLine)
    {
        yoyoLogo = researchLine = materialsLine = setBonusLine = sharedLine = -1;

        var written = 0;

        for (var n = 0; n < lines.Count; n++)
        {
            var line = lines[n];
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
                case "setBonus": setBonusLine = i; break;
                case "shared": sharedLine = i; break;
            }
        }

        if (written < lines.Count)
        {
            ContentErrors.Report($"[tooltip] {lines.Count - written} 行放不下（上限 {capacity}），已截断");
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

    /// <summary>
    /// 非悬停读取路径（图鉴、搜索、物品栏气泡这些不走鼠标提示的地方）用的是 <c>item.ToolTip</c>。
    /// 原版算完那几行之后我们补一遍 <c>ModifyTooltips</c>，再造成新的 <c>ItemTooltip</c> 写回去。
    /// </summary>
    /// <remarks>
    /// 这个钩子的调用频度远高于悬停：物品一建出来就会调。所以两道闸：
    /// 内容没加载完直接放行（那时候 <c>ModifyTooltips</c> 依赖的东西还不存在），
    /// 整份内容里没有任何作者改提示时也直接放行（一次布尔判断，不给普通物品加字符串处理）。
    /// </remarks>
    private void Rebuild(On.Terraria.Item.orig_RebuildTooltip orig, Item self)
    {
        orig(self);

        if (!_contentReady || !Wanted(self.type)) return;

        try
        {
            RebuildCore(self);
        }
        catch (Exception e)
        {
            ContentErrors.Report("[tooltip] RebuildTooltip 失败：" + Safe.Describe(e));
        }
    }

    private void RebuildCore(Item item)
    {
        var tip = item.ToolTip;
        if (tip is null) return;

        var count = tip.Lines;
        if (count <= 0) return;

        var names = new TooltipNames(item);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var lines = new List<TooltipLine>(count + 4);
        var originals = new string[count];

        for (var i = 0; i < count; i++)
        {
            var text = tip.GetLine(i) ?? "";
            originals[i] = text;
            lines.Add(new TooltipLine(names.Of(text, i == 0 ? "name" : "", false), text));
        }

        ApplyOverrides(lines);
        Modify(item, lines);

        var texts = new string[lines.Count];
        var written = 0;
        var changed = false;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (!line.Visible) continue;

            var text = line.Text ?? "";
            if (!changed && (i >= count || text != originals[i])) changed = true;
            texts[written++] = text;
        }

        if (written == 0) return;

        // 作者没改就不替换游戏原有的 ItemTooltip（详情/图鉴界面依赖它的结构）。
        if (!changed && written == count) return;

        if (written != texts.Length) Array.Resize(ref texts, written);

        item.ToolTip = ItemTooltip.FromHardcodedText(texts);
    }

    #region 绘制期

    private void DrawTooltip(OnMain.orig_MouseText_DrawItemTooltip orig, Main self, Main.MouseTextCache info,
                             int rare, byte diff, int x, int y)
    {
        var item = Main.HoverItem;
        var lines = _lines;
        var fx = (float)x;
        var fy = (float)y;
        var go = true;

        if (lines.Count > 0)
        {
            go = PreDrawTooltip(item, lines, ref fx, ref fy);
        }

        if (!go) return;

        orig(self, info, rare, diff, (int)fx, (int)fy);

        if (lines.Count == 0) return;

        var drawn = new DrawableTooltipLine[lines.Count];

        for (var i = 0; i < lines.Count; i++)
        {
            var drawable = new DrawableTooltipLine(lines[i], i, fx, fy + i * LineHeight, Color.White);
            drawn[i] = drawable;

            LineHook(item, drawable);
        }

        PostDrawTooltip(item, drawn);
    }

    private bool PreDrawTooltip(Item item, List<TooltipLine> lines, ref float x, ref float y)
    {
        if (ItemOf(item) is { } modItem)
        {
            try
            {
                if (!modItem.PreDrawTooltip(item, lines, ref x, ref y)) return false;
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
                if (!globals[i].PreDrawTooltip(item, lines, ref x, ref y)) return false;
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
        var yOffset = 0f;
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
                go = modItem.PreDrawTooltipLine(item, line, ref yOffset);
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
        if (ItemOf(item) is { } modItem)
        {
            try
            {
                modItem.PostDrawTooltip(item, drawn);
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
                globals[i].PostDrawTooltip(item, drawn);
            }
            catch (Exception e)
            {
                ContentErrors.Report("[tooltip] PostDrawTooltip 出错：" + Safe.Describe(e));
            }
        }

        for (var i = 0; i < drawn.Length; i++)
        {
            if (ItemOf(item) is not { } owner) break;

            try
            {
                owner.PostDrawTooltipLine(item, drawn[i]);
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
