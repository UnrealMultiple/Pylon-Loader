using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI.BigProgressBar;

namespace Pylon.ModLoader;

/// <summary>Boss 血条管线：拦在 BigProgressBarSystem 上抓目标与换条（NPC 与血条的绑定在 NPC.BossBar 上），GUI 层接管条身皮肤。</summary>
internal sealed class BossBarPipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static BossBarPipeline Instance { get; } = new();

    public string Name => "bossbar";

    private static IModLog Log => LoaderHost.Current.Log;

    private static readonly object Gate = new();

    private static readonly Dictionary<nint, (int Type, ModBossBar Bar)> Assigned = [];

    private static ModBossBar? Bar;

    private static int Index = -1;

    private const int ScanEvery = 30;

    private static int Tracked = -1;
    private static int ScanAge;

    internal static ModBossBar? Get(NPC npc)
    {
        if (npc is null || npc.Pointer == nint.Zero) return null;
        lock (Gate)
            return Assigned.TryGetValue(npc.Pointer, out var entry) && entry.Type == npc.type ? entry.Bar : null;
    }

    internal static void Set(NPC npc, ModBossBar? bar)
    {
        if (npc is null || npc.Pointer == nint.Zero) return;
        lock (Gate)
        {
            if (bar is null) Assigned.Remove(npc.Pointer);
            else Assigned[npc.Pointer] = (npc.type, bar);
        }
    }

    private static bool AnyAssigned
    {
        get { lock (Gate) return Assigned.Count > 0; }
    }

    public bool Install()
    {
        On.Terraria.GameContent.UI.BigProgressBar.BigProgressBarSystem.Update += Update;
        On.Terraria.GameContent.UI.BigProgressBar.BigProgressBarSystem.Draw += DrawSystem;

        On.GUIBossBar.Draw += GuiDraw;
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

    private static GlobalBossBar[] _globals = [];

    private static bool _inModBarDraw;

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
                Tracked = -1;

                var at = self._info.npcIndexToAimAt;
                var own = Of(at);

                if (own is not null && !Collect(own, at, self._info.showText)) self._currentBar = null;

                return;
            }

            if (Tracked >= 0)
            {
                var own = Of(Tracked);
                if (own is not null && Collect(own, Tracked, self._info.showText)) return;

                Tracked = -1;
            }

            if (++ScanAge < ScanEvery || !AnyAssigned) return;
            ScanAge = 0;

            var found = Find();
            if (found < 0) return;

            if (Of(found) is { } candidate && Collect(candidate, found, self._info.showText)) Tracked = found;
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[bossbar] 抓目标异常：{Safe.Describe(e)}");
        }
    }

    private static ModBossBar? Of(int at)
    {
        if ((uint)at >= 200u) return null;

        var npcs = Main.npc;
        if (npcs is null || at >= npcs.Length) return null;

        var npc = npcs[at];
        return npc is not null && npc.active ? Get(npc) : null;
    }

    private static bool Collect(ModBossBar candidate, int at, bool showText)
    {
        var info = new BossBarInfo { npcIndexToAimAt = at, showText = showText };

        bool valid;
        try
        {
            valid = candidate.ValidateAndCollectNecessaryInfo(ref info);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[bossbar] {candidate.Name}.ModifyInfo 异常：{Safe.Describe(e)}");
            valid = false;
        }

        if (!valid) return false;

        Bar = candidate;
        Index = info.npcIndexToAimAt;
        return true;
    }

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

            if (Get(npc) is null) continue;

            var dx = npc.Center.X - center.X;
            var dy = npc.Center.Y - center.Y;
            var distance = dx * dx + dy * dy;
            if (distance >= bestDistance) continue;

            best = index;
            bestDistance = distance;
        }

        return best;
    }

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
            Life = bar.Life,
            LifeMax = bar.LifeMax,
            Shield = bar.Shield,
            ShieldMax = bar.ShieldMax,
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
                if (!global.PreDraw(spriteBatch, npc, ref draw)) keep = false;
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
                keep = bar.PreDraw(spriteBatch, npc, ref draw);
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
        catch { }

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
                if (!global.PreDraw(Main.spriteBatch, npc, ref draw)) keep = false;
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

    private static Rectangle CapturedRegion(On.GUIPanel.orig_Region orig, Panel_Layout layout)
    {
        if (_skin is not null && _panel is null) _panel = layout;
        return orig(layout);
    }

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
