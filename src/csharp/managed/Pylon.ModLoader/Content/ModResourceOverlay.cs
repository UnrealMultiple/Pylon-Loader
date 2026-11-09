using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent.UI.ResourceSets;

namespace Pylon.ModLoader;

/// <summary>
/// 资源条（生命/魔力）的覆盖层：单颗心/星按次给绘制上下文，整条给显示快照。
/// <c>PreDrawResourceDisplay</c> / <c>PostDrawResourceDisplay</c> / <c>DisplayHoverText</c>）。
/// </summary>
public abstract class ModResourceOverlay : ModType
{
    /// <summary>单颗心 / 魔力星画之前；false = 这颗不画。context 可改（改完调 <see cref="ResourceOverlayDrawContext.Draw"/>）。</summary>
    public virtual bool PreDrawResource(ResourceOverlayDrawContext context) => true;

    /// <summary>单颗心 / 魔力星画之后。</summary>
    public virtual void PostDrawResource(ResourceOverlayDrawContext context) { }

    /// <summary>整条生命/魔力条画之前；返回 false = 整条不画，drawText = 是否画生命数值。</summary>
    public virtual bool PreDrawResourceDisplay(PlayerStatsSnapshot snapshot, IPlayerResourcesDisplaySet displaySet,
        bool drawingLife, ref Color textColor, out bool drawText)
    {
        drawText = true;
        return true;
    }

    /// <summary>整条生命/魔力条画之后。</summary>
    public virtual void PostDrawResourceDisplay(PlayerStatsSnapshot snapshot, IPlayerResourcesDisplaySet displaySet,
        bool drawingLife, Color textColor, bool drawText) { }

    /// <summary>悬停提示要不要画（false = 不画）。</summary>
    public virtual bool DisplayHoverText(PlayerStatsSnapshot snapshot, IPlayerResourcesDisplaySet displaySet,
        bool drawingLife) => true;
}

public struct ResourceOverlayDrawContext
{
    public readonly PlayerStatsSnapshot snapshot;
    public readonly int resourceNumber;
    public Asset<Texture2D> texture;
    public Vector2 position;
    public Rectangle? source;
    public Color color;
    public float rotation;
    public Vector2 origin;
    public Vector2 scale;
    public SpriteEffects effects;

    public IPlayerResourcesDisplaySet DisplaySet { get; init; }

    public SpriteBatch SpriteBatch { get; init; }

    public ResourceOverlayDrawContext(PlayerStatsSnapshot snapshot, IPlayerResourcesDisplaySet displaySet,
        int resourceNumber, Asset<Texture2D> texture)
    {
        this.snapshot = snapshot;
        DisplaySet = displaySet;
        this.resourceNumber = resourceNumber;
        this.texture = texture;
        position = Vector2.Zero;
        source = null;
        color = Color.White;
        rotation = 0;
        origin = Vector2.Zero;
        scale = Vector2.One;
        effects = SpriteEffects.None;
        SpriteBatch = Main.spriteBatch;
    }

    /// <summary>按当前字段画一次（作者改完 position/color/scale 后调）。</summary>
    public void Draw()
    {
        var batch = SpriteBatch;
        var tex = texture;
        if (batch is null || tex?.Value is not { } value) return;

        var frame = source;
        if (frame is null)
        {
            // 作者贴图由 LoadAsset 创建，游戏包装的 Width/Height 在真机上是 0（读它会 NRE、setter 静默无效）；
            // source=null 时 SpriteBatch 拿它算源矩形就画不出来。按 Unity 侧尺寸显式给源矩形。
            try
            {
                Textures.PylonTexture.Size(value, out var width, out var height);
                if (width > 0 && height > 0) frame = new Rectangle(0, 0, width, height);
            }
            catch
            {
                // 拿不到尺寸就保持原行为（交给 SpriteBatch 自己算）。
            }
        }

        var sourceRect = frame is { } rectangle ? new Il2CppSystem.Nullable<Rectangle>(rectangle) : null;
        batch.Draw(value, position, sourceRect, color, rotation, origin, scale, effects, 0f);
    }
}