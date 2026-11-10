using System;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using ReLogic.Content;
using System.Collections.Generic;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI.ResourceSets;

namespace PipelineTest.Content;

/// <summary>
/// </summary>
public sealed class TestLifeOverlay : ModResourceOverlay
{
    public static int CtPreDrawResource, CtPostDrawResource;
    public static int CtPreDrawResourceDisplay, CtPostDrawResourceDisplay, CtDisplayHoverText;
    public static int LastResourceNumber = -1;

    private static Asset<Texture2D>? _classic, _fancyPanel, _barsFill, _barsPanel;
    private static readonly Dictionary<string, Asset<Texture2D>> VanillaCache = new();

    public override void SetStaticDefaults()
    {
        _classic = PipelineTestMod.Instance.LoadAsset("assets/items/ClassicLifeOverlay.png");
        _fancyPanel = PipelineTestMod.Instance.LoadAsset("assets/items/FancyLifeOverlay_Panel.png");
        _barsFill = PipelineTestMod.Instance.LoadAsset("assets/items/BarsLifeOverlay_Fill.png");
        _barsPanel = PipelineTestMod.Instance.LoadAsset("assets/items/BarsLifeOverlay_Panel.png");
    }

    public override bool PreDrawResource(ResourceOverlayDrawContext context)
    {
        CtPreDrawResource++;
        LastResourceNumber = context.resourceNumber;

        if (!ArtFor(context, out var art, out var offset)) return true;

        context.position += offset;
        context.texture = art!;
        context.source = null;
        context.Draw();

        return false;
    }

    public override void PostDrawResource(ResourceOverlayDrawContext context) => CtPostDrawResource++;

    private static bool ArtFor(ResourceOverlayDrawContext context, out Asset<Texture2D>? art, out Vector2 offset)
    {
        art = null;
        offset = Vector2.Zero;

        var asset = context.texture;
        const string fancy = "Images/UI/PlayerResourceSets/FancyClassic/";
        const string bars = "Images/UI/PlayerResourceSets/HorizontalBars/";

        // 只替换"实心"那笔。空心/掉血那笔(Heart2)的锚点与原版填充层不同,
        // 用同一张实心贴图去替换会让空心的位置偏出去,看起来像多了一层阴影。
        if (Same(asset, TextureAssets.Heart) ||
            Compare(asset, fancy + "Heart_Fill") || Compare(asset, fancy + "Heart_Fill_B"))
        {
            art = _classic;
            return true;
        }

        if (Compare(asset, bars + "HP_Fill") || Compare(asset, bars + "HP_Fill_Honey"))
        {
            art = _barsFill;
            return true;
        }

        // 底板层(Heart_*)的原始贴图尺寸与示例贴图不同,替换会让填充层与底板层错位,
        // 所以这几层交给原版画,覆盖层只接管尺寸能对齐的层。
        return false;
    }

    private static bool Compare(Asset<Texture2D>? asset, string path)
    {
        if (asset is null) return false;

        if (!VanillaCache.TryGetValue(path, out var vanilla))
            vanilla = VanillaCache[path] = Main.Assets.Request<Texture2D>(path, AssetRequestMode.ImmediateLoad);

        return Same(asset, vanilla);
    }

    private static bool Same(Asset<Texture2D>? a, Asset<Texture2D>? b)
    {
        if (a is null || b is null) return ReferenceEquals(a, b);
        if (a.Pointer == b.Pointer) return true;

        var left = a.Name ?? "";
        var right = b.Name ?? "";
        return left.Length > 0 && right.Length > 0 && (left.EndsWith(right, StringComparison.Ordinal) || right.EndsWith(left, StringComparison.Ordinal));
    }

    private static void Draw(ResourceOverlayDrawContext context, Asset<Texture2D>? asset)
    {
        if (asset?.Value is null || context.SpriteBatch is null) return;

        context.texture = asset;
        context.Draw();
    }

    public override bool PreDrawResourceDisplay(PlayerStatsSnapshot snapshot, IPlayerResourcesDisplaySet displaySet,
        bool drawingLife, ref Color textColor, out bool drawText)
    {
        CtPreDrawResourceDisplay++;
        drawText = true;
        return true;
    }

    public override void PostDrawResourceDisplay(PlayerStatsSnapshot snapshot, IPlayerResourcesDisplaySet displaySet,
        bool drawingLife, Color textColor, bool drawText) => CtPostDrawResourceDisplay++;

    public override bool DisplayHoverText(PlayerStatsSnapshot snapshot, IPlayerResourcesDisplaySet displaySet,
        bool drawingLife)
    {
        CtDisplayHoverText++;
        return true;
    }

    public static void Reset()
    {
        CtPreDrawResource = CtPostDrawResource = 0;
        CtPreDrawResourceDisplay = CtPostDrawResourceDisplay = 0;
        CtDisplayHoverText = 0;
        LastResourceNumber = -1;
    }
}
