// 层：Mod 框架 / Tooltips —— 物品 tooltip 绘制管线
// 手机版 MouseText_DrawItemTooltip 只认原版物品（type < ItemID.Count），mod 物品悬停时一路空引用画不出；
// 这里挂钩 MouseText_DrawItemTooltip / MouseText_DrawItemTooltip_GetLinesInfo，mod 物品自己画（VanillaTooltips 组行 + ModifyTooltips），原版走游戏自己的，零改动。
// 手机没有 PC 的 [c/RRGGBB:文字] 标签解析器：带标签的行按段画（测量时去标签），逐段上色。
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI;
using Terraria.ID;
using Terraria.Localization;
using Terraria.UI;

namespace Pylon.ModLoader.Tooltips
{
    /// <summary>mod 物品的 tooltip 绘制管线：安装挂钩、组行、调 ModifyTooltips、画。</summary>
    internal static class TooltipLoader
    {
        // 颜色标签
        private static readonly Regex ColorTag = new(@"\[c/([0-9a-fA-F]{6}):([^\]]*)\]", RegexOptions.Compiled);

        private static Harmony _harmony;
        private static ModLogger _logger;
        private static bool _broken;   // 我们的绘制抛过异常：退成游戏自己的，别每帧刷屏

        /// <summary>内容就绪后装一次（幂等）。</summary>
        public static void Install(Harmony harmony, ModLogger logger)
        {
            if (_harmony != null) return;
            _harmony = harmony;
            _logger = logger;

            // 主绘制：鼠标悬停/按住物品时的 tooltip 框
            PatchMain(nameof(Main.MouseText_DrawItemTooltip), new HarmonyMethod(
                typeof(TooltipLoader).GetMethod(nameof(DrawTooltipPrefix), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)));
            // 合成指南弹窗：只填字符串行（不带颜色）
            PatchMain(nameof(Main.MouseText_DrawItemTooltip_GetLinesInfo), new HarmonyMethod(
                typeof(TooltipLoader).GetMethod(nameof(GetLinesInfoPrefix), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)));
            _logger?.Info("[tooltip] 已接管 mod 物品 tooltip 绘制（MouseText_DrawItemTooltip / GetLinesInfo）");
        }

        private static void PatchMain(string name, HarmonyMethod method)
        {
            try
            {
                var target = typeof(Main).GetMethod(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                                                      System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance);
                if (target == null)
                {
                    _logger?.Warn($"[tooltip] 找不到 Main.{name}，mod tooltip 不可用");
                    return;
                }
                _harmony.Patch(target, prefix: method);
            }
            catch (Exception e)
            {
                _logger?.Warn($"[tooltip] 挂 Main.{name} 失败：{e.Message}");
            }
        }

        // ---- 主绘制：对 mod 物品跳过原版（原版会空引用），自己画；其余原版 ----

        private static bool DrawTooltipPrefix(int __1, byte __2)
        {
            try
            {
                var item = Main.HoverItem;
                if (item == null || !IsModItem(item)) return true;
                if (_broken) return false;
                Draw(item, __1, __2);
                return false;
            }
            catch (Exception e)
            {
                _broken = true;
                try { _logger?.Warn($"[tooltip] mod tooltip 绘制失败，退回游戏绘制：{e}\n{e.StackTrace}"); } catch { }
                return false;
            }
        }

        // ---- 合成指南弹窗：填 toolTipLine[]（无颜色）；mod 物品用我们的行 ----

        private static bool GetLinesInfoPrefix(Item item, ref int numLines, Il2CppStringArray toolTipLine,
                                               Il2CppStructArray<bool> preFixLine, Il2CppStructArray<bool> badPreFixLine)
        {
            try
            {
                if (item == null || !IsModItem(item)) return true;
                if (_broken) return true;

                var built = VanillaTooltips.Build(item, item.knockBack);
                var list = Modify(item, built.Lines, built.PrefixEnd);
                var count = Math.Min(list.Count, toolTipLine.Length);
                for (var i = 0; i < count; i++)
                {
                    toolTipLine[i] = StripTags(list[i].Text ?? "");
                    if (i < preFixLine.Length) preFixLine[i] = list[i].IsModifier || list[i].IsModifierBad;
                    if (i < badPreFixLine.Length) badPreFixLine[i] = list[i].IsModifierBad;
                }
                numLines = count;
                return false;
            }
            catch (Exception e)
            {
                _broken = true;
                try { _logger?.Warn($"[tooltip] 指南弹窗行失败：{e.Message}"); } catch { }
                return true;
            }
        }

