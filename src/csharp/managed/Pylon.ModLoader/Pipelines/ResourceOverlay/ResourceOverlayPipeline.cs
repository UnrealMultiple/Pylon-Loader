using Terraria;
using Terraria.GameContent.UI.ResourceSets;

namespace Pylon.ModLoader;

/// <summary>
/// 资源覆盖层管线：生命条 / 魔力条画的前后各派一次作者回调。其它资源条样式与悬停提示本版不接，
/// 见 docs/pipeline/content-resourceoverlay.md。
/// </summary>
internal sealed class ResourceOverlayPipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static ResourceOverlayPipeline Instance { get; } = new();

    public string Name => "resourceoverlay";

    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        On.Terraria.GameContent.UI.ResourceSets.ClassicPlayerResourcesDisplaySet.DrawLife += DrawLife;
        On.Terraria.GameContent.UI.ResourceSets.ClassicPlayerResourcesDisplaySet.DrawMana += DrawMana;
        On.Terraria.GameContent.UI.ResourceSets.FancyClassicPlayerResourcesDisplaySet.DrawLifeBar += DrawFancyLife;
        On.Terraria.GameContent.UI.ResourceSets.HorizontalBarsPlayerResourcesDisplaySet.Draw += DrawHorizontalBars;
        On.Terraria.GameContent.UI.ResourceSets.FancyClassicPlayerResourcesDisplaySet.DrawManaBar += DrawFancyMana;
        On.Terraria.GameContent.UI.ResourceSets.CommonResourceBarMethods.DrawLifeMouseOver += DrawLifeMouseOver;
        On.Terraria.GameContent.UI.ResourceSets.CommonResourceBarMethods.DrawManaMouseOver += DrawManaMouseOver;
        On.GUIHearts.HeartDraw += HeartDraw;
        On.GUIMana.StarDraw += StarDraw;

        Log.Info("[resourceoverlay] 已挂（生命/魔力条的绘制与悬停提示前后 + 单颗心/星前后）");
        return true;
    }

    public void Refresh() => ResourceOverlayTable.Rebuild();

    /// <summary>安装期一次：收表 + 静态默认值。</summary>
    internal void Build()
    {
        ResourceOverlayTable.Rebuild();

        foreach (var overlay in ResourceOverlayTable.All) overlay.SetStaticDefaults();

        if (ResourceOverlayTable.Count > 0) Log.Info($"[resourceoverlay] 安装：{ResourceOverlayTable.Count} 个覆盖层");
    }

    // ===================== 绘制 =====================

    private static void DrawLife(On.Terraria.GameContent.UI.ResourceSets.ClassicPlayerResourcesDisplaySet.orig_DrawLife orig,
                                 ClassicPlayerResourcesDisplaySet self)
    {
        if (ResourceOverlayTable.Count == 0)
        {
            orig(self);
            return;
        }

        Dispatch(true, false);
        orig(self);
        Dispatch(false, false);
    }

    private static void DrawMana(On.Terraria.GameContent.UI.ResourceSets.ClassicPlayerResourcesDisplaySet.orig_DrawMana orig,
                                 ClassicPlayerResourcesDisplaySet self)
    {
        if (ResourceOverlayTable.Count == 0)
        {
            orig(self);
            return;
        }

        Dispatch(true, true);
        orig(self);
        Dispatch(false, true);
    }

    // FancyClassic 的条：与 Classic 同形（整条一次，spriteBatch 前后派）。
    /// <summary>
    /// 横条样式（HorizontalBars）把生命与法力画在同一块里，没有"生命条一次、法力条一次"的分开入口；
    /// 所以在这里按顺序派四次：生命前、法力前 → 原版整块 → 法力后、生命后。作者拿到的是整块的时机，
    /// 比心/星的粒度粗，但 API 语义（前后各一次）不变。
    /// </summary>
    private static void DrawHorizontalBars(
        On.Terraria.GameContent.UI.ResourceSets.HorizontalBarsPlayerResourcesDisplaySet.orig_Draw orig,
        Terraria.GameContent.UI.ResourceSets.HorizontalBarsPlayerResourcesDisplaySet self)
    {
        if (ResourceOverlayTable.Count == 0)
        {
            orig(self);
            return;
        }

        var batch = Main.spriteBatch;
        DispatchAll(true, false, batch);
        DispatchAll(true, true, batch);

        orig(self);

        DispatchAll(false, true, batch);
        DispatchAll(false, false, batch);
    }

    private static void DrawFancyLife(
        On.Terraria.GameContent.UI.ResourceSets.FancyClassicPlayerResourcesDisplaySet.orig_DrawLifeBar orig,
        Terraria.GameContent.UI.ResourceSets.FancyClassicPlayerResourcesDisplaySet self,
        Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch spriteBatch)
    {
        DispatchAll(true, false, spriteBatch);
        orig(self, spriteBatch);
        DispatchAll(false, false, spriteBatch);
    }

    private static void DrawFancyMana(
        On.Terraria.GameContent.UI.ResourceSets.FancyClassicPlayerResourcesDisplaySet.orig_DrawManaBar orig,
        Terraria.GameContent.UI.ResourceSets.FancyClassicPlayerResourcesDisplaySet self,
        Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch spriteBatch)
    {
        DispatchAll(true, true, spriteBatch);
        orig(self, spriteBatch);
        DispatchAll(false, true, spriteBatch);
    }

    /// <summary>对全部覆盖层派一次（条粒度）；Classic 与 FancyClassic 共用。</summary>
    private static void DispatchAll(bool before, bool mana, Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch spriteBatch)
    {
        if (ResourceOverlayTable.Count == 0) return;

        for (var index = 0; index < ResourceOverlayTable.Count; index++)
        {
            var overlay = ResourceOverlayTable.At(index);

            try
            {
                if (before)
                {
                    if (mana) overlay.PreDrawMana(spriteBatch);
                    else overlay.PreDrawLife(spriteBatch);
                }
                else
                {
                    if (mana) overlay.PostDrawMana(spriteBatch);
                    else overlay.PostDrawLife(spriteBatch);
                }
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[resourceoverlay] {overlay.Name}（条粒度）异常：{Safe.Describe(e)}");
            }
        }
    }

    private static void DrawLifeMouseOver(On.Terraria.GameContent.UI.ResourceSets.CommonResourceBarMethods.orig_DrawLifeMouseOver orig)
    {
        Dispatch(true, false, true);
        orig();
        Dispatch(false, false, true);
    }

    private static void DrawManaMouseOver(On.Terraria.GameContent.UI.ResourceSets.CommonResourceBarMethods.orig_DrawManaMouseOver orig)
    {
        Dispatch(true, true, true);
        orig();
        Dispatch(false, true, true);
    }

    /// <summary>单颗心：作者说"这颗不画"就跳过 orig，但 Post 照样派（作者可能只想换画法）。</summary>
    private static void HeartDraw(On.GUIHearts.orig_HeartDraw orig, GUIHearts self, ItemGrid_Layout gridLayout,
                                 int index, Il2CppMicrosoft.Xna.Framework.Vector2 position, float scale)
    {
        if (!ResourceOverlayTable.AnyResourceDraw())
        {
            orig(self, gridLayout, index, position, scale);
            return;
        }

        var go = DispatchResource(true, index, position, scale);
        if (go) orig(self, gridLayout, index, position, scale);
        DispatchResource(false, index, position, scale);
    }

    /// <summary>单颗魔力星，与心同一套语义。</summary>
    private static void StarDraw(On.GUIMana.orig_StarDraw orig, GUIMana self, ItemGrid_Layout gridLayout, int index,
                                Il2CppMicrosoft.Xna.Framework.Vector2 position, float scale)
    {
        if (!ResourceOverlayTable.AnyResourceDraw())
        {
            orig(self, gridLayout, index, position, scale);
            return;
        }

        var go = DispatchResource(true, index, position, scale);
        if (go) orig(self, gridLayout, index, position, scale);
        DispatchResource(false, index, position, scale);
    }

    /// <summary>单颗绘制的前后派发；返回 false = 至少一位作者要求这颗不画。</summary>
    private static bool DispatchResource(bool before, int index, Il2CppMicrosoft.Xna.Framework.Vector2 position,
                                        float scale)
    {
        var go = true;

        for (var slot = 0; slot < ResourceOverlayTable.Count; slot++)
        {
            var mask = ResourceOverlayTable.MaskAt(slot);
            var overlay = ResourceOverlayTable.At(slot);

            if (before)
            {
                if (!mask.Has(HookId.PreDrawResource)) continue;

                try
                {
                    if (!overlay.PreDrawResource(index, position, scale)) go = false;
                }
                catch (Exception e)
                {
                    ContentErrors.Report($"[resourceoverlay] {overlay.Name}.PreDrawResource 异常：{Safe.Describe(e)}");
                }
            }
            else
            {
                if (!mask.Has(HookId.PostDrawResource)) continue;

                try
                {
                    overlay.PostDrawResource(index, position, scale);
                }
                catch (Exception e)
                {
                    ContentErrors.Report($"[resourceoverlay] {overlay.Name}.PostDrawResource 异常：{Safe.Describe(e)}");
                }
            }
        }

        return go;
    }

    /// <summary>逐条派；每条各自 try/catch，一个覆盖层抛异常不该让条消失。</summary>
    private static void Dispatch(bool before, bool mana, bool hover = false)
    {
        var spriteBatch = Main.spriteBatch;
        if (spriteBatch is null) return;

        for (var index = 0; index < ResourceOverlayTable.Count; index++)
        {
            var overlay = ResourceOverlayTable.At(index);

            try
            {
                if (hover)
                {
                    if (mana)
                    {
                        if (before) overlay.PreDrawManaMouseOver(spriteBatch);
                        else overlay.PostDrawManaMouseOver(spriteBatch);
                    }
                    else
                    {
                        if (before) overlay.PreDrawLifeMouseOver(spriteBatch);
                        else overlay.PostDrawLifeMouseOver(spriteBatch);
                    }
                }
                else if (mana)
                {
                    if (before) overlay.PreDrawMana(spriteBatch);
                    else overlay.PostDrawMana(spriteBatch);
                }
                else
                {
                    if (before) overlay.PreDrawLife(spriteBatch);
                    else overlay.PostDrawLife(spriteBatch);
                }
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[resourceoverlay] {overlay.Name} 异常：{Safe.Describe(e)}");
            }
        }
    }
}
