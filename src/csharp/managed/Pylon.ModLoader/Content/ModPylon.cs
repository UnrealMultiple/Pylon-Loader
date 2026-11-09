using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.Localization;
using Terraria.Map;
using Terraria.ObjectData;
using Terraria.UI;

namespace Pylon.ModLoader;

/// <summary>
/// 自定义传送晶塔（ModPylon）：3x4 的 ModTile，游戏的地图/传送系统认它，一个类型在世界里只有一个。
/// 类型号排在 TeleportPylonType.Count 之后（游戏自己的枚举到 Count 为止）；放进游戏晶塔列表由管线做；
/// 点击开地图、地图图标、浮空晶石都走这里的默认实现。
/// </summary>
public abstract class ModPylon : ModTile
{
    /// <summary>这边的晶塔类型号；注册时排到游戏的 TeleportPylonType.Count（11）之后。</summary>
    public virtual int PylonType { get; set; } = -1;

    /// <summary>能不能再放一个（游戏默认：一个类型在世界里只有一个）。</summary>
    public virtual bool CanPlacePylon() => !Main.PylonSystem.HasPylonOfType((TeleportPylonType)PylonType);

    /// <summary>目的地/出发地的村民够不够（默认 2 个：在附近、在房子里）。</summary>
    public virtual bool ValidTeleportCheck_NPCCount(TeleportPylonInfo pylonInfo, int defaultNecessaryNPCCount) =>
        TeleportPylonsSystem.DoesPositionHaveEnoughNPCs(defaultNecessaryNPCCount, pylonInfo.PositionInTiles);

    /// <summary>目的地周围的生物群系对不对（sceneData 是围着它扫出来的 SceneMetrics）。</summary>
    public virtual bool ValidTeleportCheck_BiomeRequirements(TeleportPylonInfo pylonInfo, SceneMetrics sceneData) => true;

    /// <summary>在地图上画这晶塔的图标、处理悬停和点击。</summary>
    public virtual void DrawMapIcon(ref MapOverlayDrawContext context, ref string mouseOverText, TeleportPylonInfo pylonInfo,
                                    bool isNearPylon, Color drawColor, float deselectedScale, float selectedScale) { }

    /// <summary>画一张图标贴图；返回 true = 手指在它上面。</summary>
    public virtual bool DefaultDrawMapIcon(ref MapOverlayDrawContext context, Asset<Texture2D> mapIcon, Vector2 drawCenter,
                                           Color drawColor, float deselectedScale, float selectedScale) =>
        context.Draw(mapIcon.Value, drawCenter, drawColor, new SpriteFrame(1, 1, 0, 0), deselectedScale, selectedScale, Alignment.Center).IsMouseOver;

    /// <summary>手指在图标上时写名字；按下左键就关地图并请求传送到它。</summary>
    public virtual void DefaultMapClickHandle(bool mouseIsHovering, TeleportPylonInfo pylonInfo, string hoveringTextKey, ref string mouseOverText)
    {
        if (!mouseIsHovering) return;

        Main.cancelWormHole = true;
        mouseOverText = Language.GetTextValue(hoveringTextKey);

        if (Main.mouseLeft && Main.mouseLeftRelease)
        {
            Main.mouseLeftRelease = false;
            Main.mapFullscreen = false;
            Main.PylonSystem.RequestTeleportation(pylonInfo, Main.LocalPlayer);
        }
    }

    /// <summary>记下作者上一次给晶石的参数：放置预览没有真方块，只能拿这一份来补画。</summary>
    private Asset<Texture2D>? _crystalTexture;
    private Vector2 _crystalOffset;
    private Color _crystalShadow;
    private Color _crystalDust;
    private int _crystalDustChance;
    private int _crystalFrames;

    /// <summary>画浮着的晶石（管线在方块画完后对每个 mod 晶塔调一次）。</summary>
    public virtual void DefaultDrawPylonCrystal(SpriteBatch spriteBatch, int i, int j, Asset<Texture2D> crystalTexture,
                                                Asset<Texture2D> crystalHighlightTexture, Vector2 crystalOffset, Color pylonShadowColor,
                                                Color dustColor, int dustChanceDenominator, int crystalVerticalFrameCount)
    {
        _crystalTexture = crystalTexture;
        _crystalOffset = crystalOffset;
        _crystalShadow = pylonShadowColor;
        _crystalDust = dustColor;
        _crystalDustChance = dustChanceDenominator;
        _crystalFrames = crystalVerticalFrameCount;

        DrawCrystalAt(spriteBatch, i, j, crystalTexture, crystalOffset, pylonShadowColor, dustColor,
                      dustChanceDenominator, crystalVerticalFrameCount, preview: false);
    }

    /// <summary>
    /// 放置预览时补画浮空晶石（方块预览只画方块贴图，晶石是 SpecialDraw 才有的）。
    /// 参数取作者上一次 <see cref="DefaultDrawPylonCrystal"/> 给的那一份；i,j 是预览物体的左上角。
    /// </summary>
    public virtual void DrawPreviewCrystal(SpriteBatch spriteBatch, int i, int j)
    {
        if (_crystalTexture is null) return;

        DrawCrystalAt(spriteBatch, i, j, _crystalTexture, _crystalOffset, _crystalShadow, _crystalDust,
                      _crystalDustChance, _crystalFrames, preview: true);
    }

