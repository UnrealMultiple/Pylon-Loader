using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Localization;
using OnMain = On.Terraria.Main;

namespace Pylon.ModLoader;

/// <summary>
/// 物品提示管线：把游戏算出来的行变成 List&lt;TooltipLine&gt; 让作者改再按原顺序写回游戏的数组；
/// 绘制期拦 <c>SpriteBatch.DrawString</c>/<c>SpriteFont.MeasureString</c>，每一行以真实绘制参数
/// 过一遍 Pre/PostDrawTooltipLine（改字、改色、改字体/旋转/缩放当帧生效），整条的前后是
/// Pre/PostDrawTooltip；彩名（稀有度）与 [c/RRGGBB:…] 标签也在这层渲染。
/// </summary>
internal sealed class TooltipPipeline : ILoaderModule
{
    private sealed class Drawing
    {
        public Item Item = null!;
        public List<TooltipLine> Lines = null!;
        public bool CanDraw = true;
        public int Dx, Dy;
        public int Next;
        public int Shift;
        public int YOffset;
        public int Index = -1;
        public Color? NameColor;
        public string Text = "";
        public readonly List<Pass> Shadows = new();
        public readonly List<DrawableTooltipLine> Drawn = new();
        public readonly Dictionary<int, (DrawableTooltipLine Line, bool Go, int Shift)> Done = new();
    }

    private struct Pass
    {
        public SpriteBatch Self;
        public SpriteFont Font;
        public string Text;
        public Vector2 Position;
        public Color Color;
        public float Rotation;
        public Vector2 Origin;
        public float Scale;
        public SpriteEffects Effects;
        public float Depth;
    }

    private static readonly Regex Tag = new(@"\[c/([0-9a-fA-F]{6}):([^\]]*)\]", RegexOptions.Compiled);

    private const int PassesPerLine = 5;

    private static readonly Color SetBonusColor = new(50, 205, 50, 255);

    private List<TooltipLine> _lines = [];

    private long _linesItem;

    private bool _anyGlobalTooltip;

    internal static bool DamageLines;

    private static Drawing? _drawing;

    private static string? _recolorText;

    private static Color _recolorColor;

    private static int _recolorCount;

    private static On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.orig_DrawString_SpriteFont_string_Vector2_Color_float_Vector2_float_SpriteEffects_float? _drawOrig;

    private static On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteFont.orig_MeasureString_string? _measureOrig;

    public static TooltipPipeline Instance { get; } = new();