        // ---- 是不是需要 mod tooltip 的物品 ----

        private static bool IsModItem(Item item) =>
            item.type >= VanillaCounts.Item && ModContent.Find(ContentKind.Item, item.type) != null;

        // ---- 画 ----

        private static void Draw(Item item, int rare, byte diff)
        {
            var batch = Main.spriteBatch;
            var font = FontAssets.MouseText.Value;
            var tips = Settings.Instance.Tooltips;
            var opaqueBox = Main.SettingsEnabled_OpaqueBoxBehindTooltips;
            var distX = tips.Distance.X;
            var distY = tips.Distance.Y;
            var pad = opaqueBox ? tips.Backing.Size : Vector2.Zero;
            var padX = pad.X;
            var padY = pad.Y;
            var scale = tips.Scale;
            var drawOffset = tips.DrawOffset;

            // 位置：从手指边上有空间的一侧
            var screenW = Main.screenWidth;
            var screenH = Main.screenHeight;
            var screenHalf = screenW > 889 ? screenW / 2 : 445;
            bool toRight;
            int x, y;
            if (Main.overloadedMousePositionX != -1)
            {
                x = Main.overloadedMousePositionX;
                y = Main.overloadedMousePositionY + (int)drawOffset.Y;
                Main.overloadedMousePositionX = -1;
                Main.overloadedMousePositionY = -1;
                toRight = true;
            }
            else
            {
                toRight = Main.mouseX < screenHalf;
                x = Main.mouseX + (toRight ? 1 : -1) * (int)drawOffset.X;
                y = Main.mouseY + (int)drawOffset.Y;
            }
            if (distX > x) x = (int)distX;
            if (distY > y) y = (int)distY;

            var alphaByte = (int)(XNAUIInputLayer.UITextAlphaCustom(Main.inventoryTooltipTime, 1, 0) * 255);
            if (alphaByte == 0) return;
            var alpha = alphaByte / 255f;

            // 手套/蘑菇那层推进击退：进文本前先乘，出文本后还原（游戏在 HoverItem 上做同一件事）
            var oldKB = item.knockBack;
            var me = Main.LocalPlayer;
            if (me != null)
            {
                var kbMult = item.melee && me.kbGlove ? 2f : 1f;
                if (me.kbBuff) kbMult += 0.5f;
                if (kbMult != 1f) item.knockBack *= kbMult;
                if (item.ranged && me.shroomiteStealth) item.knockBack *= 1f + (1f - me.stealth) * 0.5f;
            }

            var built = VanillaTooltips.Build(item, oldKB);
            var ctx = new DrawContext
            {
                Alpha = alpha,
                NameColor = NameColor(item.rare, diff, item.expert),
                SetBonusColor = built.SetBonusColor,
                PriceColor = Color.Multiply(Color.White, alpha),
            };
            PriceLines(item, built.Lines, ctx);

            var tooltips = Modify(item, built.Lines, built.PrefixEnd);
            var count = tooltips.Count;
            var drawables = new DrawableTooltipLine[count];
            for (var i = 0; i < count; i++)
            {
                var dl = new DrawableTooltipLine(tooltips[i], i, 0, 0, LineColor(tooltips[i], ctx));
                dl.BaseScale = new Vector2(scale, scale);
                drawables[i] = dl;
            }

            // 测量
            var lineW = new float[count];
            var lineH = new float[count];
            float sizeX = 0, sizeY = 0;
            for (var i = 0; i < count; i++)
            {
                var m = font.MeasureString(StripTags(tooltips[i].Text ?? ""));
                lineW[i] = m.X * scale;
                lineH[i] = m.Y * scale;
                if (lineW[i] > sizeX) sizeX = lineW[i];
                sizeY += lineH[i];
            }

            if (toRight)
            {
                if (x + sizeX + distX + padX * 2 > screenW) x = (int)(screenW - sizeX - distX - padX * 2);
            }
            else if (x - sizeX - distX - padX * 2 < 0)
            {
                x = (int)(padX * 2) + (int)distX + (int)sizeX;
            }
            if (y + sizeY + distY > screenH) y = (int)(screenH - sizeY - distY);

            if (opaqueBox) DrawBacking(item, toRight ? x : x - (int)sizeX, y, (int)sizeX + (int)(padX * 2), (int)sizeY + (int)(padY * 2), tips, alpha);

            float X = x;
            float Y = y;
            var canDraw = PreDrawTooltip(item, tooltips, ref X, ref Y);
            x = (int)X;
            y = (int)Y;

            // 行。yOffset 是 tModLoader 的：某一行的 yOffset 把后面所有行再推远
            var shadow = Color.Multiply(Color.Black, alpha);
            float yOffset = 0;
            float lineTop = 0;
            for (var i = 0; i < count; i++)
            {
                var dl = drawables[i];
                var top = y + (int)lineTop;
                dl.OriginalX = dl.X = (toRight ? x : x - (int)lineW[i]) + (int)padX;
                dl.OriginalY = dl.Y = top + (int)padY;
                if (PreDrawTooltipLine(item, dl, ref yOffset) && canDraw)
                {
                    var right = !toRight && tooltips[i].Role == "setBonus";
                    DrawText(batch, dl, shadow, right);
                }
                PostDrawTooltipLine(item, dl);
                lineTop += (int)lineH[i] + (int)yOffset;
            }

            PostDrawTooltip(item, drawables);
        }

