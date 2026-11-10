using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI.BigProgressBar;

namespace Pylon.ModLoader;

/// <summary>
/// Boss 血条管线：拦在 <c>BigProgressBarSystem</c> 上——
/// <c>Update</c> 之后读 <c>_info.npcIndexToAimAt</c>（精确的目标 NPC 序号），
/// <c>Draw</c> 里换成作者的条。见 docs/pipeline/content-bossbar.md。
/// </summary>
internal sealed class BossBarPipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static BossBarPipeline Instance { get; } = new();

    public string Name => "bossbar";

    private static IModLog Log => LoaderHost.Current.Log;

    /// <summary>当前这条 mod 血条（null = 这一帧画原版）。</summary>
    private static ModBossBar? Bar;

    /// <summary>当前这条血条指向的 NPC 序号（-1 = 没有）。</summary>
    private static int Index = -1;

    /// <summary>原版没血条时，隔多少帧扫一次附近的 mod Boss。</summary>
    private const int ScanEvery = 30;

    private static int Tracked = -1;
    private static int ScanAge;

    public bool Install()
    {
        On.Terraria.GameContent.UI.BigProgressBar.BigProgressBarSystem.Update += Update;
        On.Terraria.GameContent.UI.BigProgressBar.BigProgressBarSystem.Draw += DrawSystem;

        // 原版条不经过 DrawBar；GlobalBossBar 要在 GUIBossBar.Draw 这一层也跑一遍。
        On.GUIBossBar.Draw += GuiDraw;

        // 条身皮肤：条身的底色/贴图与生命文字都走下面这一族 GUI 绘制，接管它们才能让
        // BossBarDrawParams 的 BarTexture / BarColor / ShowText 真的生效（见 §2.4 的修正）。
        On.GUIPanel.DrawBacking += Backing;
        On.GUIPanel.Region += CapturedRegion;
        On.GUIString.Draw_String_Layout_string += Text;
        On.GUIString.Draw_String_Layout_string_Color_bool += TextColor;
        On.GUIString.Draw_String_Layout_StringBuilder += TextBuilder;
        On.GUIString.Draw_String_Layout_StringBuilder_Color += TextBuilderColor;
        On.GUIString.DrawWithShadow_String_Layout_string += ShadowText;
        On.GUIString.DrawWithShadow_String_Layout_string_Vector2 += ShadowTextVector;
        On.GUIString.DrawWithShadow_String_Layout_string_Vector2_Color += ShadowTextVectorColor;
        On.GUIString.DrawWithShadow_String_Layout_string_bool_Color += ShadowTextBoolColor;

        Log.Info("[bossbar] 已挂（BigProgressBarSystem.Update 抓目标 / Draw 换条）");
        return true;
    }

    public void Refresh() => BossBarTable.Rebuild();

    /// <summary>安装期一次：血条的静态默认值。</summary>
    /// <summary>横切血条（游戏条 + mod 条都走）。安装期快照，分派时不再分配。</summary>
    private static GlobalBossBar[] _globals = [];

    /// <summary>正在画 mod 条：此时 GUIBossBar.Draw 里不再重复分派 GlobalBossBar。</summary>
    private static bool _inModBarDraw;

    /// <summary>
    ///    正在画的这条 mod 条的作者参数（null = 没在画 mod 条）。
    ///    条身贴图/底色/生命文字的接管都只在这期间生效，原版条一个像素都不动。
    /// </summary>
    private static BossBarDrawParams? _skin;

    internal void Build()
    {
        foreach (var bar in ModContent.GetContent<ModBossBar>())
        {
            bar.Registered = true;
            bar.SetStaticDefaults();
        }

        _globals = [.. ModContent.GetContent<GlobalBossBar>()];
        BossBarTable.Rebuild();

        if (BossBarTable.Count > 0) Log.Info($"[bossbar] 安装：{BossBarTable.Count} 条血条");
    }

    // ===================== 抓目标 =====================

    private static void Update(On.Terraria.GameContent.UI.BigProgressBar.BigProgressBarSystem.orig_Update orig,
                               BigProgressBarSystem self)
    {
        orig(self);

        Bar = null;
        Index = -1;

        if (BossBarTable.Count == 0) return;

        try
        {
            var current = self._currentBar;
            if (current is not null)
            {
                // 原版已经盯上了一个：看它的 _info 里是哪个 NPC。这就是精确序号。
                Tracked = -1;

                var at = self._info.npcIndexToAimAt;
                var own = Of(at);

                if (own is not null && !Collect(own, at)) self._currentBar = null;

                return;
            }

            if (Tracked >= 0)
            {
                var own = Of(Tracked);
                if (own is not null && Collect(own, Tracked)) return;

                Tracked = -1;
            }

            if (++ScanAge < ScanEvery) return;
            ScanAge = 0;

            var found = Find();
            if (found < 0) return;

            if (Of(found) is { } candidate && Collect(candidate, found)) Tracked = found;
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[bossbar] 抓目标异常：{Safe.Describe(e)}");
        }
    }

    /// <summary>第 <paramref name="at"/> 号 NPC 是不是 mod Boss 且声明了血条。</summary>
    private static ModBossBar? Of(int at)
    {
        if ((uint)at >= 200u) return null;

        var npcs = Main.npc;
        if (npcs is null || at >= npcs.Length) return null;

        var npc = npcs[at];
        if (npc is null || !npc.active) return null;
        if (ModContent.Find(ContentKind.Npc, npc.type) is not ModNpc modNpc) return null;

        return modNpc.BossBar;
    }

    /// <summary>作者点头就用这条；返回 false = 这个目标不画。</summary>
    private static bool Collect(ModBossBar candidate, int at)
    {
        var info = new BossBarInfo { npcIndexToAimAt = at };

        // 合同里没有单独的 Validate：ModifyInfo 返回 false 就是不画。
        Bar = candidate;
        Index = at;
        return true;
    }

    /// <summary>原版没在盯任何 Boss 时，就近找一个声明了血条的 mod NPC。</summary>
    private static int Find()
    {
        var npcs = Main.npc;
        if (npcs is null) return -1;

        var me = Main.LocalPlayer;
        if (me is null) return -1;

        var center = me.Center;
        var screen = Main.screenPosition;
        var left = screen.X - 5000f;
        var right = screen.X + Main.screenWidth + 5000f;
        var top = screen.Y - 5000f;
        var bottom = screen.Y + Main.screenHeight + 5000f;

        var best = -1;
        var bestDistance = float.MaxValue;

        var count = Math.Min(npcs.Length, 200);
        for (var index = 0; index < count; index++)
        {
            var npc = npcs[index];
            if (npc is null || !npc.active) continue;

            var position = npc.position;
            if (position.X + npc.width < left || position.X > right) continue;
            if (position.Y + npc.height < top || position.Y > bottom) continue;

            if (ModContent.Find(ContentKind.Npc, npc.type) is not ModNpc modNpc || modNpc.BossBar is null) continue;

            var dx = npc.Center.X - center.X;
            var dy = npc.Center.Y - center.Y;
            var distance = dx * dx + dy * dy;
            if (distance >= bestDistance) continue;

            best = index;
            bestDistance = distance;
        }

        return best;
    }

    // ===================== 画 =====================

    private static void DrawSystem(On.Terraria.GameContent.UI.BigProgressBar.BigProgressBarSystem.orig_Draw orig,
                                   BigProgressBarSystem self, SpriteBatch spriteBatch)
    {
        if (Bar is not { } bar || (uint)Index >= 200u || Main.npc is null || Index >= Main.npc.Length)
        {
            orig(self, spriteBatch);
            return;
        }

        var npc = Main.npc[Index];
        if (npc is null)
        {
            orig(self, spriteBatch);
            return;
        }

        try
        {
            DrawBar(bar, npc, spriteBatch);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[bossbar] {bar.Name} 绘制异常：{Safe.Describe(e)}");
        }
    }

    private static void DrawBar(ModBossBar bar, NPC npc, SpriteBatch spriteBatch)
    {
        var gui = GUIInstance.Active is null ? null : GUIInstance.Active.GUIBossBar;
        if (gui is null) return;

        var life = bar.Life;
        var lifeMax = bar.LifeMax;
        var shield = bar.Shield;
        var shieldMax = bar.ShieldMax;

        // 先把这一帧的生命收进作者对象：GetIconTexture / ModifyInfo 读到的才是当前帧。
        try
        {
            bar.Collected(npc.life, npc.lifeMax, shield, shieldMax);
            life = bar.Life;
            lifeMax = bar.LifeMax;
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[bossbar] {bar.Name}.Collected 异常：{Safe.Describe(e)}");
        }

        var info = new BossBarInfo { npcIndexToAimAt = npc.whoAmI };
        bool? verdict = null;
        try
        {
            verdict = bar.ModifyInfo(info, ref life, ref lifeMax, ref shield, ref shieldMax);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[bossbar] {bar.Name}.ModifyInfo 异常：{Safe.Describe(e)}");
        }

        if (verdict == false) return;

        Rectangle? frame = null;
        Texture2D? icon = null;
        try
        {
            if (bar.GetIconTexture(ref frame) is { } asset) icon = asset.Value;
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[bossbar] {bar.Name}.GetIconTexture 异常：{Safe.Describe(e)}");
        }

        var heads = TextureAssets.NpcHead;
        if (icon is null && heads is not null && heads.Length > 0) icon = heads[0]?.Value;
        if (icon is null) return;

        var draw = new BossBarDrawParams
        {
            IconTexture = icon,
            IconFrame = frame ?? new Rectangle(0, 0, icon.Width, icon.Height),
            Life = life,
            LifeMax = lifeMax,
            Shield = shield,
            ShieldMax = shieldMax,
            BarTexture = null!,
            BarCenter = Center(),
            BarColor = Color.White,
            IconColor = Color.White,
            IconScale = 1f,
            ShowText = true,
            TextOffset = Vector2.Zero,
        };

        // 横切类先跑（ModBossBar 的 PreDraw 在它们之后）；任何一个返回 false 都跳过条身。
        var keep = true;
        foreach (var global in _globals)
        {
            try
            {
                if (!global.PreDraw(spriteBatch, npc, draw)) keep = false;
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[bossbar] {global.Name}.PreDraw 异常：{Safe.Describe(e)}");
            }
        }

        if (keep)
        {
            try
            {
                keep = bar.PreDraw(spriteBatch, npc, draw);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[bossbar] {bar.Name}.PreDraw 异常：{Safe.Describe(e)}");
            }
        }

        if (keep)
        {
            var shielded = draw.ShieldMax > 0f;
            _inModBarDraw = true;
            _skin = draw;
            try
            {
                gui.Draw(draw.Life, draw.LifeMax, draw.IconTexture, draw.IconFrame,
                         shielded ? draw.Shield : -1f, shielded ? draw.ShieldMax : 1f);
            }
            finally
            {
                _skin = null;
                _inModBarDraw = false;
            }
        }

        try
        {
            bar.PostDraw(spriteBatch, npc, draw);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[bossbar] {bar.Name}.PostDraw 异常：{Safe.Describe(e)}");
        }

        foreach (var global in _globals)
        {
            try { global.PostDraw(spriteBatch, npc, draw); }
            catch (Exception e) { ContentErrors.Report($"[bossbar] {global.Name}.PostDraw 异常：{Safe.Describe(e)}"); }
        }
    }

    /// <summary>
    /// 原版条也过一遍 <see cref="GlobalBossBar"/>：<c>GUIBossBar.Draw</c> 是两种条共用的最后一步。
    /// mod 条在 <see cref="DrawBar"/> 里已经分派过，用 <c>_inModBarDraw</c> 防止重复。
    /// </summary>
    private static void GuiDraw(On.GUIBossBar.orig_Draw orig, GUIBossBar self, float life, float lifeMax,
                                Texture2D icon, Rectangle frame, float shield, float shieldMax)
    {
        if (_inModBarDraw || _globals.Length == 0)
        {
            orig(self, life, lifeMax, icon, frame, shield, shieldMax);
            return;
        }

        var at = -1;
        try
        {
            var system = Main.BigBossProgressBar;
            if (system is not null) at = system._info.npcIndexToAimAt;
        }
        catch { /* 读不到就按原版画 */ }

        if ((uint)at >= 200u || Main.npc is null || at >= Main.npc.Length)
        {
            orig(self, life, lifeMax, icon, frame, shield, shieldMax);
            return;
        }

        var npc = Main.npc[at];
        if (npc is null)
        {
            orig(self, life, lifeMax, icon, frame, shield, shieldMax);
            return;
        }

        var draw = new BossBarDrawParams
        {
            IconTexture = icon,
            IconFrame = frame,
            Life = life,
            LifeMax = lifeMax,
            Shield = shield,
            ShieldMax = shieldMax,
            BarTexture = null!,
            BarCenter = Center(),
            BarColor = Color.White,
            IconColor = Color.White,
            IconScale = 1f,
            ShowText = true,
            TextOffset = Vector2.Zero,
        };

        var keep = true;
        foreach (var global in _globals)
        {
            try
            {
                if (!global.PreDraw(Main.spriteBatch, npc, draw)) keep = false;
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[bossbar] {global.Name}.PreDraw 异常：{Safe.Describe(e)}");
            }
        }

        if (keep)
        {
            var shielded = draw.ShieldMax > 0f;
            orig(self, draw.Life, draw.LifeMax, draw.IconTexture, draw.IconFrame,
                 shielded ? draw.Shield : -1f, shielded ? draw.ShieldMax : 1f);
        }

        foreach (var global in _globals)
        {
            try { global.PostDraw(Main.spriteBatch, npc, draw); }
            catch (Exception e) { ContentErrors.Report($"[bossbar] {global.Name}.PostDraw 异常：{Safe.Describe(e)}"); }
        }
    }

    // ===================== 条身皮肤 =====================

    /// <summary>
    ///     条身底色与贴图。<c>GUIBossBar.Draw</c> 内部的条身是 <c>GUIPanel.DrawBacking</c> 画的，
    ///     所以接管这一笔就能让 <see cref="BossBarDrawParams.BarTexture"/> / <see cref="BossBarDrawParams.BarColor"/> 生效。
    ///     只在画 mod 条期间动手，作者不设（Texture 为 null、Color 为白）就一个像素都不改。
    /// </summary>
    private static void Backing(On.GUIPanel.orig_DrawBacking orig, Vector2 position, Vector2 size,
                                Texture2D texture, Color color, Il2CppSystem.Nullable<Rectangle> frame)
    {
        if (_skin is { } skin)
        {
            if (skin.BarTexture is not null && !ReferenceEquals(skin.BarTexture, texture)) texture = skin.BarTexture;
            if (skin.BarColor != Color.White) color = skin.BarColor;
        }

        orig(position, size, texture, color, frame);
    }

    private static Vector2 Center()
    {
        if (_panel is null) return new Vector2(Main.screenWidth * 0.5f, Main.screenHeight - 50f);

        try
        {
            var region = GUIPanel.Region(_panel);
            return new Vector2(region.X + region.Width / 2f, region.Y + region.Height / 2f);
        }
        catch
        {
            return new Vector2(Main.screenWidth * 0.5f, Main.screenHeight - 50f);
        }
    }

    private static Panel_Layout? _panel;

    /// <summary>画 mod 条期间记下条身面板布局:Center() 才能给出真实中心。</summary>
    private static Rectangle CapturedRegion(On.GUIPanel.orig_Region orig, Panel_Layout layout)
    {
        if (_skin is not null && _panel is null) _panel = layout;
        return orig(layout);
    }

    /// <summary>画 mod 条期间作者要求不显文字：这一族 GUI 文字全部跳过。</summary>
    private static bool NoText => _skin is { ShowText: false };

    private static void Text(On.GUIString.orig_Draw_String_Layout_string orig, String_Layout layout, string text)
    {
        if (NoText) return;
        orig(layout, text);
    }

    private static void TextColor(On.GUIString.orig_Draw_String_Layout_string_Color_bool orig, String_Layout layout,
                                  string text, Color color, bool offset)
    {
        if (NoText) return;
        orig(layout, text, color, offset);
    }

    private static void TextBuilder(On.GUIString.orig_Draw_String_Layout_StringBuilder orig, String_Layout layout,
                                    Il2CppSystem.Text.StringBuilder text)
    {
        if (NoText) return;
        orig(layout, text);
    }

    private static void TextBuilderColor(On.GUIString.orig_Draw_String_Layout_StringBuilder_Color orig,
                                         String_Layout layout, Il2CppSystem.Text.StringBuilder text, Color color)
    {
        if (NoText) return;
        orig(layout, text, color);
    }

    private static void ShadowText(On.GUIString.orig_DrawWithShadow_String_Layout_string orig, String_Layout layout,
                                   string text)
    {
        if (NoText) return;
        orig(layout, text);
    }

    private static void ShadowTextVector(On.GUIString.orig_DrawWithShadow_String_Layout_string_Vector2 orig,
                                         String_Layout layout, string text, Vector2 offset)
    {
        if (NoText) return;
        orig(layout, text, offset);
    }

    private static void ShadowTextVectorColor(On.GUIString.orig_DrawWithShadow_String_Layout_string_Vector2_Color orig,
                                              String_Layout layout, string text, Vector2 offset, Color color)
    {
        if (NoText) return;
        orig(layout, text, offset, color);
    }

    private static void ShadowTextBoolColor(On.GUIString.orig_DrawWithShadow_String_Layout_string_bool_Color orig,
                                            String_Layout layout, string text, bool offset, Color color)
    {
        if (NoText) return;
        orig(layout, text, offset, color);
    }
}