    public string Name => "tooltip";

    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        OnMain.MouseText_DrawItemTooltip_GetLinesInfo += GetLinesInfo;
        OnMain.MouseText_DrawItemTooltip += DrawTooltip;
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.DrawString_SpriteFont_string_Vector2_Color_float_Vector2_float_SpriteEffects_float += DrawString;
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteFont.MeasureString_string += MeasureString;
        Log.Info("[tooltip] 已挂（取行 + 绘制期逐行）");
        return true;
    }

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
        var first = numLines;

        orig(item, ref yoyoLogo, ref researchLine, ref materialsLine, oldKB, ref numLines, toolTipLine, preFixLine,
            badPreFixLine, ref setBonusLine, ref setBonusColour, ref sharedLine);

        if (item is null || item.type <= 0) return;
        if (!_anyGlobalTooltip && ItemOf(item) is null && !PrefixWanted(item) && !item.wornArmor
            && !(DamageLines && item.damage > 0)) return;

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

    private bool PrefixWanted(Item item)
    {
        return PrefixTable.Of(item.prefix) is { } prefix && PrefixTable.HasHook(prefix, HookId.GetTooltipLines);
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
        _linesItem = item.Pointer;
    }

    private List<TooltipLine> Build(Item item, Il2CppStringArray texts, Il2CppStructArray<bool> modifiers,
                                    Il2CppStructArray<bool> bad, bool named, int count,
                                    Dictionary<int, string> roles)
    {
        var names = new TooltipNames(item);
        var lines = new List<TooltipLine>(count + 4);

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

        InsertPrefixLines(item, lines);
        if (DamageLines)
        {
            try { DamageClassHooks.Tooltip(item, lines); }
            catch (Exception e) { ContentErrors.Report("[tooltip] DamageClass 行重排异常：" + Safe.Describe(e)); }
        }
        AddSetBonus(item, lines);

        Modify(item, lines);

        for (var i = lines.Count - 1; i >= 0; i--)
        {
            if (!lines[i].Visible) lines.RemoveAt(i);
        }

        return lines;
    }

    private static void InsertPrefixLines(Item item, List<TooltipLine> lines)
    {
        if (item is null || item.prefix <= 0) return;
        if (PrefixTable.Of(item.prefix) is not { } prefix || !PrefixTable.HasHook(prefix, HookId.GetTooltipLines)) return;

        List<TooltipLine>? extra;
        try
        {
            extra = prefix.GetTooltipLines(item)?.Where(l => l is not null).ToList();
        }
        catch (Exception e)
        {
            ContentErrors.Report("[tooltip] " + prefix.Name + ".GetTooltipLines：" + Safe.Describe(e));
            return;
        }

        if (extra is not { Count: > 0 }) return;

        var at = lines.FindLastIndex(l => l.IsModifier || l.IsModifierBad);
        lines.InsertRange(at < 0 ? lines.Count : at + 1, extra);
    }

    private static void AddSetBonus(Item item, List<TooltipLine> lines)
    {
        if (!item.wornArmor || lines.Exists(l => l.Role == "setBonus")) return;
        if (EquipPipeline.SetBonusText(Main.LocalPlayer) is not { } bonus || bonus.Length == 0) return;
        if (lines.Exists(l => l.Name is "SetBonus")) return;

        string text;
        try
        {
            var template = Language.GetTextValue("UI.ItemSetBonusGeneral") ?? "";
            text = template.Contains("{Description}", StringComparison.Ordinal) ? template.Replace("{Description}", bonus) : bonus;
        }
        catch
        {
            text = bonus;
        }

        var at = lines.FindIndex(l => l.Role is "shared" or "research" || l.Name is "LoadoutShareHint" or "JourneyResearch" or "JourneyResearchByTeammate");
        lines.Insert(at < 0 ? lines.Count : at, new TooltipLine("Terraria", "SetBonus", text) { Role = "setBonus", OverrideColor = SetBonusColor });
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

            if (line.Role == "name") continue;
            if (written >= capacity) break;

            var i = written++;
            texts[i] = line.Text ?? "";
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

    #endregion

    #region 绘制期

    private bool Wanted(Item item) =>
        _anyGlobalTooltip || ItemOf(item) is not null || PrefixWanted(item) || item.wornArmor
        || DamageLines && item.damage > 0;

    internal static void WantDamageLines() => DamageLines = true;

    private bool Prepare(Item item)
    {
        var texts = new Il2CppStringArray(64);
        var modifiers = new Il2CppStructArray<bool>(64);
        var bad = new Il2CppStructArray<bool>(64);
        int yoyo = -1, research = -1, materials = -1, count = 1, setBonus = -1, shared = -1;
        var setColor = new Color(255, 255, 255, 255);

        Main.MouseText_DrawItemTooltip_GetLinesInfo(item, ref yoyo, ref research, ref materials,
            item.knockBack, ref count, texts, modifiers, bad, ref setBonus, ref setColor, ref shared);

        return item.Pointer == _linesItem && _lines.Count > 0;
    }

    private void DrawTooltip(OnMain.orig_MouseText_DrawItemTooltip orig, Main self, Main.MouseTextCache info,
                             int rare, byte diff, int x, int y)
    {
        Item? item = null;
        Drawing? state = null;

        try
        {
            item = Main.HoverItem;
            if (_drawing is null && item is not null && item.type > 0 && Wanted(item) && Prepare(item))
            {
                state = new Drawing { Item = item, Lines = _lines };
                var px = x;
                var py = y;
                state.CanDraw = PreDrawTooltip(item, state.Lines.AsReadOnly(), ref px, ref py);
                state.Dx = px - x;
                state.Dy = py - y;

                bool needed = DrawHooks(item) || state.Lines.Exists(l => l.OverrideColor is not null || HasTags(l.Text));
                if (!needed) state = null;
            }
        }
        catch (Exception e)
        {
            ContentErrors.Report("[tooltip] 绘制期前置失败：" + Safe.Describe(e));
            state = null;
        }

        Color? rarity = null;
        try
        {
            var mod = RarityTable.Of(rare) ?? (item is null || item.type <= 0 ? null : RarityTable.Of(item.rare));
            if (mod is not null) rarity = RarityTable.ColorOf(mod.Type);
        }
        catch (Exception e)
        {
            ContentErrors.Report("[tooltip] RarityColor：" + Safe.Describe(e));
        }

        _drawing = state;
        if (state is not null) state.NameColor = rarity;
        else if (rarity is { } rc && item is not null && item.type > 0)
        {
            try
            {
                if (item.HoverName is { } hover && hover.Length > 0) Recolor(hover, rc);
            }
            catch
            {
            }
        }

        try
        {
            orig(self, info, rare, diff, x, y);
        }
        finally
        {
            _drawing = null;
            if (state is null && rarity is not null) EndRecolor();
        }

        if (state is null) return;

        try
        {
            Flush(state);
            PostDrawTooltip(state.Item, state.Drawn.AsReadOnly());
        }
        catch (Exception e)
        {
            ContentErrors.Report("[tooltip] PostDrawTooltip：" + Safe.Describe(e));
        }
    }

    private static void DrawString(
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.orig_DrawString_SpriteFont_string_Vector2_Color_float_Vector2_float_SpriteEffects_float orig,
        SpriteBatch self, SpriteFont font, string text, Vector2 position, Color color, float rotation, Vector2 origin,
        float scale, SpriteEffects effects, float depth)
    {
        _drawOrig = orig;
        var state = _drawing;
        if (state is not null && text is not null)
        {
            try
            {
                var pass = new Pass
                {
                    Self = self, Font = font, Text = text, Position = position, Color = color, Rotation = rotation,
                    Origin = origin, Scale = scale, Effects = effects, Depth = depth,
                };
                if (Take(state, text, pass)) return;
            }
            catch (Exception e)
            {
                ContentErrors.Report("[tooltip] PreDrawTooltipLine：" + Safe.Describe(e));
            }
        }
        else if (_recolorText is not null && text is not null && text == _recolorText && ++_recolorCount % PassesPerLine == 0)
        {
            var alpha = color.A / 255f;
            color = new Color((int)(_recolorColor.R * alpha), (int)(_recolorColor.G * alpha), (int)(_recolorColor.B * alpha), color.A);
        }

        orig(self, font, text, position, color, rotation, origin, scale, effects, depth);
    }

    private static Vector2 MeasureString(
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteFont.orig_MeasureString_string orig,
        SpriteFont self, string text)
    {
        _measureOrig = orig;
        if (_drawing is not null && text is not null && HasTags(text))
        {
            try
            {
                text = Strip(text);
            }
            catch (Exception e)
            {
                ContentErrors.Report("[tooltip] tooltip color tags：" + Safe.Describe(e));
            }
        }

        return orig(self, text);
    }

    internal static void Recolor(string text, Color color)
    {
        _recolorText = text;
        _recolorColor = color;
        _recolorCount = 0;
    }

    internal static void EndRecolor() => _recolorText = null;

    private static bool HasTags(string? text) => text is not null && text.Contains("[c/", StringComparison.Ordinal);

    private static string Strip(string text) => Tag.Replace(text, "$2");

    private static bool DrawHooks(Item item)
    {
        if (ItemOf(item) is { } modItem && AnyDrawHook(modItem.Plan)) return true;

        var globals = GlobalItemRegistry.Of(item);
        for (var i = 0; i < globals.Count; i++)
        {
            if (AnyDrawHook(globals[i].Plan)) return true;
        }

        return false;
    }

    private static bool AnyDrawHook(HookMask mask) =>
        mask.Has(HookId.PreDrawTooltip) || mask.Has(HookId.PostDrawTooltip) ||
        mask.Has(HookId.PreDrawTooltipLine) || mask.Has(HookId.PostDrawTooltipLine);

    private static bool Take(Drawing state, string text, Pass pass)
    {
        if (state.Index >= 0 && text == state.Text && state.Shadows.Count < PassesPerLine)
        {
            state.Shadows.Add(pass);
            if (state.Shadows.Count == PassesPerLine) Flush(state);
            return true;
        }

        var at = state.Lines.FindIndex(state.Next, l => l.Text == text);
        if (at < 0) at = state.Lines.FindIndex(l => l.Text == text);
        if (at < 0) return false;

        Flush(state);
        state.Index = at;
        state.Text = text;
        state.Next = at + 1;
        state.Shadows.Add(pass);
        return true;
    }

    private static void Line(Drawing state, Pass main)
    {
        state.Shadows.RemoveAt(state.Shadows.Count - 1);
        var line = state.Lines[state.Index];
        var color = main.Color;
        if (state.NameColor is { } named && line.Mod == "Terraria" && line.Name == "ItemName")
        {
            var alpha = color.A / 255f;
            color = new Color((int)(named.R * alpha), (int)(named.G * alpha), (int)(named.B * alpha), color.A);
        }
        if (line.OverrideColor is { } over)
        {
            var alpha = color.A / 255f;
            color = new Color((int)(over.R * alpha), (int)(over.G * alpha), (int)(over.B * alpha), color.A);
        }

        var first = !state.Done.TryGetValue(state.Index, out var done);
        DrawableTooltipLine dl;
        bool go;
        int shift;
        if (first)
        {
            dl = new DrawableTooltipLine(line, state.Index, (int)main.Position.X, (int)main.Position.Y, color)
            {
                Font = main.Font,
                Rotation = main.Rotation,
                Origin = main.Origin,
                BaseScale = new Vector2(main.Scale),
            };
            go = PreDrawTooltipLine(state.Item, dl, ref state.YOffset) && state.CanDraw;
            shift = state.Shift;
            state.Done[state.Index] = (dl, go, shift);
        }
        else
        {
            (dl, go, shift) = done;
        }

        if (go)
        {
            var dx = dl.X - dl.OriginalX + state.Dx;
            var dy = dl.Y - dl.OriginalY + state.Dy + shift;
            var spread = dl.Spread / 2f;
            foreach (var s in state.Shadows)
            {
                var shadow = s;
                shadow.Position = new Vector2(main.Position.X + dx + (s.Position.X - main.Position.X) * spread,
                    main.Position.Y + dy + (s.Position.Y - main.Position.Y) * spread);
                Apply(ref shadow, dl);
                Write(shadow, Strip(line.Text), true, dl);
            }

            var text = main;
            text.Position = new Vector2(main.Position.X + dx, main.Position.Y + dy);
            text.Color = dl.Color;
            Apply(ref text, dl);
            Write(text, line.Text, false, dl);
        }

        state.Shadows.Clear();
        if (first)
        {
            PostDrawTooltipLine(state.Item, dl);
            state.Drawn.Add(dl);
            state.Shift += state.YOffset;
        }
        state.Index = -1;
        state.Text = "";
    }

    private static void Apply(ref Pass p, DrawableTooltipLine dl)
    {
        p.Font = dl.Font;
        p.Rotation = dl.Rotation;
        p.Origin = dl.Origin;
        p.Scale = dl.BaseScale.X;
    }

    private static void Write(Pass p, string text, bool shadowColor, DrawableTooltipLine dl)
    {
        if (shadowColor || !HasTags(text))
        {
            Original(in p, Strip(text));
            return;
        }

        var baseColor = p.Color;
        var alpha = baseColor.A / 255f;
        var x = p.Position.X;
        var at = 0;
        foreach (Match m in Tag.Matches(text))
        {
            if (m.Index > at) x = Segment(in p, text[at..m.Index], baseColor, x);
            var rgb = Convert.ToInt32(m.Groups[1].Value, 16);
            var c = new Color((int)(((rgb >> 16) & 255) * alpha), (int)(((rgb >> 8) & 255) * alpha), (int)((rgb & 255) * alpha), baseColor.A);
            x = Segment(in p, m.Groups[2].Value, c, x);
            at = m.Index + m.Length;
        }
        if (at < text.Length) Segment(in p, text[at..], baseColor, x);
    }

    private static float Segment(in Pass p, string text, Color color, float x)
    {
        if (text.Length == 0) return x;
        var pass = p;
        pass.Position = new Vector2(x, p.Position.Y);
        pass.Color = color;
        Original(in pass, text);
        return x + Measure(pass.Font, text).X * pass.Scale;
    }

    private static void Original(in Pass p, string text)
    {
        if (_drawOrig is not { } orig) return;
        orig(p.Self, p.Font, text, p.Position, p.Color, p.Rotation, p.Origin, p.Scale, p.Effects, p.Depth);
    }

    private static Vector2 Measure(SpriteFont font, string text)
    {
        if (_measureOrig is not { } orig) return default;
        try
        {
            return orig(font, text);
        }
        catch
        {
            return default;
        }
    }

    private static void Flush(Drawing state)
    {
        if (state.Index >= 0 && state.Shadows.Count > 0) Line(state, state.Shadows[^1]);
        state.Shadows.Clear();
        state.Index = -1;
        state.Text = "";
    }

    private static bool PreDrawTooltip(Item item, ReadOnlyCollection<TooltipLine> lines, ref int x, ref int y)
    {
        bool go = true;
        if (ItemOf(item) is { } modItem)
        {
            try
            {
                go &= modItem.PreDrawTooltip(lines, ref x, ref y);
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
                go &= globals[i].PreDrawTooltip(item, lines, ref x, ref y);
            }
            catch (Exception e)
            {
                ContentErrors.Report("[tooltip] PreDrawTooltip 出错：" + Safe.Describe(e));
            }
        }

        return go;
    }

    private static void PostDrawTooltip(Item item, ReadOnlyCollection<DrawableTooltipLine> lines)
    {
        if (ItemOf(item) is { } modItem)
        {
            try
            {
                modItem.PostDrawTooltip(lines);
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
                globals[i].PostDrawTooltip(item, lines);
            }
            catch (Exception e)
            {
                ContentErrors.Report("[tooltip] PostDrawTooltip 出错：" + Safe.Describe(e));
            }
        }
    }

    private static bool PreDrawTooltipLine(Item item, DrawableTooltipLine line, ref int yOffset)
    {
        bool go = true;
        if (ItemOf(item) is { } modItem)
        {
            try
            {
                go &= modItem.PreDrawTooltipLine(line, ref yOffset);
            }
            catch (Exception e)
            {
                ContentErrors.Report("[tooltip] " + modItem.Name + ".PreDrawTooltipLine：" + Safe.Describe(e));
            }
        }

        var globals = GlobalItemRegistry.Of(item);
        for (var i = 0; i < globals.Count; i++)
        {
            try
            {
                go &= globals[i].PreDrawTooltipLine(item, line, ref yOffset);
            }
            catch (Exception e)
            {
                ContentErrors.Report("[tooltip] PreDrawTooltipLine 出错：" + Safe.Describe(e));
            }
        }

        return go;
    }

    private static void PostDrawTooltipLine(Item item, DrawableTooltipLine line)
    {
        if (ItemOf(item) is { } modItem)
        {
            try
            {
                modItem.PostDrawTooltipLine(line);
            }
            catch (Exception e)
            {
                ContentErrors.Report("[tooltip] " + modItem.Name + ".PostDrawTooltipLine：" + Safe.Describe(e));
            }
        }

        var globals = GlobalItemRegistry.Of(item);
        for (var i = 0; i < globals.Count; i++)
        {
            try
            {
                globals[i].PostDrawTooltipLine(item, line);
            }
            catch (Exception e)
            {
                ContentErrors.Report("[tooltip] Global PostDrawTooltipLine 出错：" + Safe.Describe(e));
            }
        }
    }

    #endregion
}