        // 四道阴影 + 正文；套装行在左侧对齐时右对齐
        private static void DrawText(SpriteBatch batch, DrawableTooltipLine dl, Color shadow, bool right)
        {
            var s = dl.Spread;
            var offsets = new[] { new Vector2(-s, 0), new Vector2(s, 0), new Vector2(0, -s), new Vector2(0, s), Vector2.Zero };
            for (var pass = 0; pass < 5; pass++)
            {
                var pos = new Vector2(dl.X + offsets[pass].X, dl.Y + offsets[pass].Y);
                var color = pass < 4 ? shadow : dl.Color;
                if (right)
                {
                    // 套装行：文本右缘对齐（对齐由测量宽度推）
                    var w = dl.Font.MeasureString(StripTags(dl.Text ?? "")).X * dl.BaseScale.X;
                    batch.DrawString(dl.Font, dl.Text, new Vector2(pos.X - w, pos.Y), color, dl.Rotation, dl.Origin, dl.BaseScale.X, 0, 0);
                }
                else
                {
                    DrawStringSegments(batch, dl.Font, dl.Text, pos, color, dl.Rotation, dl.Origin, dl.BaseScale.X, 0, 0);
                }
            }
        }

        // 带 [c/RRGGBB:文字] 标签的文本按段画（测量时去标签）
        private static void DrawStringSegments(SpriteBatch batch, SpriteFont font, string text, Vector2 pos, Color color,
                                               float rotation, Vector2 origin, float scale, SpriteEffects effects, float layerDepth)
        {
            if (!HasTags(text))
            {
                batch.DrawString(font, text, pos, color, rotation, origin, scale, effects, layerDepth);
                return;
            }
            var shadow = color.R == 0 && color.G == 0 && color.B == 0;
            var alpha = color.A / 255f;
            var x = pos.X;
            foreach (var seg in Segments(text))
            {
                if (seg.Text.Length == 0) continue;
                var c = !shadow && seg.Rgb.HasValue
                    ? new Color((int)(seg.Rgb.Value.R * alpha), (int)(seg.Rgb.Value.G * alpha), (int)(seg.Rgb.Value.B * alpha), color.A)
                    : color;
                batch.DrawString(font, seg.Text, new Vector2(x, pos.Y), c, rotation, origin, scale, effects, layerDepth);
                x += font.MeasureString(seg.Text).X * scale;
            }
        }

        // ---- 背景：颜色按物品在哪（商店/箱子/装备…）----

        // C# 侧要显式建 Il2CppSystem.Nullable（传 null 会 NRE）。
        private static readonly Il2CppSystem.Nullable<Il2CppMicrosoft.Xna.Framework.Rectangle> NoSourceFrame = new();

