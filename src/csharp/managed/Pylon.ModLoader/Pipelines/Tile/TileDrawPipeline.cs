using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.Drawing;

namespace Pylon.ModLoader;

/// <summary>
/// 方块的逐格绘制回调：方块画完之后，把屏幕内覆写了 <see cref="ModTile.DrawEffects"/> 的 mod 方块逐格回调一遍；
/// 期间拦下 <c>AddSpecialLegacyPoint</c>，最后对这些点调 <see cref="ModTile.SpecialDraw"/>。
/// 晶塔的浮空晶石就走这条链：<c>ModPylon.DrawEffects</c> → <c>AddSpecialLegacyPoint</c> → <c>SpecialDraw</c>。
/// </summary>
internal sealed class TileDrawPipeline : ILoaderModule
{
    public static TileDrawPipeline Instance { get; } = new();

    public string Name => "tiledraw";

    private static readonly List<Point> Special = [];
    private static readonly HashSet<string> Reported = new(StringComparer.Ordinal);
    private static bool _drawing;

    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        On.Terraria.GameContent.Drawing.TileDrawing.PostDrawTiles += Draw;
        On.Terraria.GameContent.Drawing.TileDrawing.AddSpecialLegacyPoint_Point += AddPoint;
        On.Terraria.GameContent.Drawing.TileDrawing.AddSpecialLegacyPoint_int_int += AddPointXY;

        Log.Info("[tiledraw] 已挂（方块画完后的 DrawEffects / SpecialDraw）");
        return true;
    }

    /// <summary>绘制期作者要的"这一格要单独画"：先记下来，等这一遍方块回调走完再统一画。</summary>
    private static void AddPoint(On.Terraria.GameContent.Drawing.TileDrawing.orig_AddSpecialLegacyPoint_Point orig,
                                 TileDrawing self, Point point)
    {
        if (_drawing)
        {
            Special.Add(new Point(point.X, point.Y));
            return;
        }

        orig(self, point);
    }

    private static void AddPointXY(On.Terraria.GameContent.Drawing.TileDrawing.orig_AddSpecialLegacyPoint_int_int orig,
                                   TileDrawing self, int x, int y)
    {
        if (_drawing)
        {
            Special.Add(new Point(x, y));
            return;
        }

        orig(self, x, y);
    }

    private static void Draw(On.Terraria.GameContent.Drawing.TileDrawing.orig_PostDrawTiles orig,
                             TileDrawing self, bool solidLayer, bool forRenderTargets, bool intoRenderTargets)
    {
        orig(self, solidLayer, forRenderTargets, intoRenderTargets);

        if (solidLayer || intoRenderTargets || Main.gameMenu) return;
        if (TileTable.Count == 0) return;

        var batch = Main.spriteBatch;
        if (batch is null) return;

        var begun = batch._beginCalled;
        if (!begun)
        {
            try
            {
                batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState,
                            DepthStencilState.None, Main.Rasterizer, null,
                            new Il2CppSystem.Nullable<Matrix>(Main.Transform), true);
            }
            catch (Exception e)
            {
                Report("begin", "[tiledraw] SpriteBatch.Begin 异常", e);
                return;
            }
        }

        var toScreen = Main.drawToScreen;
        Main.drawToScreen = true;
        _drawing = true;
        Special.Clear();

        try
        {
            DrawVisible(batch);
        }
        finally
        {
            _drawing = false;
            Main.drawToScreen = toScreen;
            if (!begun)
            {
                try { batch.End(); } catch { /* 批次坏了就别再抛 */ }
            }
        }
    }

    private static void DrawVisible(SpriteBatch batch)
    {
        var screen = Main.screenPosition;
        var left = Math.Max(0, (int)(screen.X / 16f) - 3);
        var top = Math.Max(0, (int)(screen.Y / 16f) - 3);
        var right = Math.Min(Main.maxTilesX, left + Main.screenWidth / 16 + 8);
        var bottom = Math.Min(Main.maxTilesY, top + Main.screenHeight / 16 + 8);

        for (var i = left; i < right; i++)
        {
            for (var j = top; j < bottom; j++)
            {
                var tile = Main.tile[i, j];
                if (!tile.active()) continue;

                var mod = TileTable.At(i, j);
                if (mod is null || !mod.HookEnabled(HookId.DrawEffects)) continue;

                try
                {
                    var data = new TileDrawData
                    {
                        tileFrameX = tile.frameX,
                        tileFrameY = tile.frameY,
                        typeCache = tile.type,
                        tileLight = Lighting.GetColor(i, j),
                        tileCache = tile,
                    };
                    mod.DrawEffects(i, j, batch, data);
                }
                catch (Exception e)
                {
                    Report(mod.FullKey + ".DrawEffects", $"[tiledraw] {mod.Name}.DrawEffects 异常", e);
                }
            }
        }

        for (var k = 0; k < Special.Count; k++)
        {
            var point = Special[k];
            var mod = TileTable.At(point.X, point.Y);
            if (mod is null || !mod.HookEnabled(HookId.SpecialDraw)) continue;

            try
            {
                mod.SpecialDraw(point.X, point.Y, batch);
            }
            catch (Exception e)
            {
                Report(mod.FullKey + ".SpecialDraw", $"[tiledraw] {mod.Name}.SpecialDraw 异常", e);
            }
        }

        DrawPreviewCrystal(batch);
    }

    /// <summary>
    /// 放置预览只有方块贴图（原版也是），晶塔的浮空晶石是 SpecialDraw 才有的。
    /// 拿玩家当前的放置预览数据把晶石补上，预览才和放下之后长得一样。
    /// </summary>
    private static void DrawPreviewCrystal(SpriteBatch batch)
    {
        // 放置预览的数据是静态的那一份（Player.PlaceThing_Tiles 每次复用它）。
        var preview = TileObjectPreviewData.placementCache;
        if (preview is null || !preview.Active) return;
        if (ModContent.Find(ContentKind.Tile, (int)preview.Type) is not ModPylon pylon) return;

        var start = preview.ObjectStart;

        try
        {
            pylon.DrawPreviewCrystal(batch, start.X, start.Y);
        }
        catch (Exception e)
        {
            Report(pylon.FullKey + ".DrawPreviewCrystal", $"[tiledraw] {pylon.Name}.DrawPreviewCrystal 异常", e);
        }
    }

    private static void Report(string key, string what, Exception e)
    {
        if (!Reported.Add(key)) return;
        ContentErrors.Report($"{what}：{Safe.Describe(e)}");
    }
}
