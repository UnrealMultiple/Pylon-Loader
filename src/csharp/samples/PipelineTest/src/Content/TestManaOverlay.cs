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
public sealed class TestManaOverlay : ModResourceOverlay
{
    public static int CtPreDrawResource, CtPostDrawResource;
    public static int LastResourceNumber = -1;

    private static Asset<Texture2D>? _classic, _fancyPanel, _barsFill, _barsPanel;
    private static readonly Dictionary<string, Asset<Texture2D>> VanillaCache = new();

    public override void SetStaticDefaults()
    {
        _classic = PipelineTestMod.Instance.LoadAsset("assets/items/ClassicManaOverlay.png");
        _fancyPanel = PipelineTestMod.Instance.LoadAsset("assets/items/FancyManaOverlay_Panel.png");
        _barsFill = PipelineTestMod.Instance.LoadAsset("assets/items/BarsManaOverlay_Fill.png");
        _barsPanel = PipelineTestMod.Instance.LoadAsset("assets/items/BarsManaOverlay_Panel.png");
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

        if (Same(asset, TextureAssets.Mana) || Compare(asset, fancy + "Star_Fill"))
        {
            art = _classic;
            return true;
        }

        if (Compare(asset, bars + "MP_Fill"))
        {
            art = _barsFill;
            return true;
        }

        if (Compare(asset, fancy + "Star_A") || Compare(asset, fancy + "Star_B") ||
            Compare(asset, fancy + "Star_C") || Compare(asset, fancy + "Star_Single"))
        {
            offset = Compare(asset, fancy + "Star_Single") ? new Vector2(4, 4) : new Vector2(4, 0);
            art = _fancyPanel;
            return true;
        }

        if (Compare(asset, bars + "MP_Panel_Middle"))
        {
            offset = new Vector2(0, 6);
            art = _barsPanel;
            return true;
        }

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

    public static void Reset()
    {
        CtPreDrawResource = CtPostDrawResource = 0;
        LastResourceNumber = -1;
    }
}