        private static void DrawBacking(Item item, int x, int y, int w, int h, SettingsTooltips tips, float alpha)
        {
            // 背景是装饰：任何一步拿不到就跳过，文字行照画（游戏自己的路径也常因资源未建而退让）。
            try
            {
                var settings = Settings.Instance;
                if (settings == null) return;                       // 游戏设置还没建（主菜单前）
                var backing = tips != null ? tips.Backing : null;    // tooltip 面板布局
                if (backing == null) return;

                var color = backing.BackingColor;
                switch (item.tooltipContext)
                {
                    case 29: color = settings.DuplicationTooltipBacking; break;
                    case 32: color = settings.VoidItemTooltipBacking; break;
                    case 35: color = settings.BannerTooltipBacking; break;
                    case 15: color = settings.ShopTooltipBacking; break;
                    case 3: color = settings.ChestTooltipBacking; break;
                    case 44: color = settings.ResearchTooltipBacking; break;
                    case 4: color = settings.BankTooltipBacking; break;
                    case 5: color = settings.PrefixTooltipBacking; break;
                    case 47: color = settings.CraftingStationTooltipBacking; break;
                    case 8: case 9: case 10: case 11: case 12:
                    {
                        var loadout = Main.LocalPlayer != null ? Main.LocalPlayer.CurrentLoadoutIndex : 0;
                        if (loadout == 0) color = settings.Equip0TooltipBacking;
                        else if (loadout == 1) color = settings.Equip1TooltipBacking;
                        else if (loadout == 2) color = settings.Equip2TooltipBacking;
                        break;
                    }
                    case 16: case 17: case 18: case 19: case 20: case 33:
                        color = settings.Equip0TooltipBacking;
                        break;
                }

                // 背景纹理没加载（游戏还没建这些资源）就只画边框或跳过；文字行照画。
                var backingLayout = backing.Backing;
                var borderLayout = backing.Border;
                if (backingLayout == null && borderLayout == null) return;

                var pos = new Vector2(x, y);
                var size = new Vector2(w, h);
                if (backingLayout != null && backingLayout.Texture != null)
                    GUIPanel.DrawBacking(pos, size, backingLayout.Texture, Color.Multiply(color, alpha), NoSourceFrame);
                if (borderLayout != null && borderLayout.Texture != null)
                    GUIPanel.DrawBacking(pos, size, borderLayout.Texture, Color.Multiply(backing.BorderColor, alpha), NoSourceFrame);
            }
            catch { /* 背景缺失/未建：跳过，文字照画 */ }
        }

        // ---- 价格行（商店 / 重铸）----

        private static void PriceLines(Item item, List<TooltipLine> lines, DrawContext ctx)
        {
            var alpha = ctx.Alpha;
            var coinText = new Func<int, int, int, int, string>((plat, gold, silver, copper) =>
                (plat > 0 ? plat + " " + Lang.inter[15].Value + " " : "") +
                (gold > 0 ? gold + " " + Lang.inter[16].Value + " " : "") +
                (silver > 0 ? silver + " " + Lang.inter[17].Value + " " : "") +
                (copper > 0 ? copper + " " + Lang.inter[18].Value + " " : ""));

            void Add(string name, string text)
            {
                var line = new TooltipLine("Terraria", name, text) { Role = "price" };
                lines.Add(line);
            }

            if (Main.npcShop > 0 && item.value >= 0)
            {
                var localPlayer = Main.LocalPlayer;
                if (localPlayer == null) return;
                localPlayer.GetItemExpectedPrice(item, out var selling, out var buying);
                var isCoin = item.type >= 71 && item.type <= 74;
                var price = isCoin ? 0 : item.isAShopItem || item.buyOnce ? buying : selling;

                if (item.shopSpecialCurrency != -1)
                {
                    var buffer = new Il2CppStringArray(4);
                    var count = 0;
                    CustomCurrencyManager.GetPriceText(item.shopSpecialCurrency, buffer, ref count, price);
                    if (count > 0)
                    {
                        var text = buffer[count - 1];
                        Add(TooltipNames.SpecialPrice, text.Substring(text.IndexOf(':') + 1).Replace("]", ""));                        ctx.PriceColor = Color.Multiply(CurrencyColor(item.shopSpecialCurrency), alpha);
                    }
                }
                else if (price > 0)
                {
                    long total;
                    if (item.buy)
                    {
                        total = price * item.stack;
                    }
                    else
                    {
                        var unit = Math.Max(price / 5, 1);
                        total = unit * item.stack;
                        var sellback = Main.shopSellbackHelper.GetAmount(item);
                        if (sellback > 0) total += (buying - unit) * Math.Min(sellback, item.stack);
                    }
                    total = Math.Max(total, 1);
                    var plat = (int)(total / 1000000); total -= plat * 1000000L;
                    var gold = (int)(total / 10000); total -= gold * 10000L;
                    var silver = (int)(total / 100); total -= silver * 100L;
                    var copper = (int)total;
                    Add(TooltipNames.Price, (item.buy ? Lang.tip[50].Value : Lang.tip[49].Value) + " " + coinText(plat, gold, silver, copper));
                    ctx.PriceColor = plat > 0 ? new Color(220, 220, 198) : gold > 0 ? new Color(224, 201, 92)
                        : silver > 0 ? new Color(181, 192, 193) : new Color(246, 138, 96);
                }
                else if (!isCoin && item.type != 3817)
                {
                    Add(TooltipNames.Price, Lang.tip[51].Value);
                    ctx.PriceColor = Color.Multiply(new Color(120, 120, 120), alpha);
                }
            }
        }

