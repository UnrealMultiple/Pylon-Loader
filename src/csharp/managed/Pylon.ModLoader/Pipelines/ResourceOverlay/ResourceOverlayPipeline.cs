using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI.ResourceSets;

namespace Pylon.ModLoader;

/// <summary>
/// 资源覆盖层管线：单颗心/星绘制前后给 <see cref="ResourceOverlayDrawContext"/>，整条生命/魔力条前后给快照。
/// <c>HeartDraw</c>/<c>StarDraw</c>；<c>PreDrawResourceDisplay</c> 的 drawText 控制生命数值画不画。
/// </summary>
internal sealed class ResourceOverlayPipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static ResourceOverlayPipeline Instance { get; } = new();

    public string Name => "resourceoverlay";

    private static IModLog Log => LoaderHost.Current.Log;

    private static PlayerStatsSnapshot _snapshot;
    private static bool _haveSnapshot;
    private static bool _drawLifeText = true;
    private static ResourceOverlayDrawContext _current;
    private static int _resourceNumber;

    public bool Install()
    {
        // 与 menu 一样：Install 早于 Build/收表，所以无条件挂；分派时按位集跳过没覆写的覆盖层。
        On.GUIHearts.HeartDraw += HeartDraw;
        On.GUIMana.StarDraw += StarDraw;
        On.GUIHearts.Draw += HeartsDraw;
        On.GUIMana.Draw += ManaDraw;
        On.GUIHearts.DrawLifeString += DrawLifeString;

        SpriteBatchCapture.Install();

        Log.Info("[resourceoverlay] 已挂（生命/魔力条前后 + 单颗心/星前后 + 生命数值）");
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

    // ===================== 整条 =====================

    private static IPlayerResourcesDisplaySet DisplaySet => Main.ActivePlayerResourcesSet;

    private static void HeartsDraw(On.GUIHearts.orig_Draw orig, GUIHearts self) => Display(true, () => orig(self));

    private static void ManaDraw(On.GUIMana.orig_Draw orig, GUIMana self) => Display(false, () => orig(self));

    /// <summary>生命数值：<c>PreDrawResourceDisplay</c> 的 drawText 为 false 时这一帧不画。</summary>
    private static void DrawLifeString(On.GUIHearts.orig_DrawLifeString orig, GUIHearts self)
    {
        if (_drawLifeText) orig(self);
    }

    private static void Display(bool life, Action draw)
    {
        if (!ResourceOverlayTable.AnyDisplay())
        {
            draw();
            return;
        }

        _snapshot = new PlayerStatsSnapshot(Main.LocalPlayer);
        _haveSnapshot = true;
        var textColor = Color.White;
        var drawText = true;
        var go = true;

        DispatchDisplay(true, life, ref textColor, ref drawText, ref go);

        if (life) _drawLifeText = drawText;
        try
        {
            if (go) draw();
        }
        finally
        {
            _drawLifeText = true;
        }

        DispatchDisplay(false, life, ref textColor, ref drawText, ref go);
    }

    private static void DispatchDisplay(bool before, bool life, ref Color textColor, ref bool drawText, ref bool go)
    {
        for (var slot = 0; slot < ResourceOverlayTable.Count; slot++)
        {
            var mask = ResourceOverlayTable.MaskAt(slot);
            if (before ? !mask.Has(HookId.PreDrawResourceDisplay) : !mask.Has(HookId.PostDrawResourceDisplay)) continue;

            var overlay = ResourceOverlayTable.At(slot);
            try
            {
                if (before)
                {
                    if (!overlay.PreDrawResourceDisplay(_snapshot, DisplaySet, life, ref textColor, out var overlayDraw))
                        go = false;
                    drawText &= overlayDraw;
                }
                else
                {
                    overlay.PostDrawResourceDisplay(_snapshot!, DisplaySet, life, textColor, drawText);
                }
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[resourceoverlay] {overlay.Name} 异常：{Safe.Describe(e)}");
            }
        }
    }

    // ===================== 单颗 =====================

    private static void HeartDraw(On.GUIHearts.orig_HeartDraw orig, GUIHearts self, ItemGrid_Layout gridLayout,
                                  int index, Vector2 position, float scale)
    {
        if (!ResourceOverlayTable.AnyResourceDraw())
        {
            orig(self, gridLayout, index, position, scale);
            return;
        }

        Around(index, () => orig(self, gridLayout, index, position, scale));
    }

    private static void StarDraw(On.GUIMana.orig_StarDraw orig, GUIMana self, ItemGrid_Layout gridLayout,
                                 int index, Vector2 position, float scale)
    {
        if (!ResourceOverlayTable.AnyResourceDraw())
        {
            orig(self, gridLayout, index, position, scale);
            return;
        }

        Around(index, () => orig(self, gridLayout, index, position, scale));
    }

    /// <summary>
    /// 经典 = Heart/Mana，FancyClassic = Heart_Fill/Star_Fill，横条 = HP_Fill/MP_Fill。
    /// </summary>
    /// <summary>单颗绘制：打开捕获窗口，游戏这一笔的真实参数由 <see cref="SpriteBatchCapture"/> 交出来。</summary>
    private static void Around(int index, Action draw)
    {
        if (!ResourceOverlayTable.AnyResourceDraw())
        {
            draw();
            return;
        }

        if (!_haveSnapshot)
        {
            _snapshot = new PlayerStatsSnapshot(Main.LocalPlayer);
            _haveSnapshot = true;
        }

        _resourceNumber = index;
        SpriteBatchCapture.Pre = OnPre;
        SpriteBatchCapture.Post = OnPost;
        SpriteBatchCapture.Active = true;

        try
        {
            draw();
        }
        finally
        {
            SpriteBatchCapture.Active = false;
            SpriteBatchCapture.Pre = null;
            SpriteBatchCapture.Post = null;
        }
    }

    private static bool OnPre(SpriteBatchCapture.DrawInfo info)
    {
        if (info.Texture is not { } texture) return true;

        if (FindAsset(texture) is not { } asset) return true;

        _current = new ResourceOverlayDrawContext(_snapshot, DisplaySet, _resourceNumber, asset)
        {
            SpriteBatch = Main.spriteBatch,
        };
        _current.position = info.Position;
        _current.source = info.Source;
        _current.color = info.Color;
        _current.rotation = info.Rotation;
        _current.origin = info.Origin;
        _current.scale = info.Scale;
        _current.effects = info.Effects;

        var go = true;
        DispatchResource(true, _current, ref go);
        return go;
    }

    private static void OnPost(SpriteBatchCapture.DrawInfo info)
    {
        if (FindAsset(info.Texture) is null) return;

        var go = true;
        DispatchResource(false, _current, ref go);
    }

    /// <summary>把这一笔的贴图对回它对应的 Asset（按原生指针）；认不出就放过。</summary>
    private static Asset<Texture2D>? FindAsset(Texture2D texture)
    {
        if (Matches(texture, TextureAssets.Heart)) return TextureAssets.Heart;
        if (Matches(texture, TextureAssets.Heart2)) return TextureAssets.Heart2;
        if (Matches(texture, TextureAssets.Mana)) return TextureAssets.Mana;

        // 游戏自己加载的资产名（如 Content/Images/UI/...）：按名字请求同一张，拿到精确的 Asset。
        try
        {
            var name = texture.Name;
            if (!string.IsNullOrEmpty(name))
                return Main.Assets.Request<Texture2D>(name, ReLogic.Content.AssetRequestMode.ImmediateLoad);
        }
        catch
        {
            // 认不出就放过这一笔。
        }

        return null;
    }

    private static bool Matches(Texture2D texture, Asset<Texture2D>? asset)
    {
        try
        {
            return asset?.Value is { } value && value.Pointer == texture.Pointer;
        }
        catch
        {
            return false;
        }
    }

    private static void DispatchResource(bool before, ResourceOverlayDrawContext context, ref bool go)
    {
        for (var slot = 0; slot < ResourceOverlayTable.Count; slot++)
        {
            var mask = ResourceOverlayTable.MaskAt(slot);
            if (before ? !mask.Has(HookId.PreDrawResource) : !mask.Has(HookId.PostDrawResource)) continue;

            var overlay = ResourceOverlayTable.At(slot);
            try
            {
                if (before)
                {
                    if (!overlay.PreDrawResource(context)) go = false;
                }
                else
                {
                    overlay.PostDrawResource(context);
                }
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[resourceoverlay] {overlay.Name} {(before ? "PreDrawResource" : "PostDrawResource")} 异常：{Safe.Describe(e)}");
            }
        }
    }
}