    private void DrawCrystalAt(SpriteBatch spriteBatch, int i, int j, Asset<Texture2D> crystalTexture, Vector2 crystalOffset,
                               Color pylonShadowColor, Color dustColor, int dustChanceDenominator, int crystalVerticalFrameCount,
                               bool preview)
    {
        var offscreen = Main.drawToScreen ? Vector2.Zero : new Vector2(Main.offScreenRange);
        var tile = Main.tile[i, j];

        // 放下之后用这一格自己的 TileObjectData；预览时那格还是空气，用类型默认的数据。
        var tileData = tile.active() ? TileObjectData.GetTileData(tile) : TileObjectData.GetTileData(Type, 0);
        if (tileData is null) return;

        var frameY = Main.tileFrameCounter[TileID.TeleportationPylon] / Math.Max(1, crystalVerticalFrameCount);
        var frame = new Rectangle(0, frameY * (crystalTexture.Height() / crystalVerticalFrameCount),
                                  crystalTexture.Width(), crystalTexture.Height() / crystalVerticalFrameCount);
        frame.Height -= 1;

        var origin = new Vector2(frame.Width / 2f, frame.Height / 2f);
        var crystalPosition = new Point16(i, j).ToWorldCoordinates(tileData.CoordinateFullWidth / 2f - 2f,
                                                                  tileData.CoordinateFullHeight / 2f) + crystalOffset;
        var sinusoidalOffset = (float)Math.Sin(Main.GlobalTimeWrappedHourly * (Math.PI * 2) / 5);
        var drawingPosition = crystalPosition + offscreen + new Vector2(0f, sinusoidalOffset * 4f);

        if (!preview && !Main.gamePaused && dustChanceDenominator > 0 && Main.rand.Next(4 * dustChanceDenominator) == 0)
        {
            var corner = crystalPosition - origin;
            var numForDust = Dust.NewDust(corner, frame.Width, frame.Height, DustID.TintableDustLighted, 0f, 0f, 254, dustColor, 0.5f);
            var dust = Main.dust[numForDust];
            dust.velocity = new Vector2(dust.velocity.X * 0.1f, dust.velocity.Y * 0.1f - 0.2f);
        }

        var light = Lighting.GetColor(i, j);
        var color = new Color((int)((light.R + (255 - light.R) * 0.8f) * 0.7f),
                              (int)((light.G + (255 - light.G) * 0.8f) * 0.7f),
                              (int)((light.B + (255 - light.B) * 0.8f) * 0.7f),
                              (int)((light.A + (255 - light.A) * 0.8f) * 0.7f));

        var texture = crystalTexture.Value;
        var sourceRect = new Il2CppSystem.Nullable<Rectangle>(frame);

        // 必须用 9 参重载（position / sourceRect / color / rotation / origin / scale / effects / depth）。
        // 10 参那个第 3 个参数是 destinationRectangle，第 4 个才是 sourceRectangle；按 9 参的样子传会
        // 把 null 塞进 Nullable<Rectangle>，生成层 ToPtrNotNull 直接 NRE（真机上就是"只有底座、没有晶石"）。
        spriteBatch.Draw(texture, drawingPosition - Main.screenPosition, sourceRect, color, 0f, origin, 1f,
                         SpriteEffects.None, 0f);

        var scale = (float)Math.Sin(Main.GlobalTimeWrappedHourly * ((float)Math.PI * 2f)) * 0.2f + 0.8f;
        var shadowColor = pylonShadowColor * scale;
        for (var shadowPos = 0f; shadowPos < 1f; shadowPos += 1f / 6f)
        {
            var offset = new Vector2((float)Math.Cos(Math.PI * 2f * shadowPos), (float)Math.Sin(Math.PI * 2f * shadowPos)) * (6f + sinusoidalOffset * 2f);
            spriteBatch.Draw(texture, drawingPosition - Main.screenPosition + offset, sourceRect, shadowColor, 0f, origin, 1f,
                             SpriteEffects.None, 0f);
        }
    }

    /// <summary>
    /// 方块画完后的"这一格要单独画"：只有左上角那格登记，管线随后会调 <see cref="SpecialDraw"/> 画浮空晶石。
    /// 本版没有逐格绘制入口，管线补了这条链：这里登记一个点，管线随后对它调 SpecialDraw。
    /// </summary>
    public override void DrawEffects(int i, int j, SpriteBatch spriteBatch, TileDrawData drawData)
    {
        var tile = Main.tile[i, j];
        var data = TileObjectData.GetTileData(tile);
        if (data is null) return;

        if (tile.frameX % data.CoordinateFullWidth != 0 || tile.frameY != 0) return;

        var renderer = Main.instance is null ? null : Main.instance.TilesRenderer;
        renderer?.AddSpecialLegacyPoint(i, j);
    }

    /// <summary>点它开地图（打开全屏地图并把原点设到这座晶塔）。</summary>
    public override bool RightClick(int i, int j)
    {
        PylonPipeline.OpenMap(i, j);
        return true;
    }

    /// <summary>总是智能交互的目标（点它开地图）。</summary>
    public override bool HasSmartInteract(int i, int j, object settings) => true;

    /// <summary>放下时建实体（左上角那一格）；返回实体 ID。</summary>
    public override int Hook_AfterPlacement(int i, int j, int type, int style, int direction, int alternate)
    {
        var data = TileObjectData.GetTileData(type, style, alternate);
        if (data is null) return -1;

        var topLeftX = i - data.Origin.X;
        var topLeftY = j - data.Origin.Y;
        return PylonPipeline.PlaceEntity(topLeftX, topLeftY);
    }
}