        private static Color CurrencyColor(int id)
        {
            var currency = CustomCurrencyManager._currencies[id];
            // 只有 CustomCurrencySingleCoin 带文本色；没有就用手机那套固定色
            var c = currency is CustomCurrencySingleCoin coin && coin.CurrencyTextColor.A > 0
                ? coin.CurrencyTextColor : new Color(246, 100, 120);
            return new Color(c.R, c.G, c.B);
        }

        // ---- ModifyTooltips：前缀额外行 + ModItem + GlobalItem，滤掉隐藏行 ----

        private static List<TooltipLine> Modify(Item item, List<TooltipLine> lines, int prefixEnd)
        {
            var modItem = ModContent.Find(ContentKind.Item, item.type) as ModItem;
            if (modItem != null && modItem.Plan != null && modItem.Plan.Has("ModifyTooltips"))
            {
                modItem.ModifyTooltips(item, lines);
            }
            foreach (var g in ModContent.GetContent<GlobalItem>())
            {
                if (g.Plan != null && g.Plan.Has("ModifyTooltips") && g.AppliesToEntity(item, false))
                    g.ModifyTooltips(item, lines);
            }
            return lines.FindAll(l => l.Visible);
        }

        // ---- 四个绘制钩子：ModItem 先，GlobalItem 后；Pre 返回 false 不画 ----

        private static bool PreDrawTooltip(Item item, List<TooltipLine> lines, ref float x, ref float y)
        {
            var ok = true;
            var modItem = ModContent.Find(ContentKind.Item, item.type) as ModItem;
            if (modItem != null && modItem.Plan != null && modItem.Plan.Has("PreDrawTooltip"))
                if (modItem.PreDrawTooltip(item, lines, ref x, ref y) == false) ok = false;
            foreach (var g in ModContent.GetContent<GlobalItem>())
            {
                if (g.Plan != null && g.Plan.Has("PreDrawTooltip") && g.AppliesToEntity(item, false))
                    if (g.PreDrawTooltip(item, lines, ref x, ref y) == false) ok = false;
            }
            return ok;
        }

        private static void PostDrawTooltip(Item item, DrawableTooltipLine[] lines)
        {
            var modItem = ModContent.Find(ContentKind.Item, item.type) as ModItem;
            if (modItem != null && modItem.Plan != null && modItem.Plan.Has("PostDrawTooltip"))
                modItem.PostDrawTooltip(item, lines);
            foreach (var g in ModContent.GetContent<GlobalItem>())
            {
                if (g.Plan != null && g.Plan.Has("PostDrawTooltip") && g.AppliesToEntity(item, false))
                    g.PostDrawTooltip(item, lines);
            }
        }

        private static bool PreDrawTooltipLine(Item item, DrawableTooltipLine line, ref float yOffset)
        {
            var ok = true;
            var modItem = ModContent.Find(ContentKind.Item, item.type) as ModItem;
            if (modItem != null && modItem.Plan != null && modItem.Plan.Has("PreDrawTooltipLine"))
                if (modItem.PreDrawTooltipLine(item, line, ref yOffset) == false) ok = false;
            foreach (var g in ModContent.GetContent<GlobalItem>())
            {
                if (g.Plan != null && g.Plan.Has("PreDrawTooltipLine") && g.AppliesToEntity(item, false))
                    if (g.PreDrawTooltipLine(item, line, ref yOffset) == false) ok = false;
            }
            return ok;
        }

