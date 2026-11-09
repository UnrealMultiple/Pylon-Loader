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
        return true;
    }

    public override void PostDrawResource(ResourceOverlayDrawContext context)
    {
        CtPostDrawResource++;

        var asset = context.texture;
        const string fancy = "Images/UI/PlayerResourceSets/FancyClassic/";
        const string bars = "Images/UI/PlayerResourceSets/HorizontalBars/";

        if (Same(asset, TextureAssets.Heart) || Same(asset, TextureAssets.Heart2) ||
            Compare(asset, fancy + "Heart_Fill") || Compare(asset, fancy + "Heart_Fill_B"))
        {
            Draw(context, _classic);
        }
        else if (Compare(asset, bars + "HP_Fill") || Compare(asset, bars + "HP_Fill_Honey"))
        {
            Draw(context, _barsFill);
        }
        else if (Compare(asset, fancy + "Heart_Left") || Compare(asset, fancy + "Heart_Middle") ||
                 Compare(asset, fancy + "Heart_Right") || Compare(asset, fancy + "Heart_Right_Fancy") ||
                 Compare(asset, fancy + "Heart_Single_Fancy"))
        {
            // 与原示例一致的偏移。
            var offset = Compare(asset, fancy + "Heart_Single_Fancy") ? new Vector2(8, 8)
                       : Compare(asset, fancy + "Heart_Left") ? new Vector2(4, 4)
                       : new Vector2(0, 4);

            context.position += offset;
            Draw(context, _fancyPanel);
        }
        else if (Compare(asset, bars + "HP_Panel_Middle"))
        {
            context.position.Y += 6;
            Draw(context, _barsPanel);
        }
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