        private static void PostDrawTooltipLine(Item item, DrawableTooltipLine line)
        {
            var modItem = ModContent.Find(ContentKind.Item, item.type) as ModItem;
            if (modItem != null && modItem.Plan != null && modItem.Plan.Has("PostDrawTooltipLine"))
                modItem.PostDrawTooltipLine(item, line);
            foreach (var g in ModContent.GetContent<GlobalItem>())
            {
                if (g.Plan != null && g.Plan.Has("PostDrawTooltipLine") && g.AppliesToEntity(item, false))
                    g.PostDrawTooltipLine(item, line);
            }
        }

        // ---- 颜色 ----

        private sealed class DrawContext
        {
            public float Alpha;
            public Color NameColor;
            public Color SetBonusColor;
            public Color PriceColor;
        }

        // 名字颜色按稀有度（mod 稀有度没有，先只做原版那 12 档 + 专家/大师）
        private static Color NameColor(int rare, byte diff, bool expert)
        {
            if (diff == 1) return new Color(Main.mcColor.R, Main.mcColor.G, Main.mcColor.B);
            if (diff == 2) return new Color(Main.hcColor.R, Main.hcColor.G, Main.hcColor.B);
            if (expert || rare == -12) return new Color(Main.DiscoR, Main.DiscoG, Main.DiscoB);

            switch (rare)
            {
                case -13: return new Color(255, (int)(Main.masterColor * 200), 0);
                case -11: return new Color(255, 175, 0);
                case -1: return new Color(130, 130, 130);
                case 1: return new Color(150, 150, 255);
                case 2: return new Color(150, 255, 150);
                case 3: return new Color(255, 200, 150);
                case 4: return new Color(255, 150, 150);
                case 5: return new Color(255, 150, 255);
                case 6: return new Color(210, 160, 255);
                case 7: return new Color(150, 255, 10);
                case 8: return new Color(255, 255, 10);
                case 9: return new Color(5, 200, 255);
                case 10: return new Color(255, 40, 100);
                case 11: return new Color(180, 40, 255);
            }
            return rare > 11 ? new Color(180, 40, 255) : Color.White;
        }

        // 行色按角色（不是下标：ModifyTooltips 加/删行不换别的行的颜色）
        private static Color LineColor(TooltipLine line, DrawContext ctx)
        {
            var mul = new Func<Color, Color>(c => Color.Multiply(c, ctx.Alpha));
            if (line.OverrideColor.HasValue) return mul(line.OverrideColor.Value);

            switch (line.Role)
            {
                case "name": return mul(ctx.NameColor);
                case "price": return ctx.PriceColor;
                case "setBonus": return mul(ctx.SetBonusColor);
                case "research": return mul(Colors.JourneyMode);
                case "shared": return mul(Main.OurFavoriteColor);
            }
            if (line.IsModifier || line.IsModifierBad)
                return mul(line.IsModifierBad ? new Color(190, 120, 120) : new Color(120, 190, 120));
            return mul(Color.White);
        }

        
        // ---- 颜色标签 [c/RRGGBB:文字] ----

        public static bool HasTags(string text) => text != null && text.IndexOf("[c/", StringComparison.Ordinal) >= 0;

        public static string StripTags(string text) => text == null ? "" : ColorTag.Replace(text, "$2");

        private static IEnumerable<Segment> Segments(string text)
        {
            var matches = ColorTag.Matches(text);
            if (matches.Count == 0)
            {
                yield return new Segment(text, null);
                yield break;
            }
            var at = 0;
            foreach (Match m in matches)
            {
                if (m.Index > at) yield return new Segment(text.Substring(at, m.Index - at), null);
                var v = Convert.ToInt32(m.Groups[1].Value, 16);
                yield return new Segment(m.Groups[2].Value, new Color255((v >> 16) & 255, (v >> 8) & 255, v & 255));
                at = m.Index + m.Length;
            }
            if (at < text.Length) yield return new Segment(text.Substring(at), null);
        }

        private sealed class Segment
        {
            public readonly string Text;
            public readonly Color255? Rgb;
            public Segment(string text, Color255? rgb) { Text = text ?? ""; Rgb = rgb; }
        }

        private readonly struct Color255
        {
            public readonly byte R, G, B;
            public Color255(int r, int g, int b) { R = (byte)r; G = (byte)g; B = (byte)b; }
        }
    }
}
