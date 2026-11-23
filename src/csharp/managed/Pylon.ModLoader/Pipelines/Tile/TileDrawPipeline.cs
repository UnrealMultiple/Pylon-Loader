using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;
using Pylon.ModLoader.Hooks;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.Drawing;

namespace Pylon.ModLoader.Pipelines;
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
    private static readonly List<Point> Vines = [];
    private static readonly HashSet<string> Reported = new(StringComparer.Ordinal);
    private static bool _drawing;

    private static IModLog Log => LoaderHost.Current.Log;

    /// <summary>tiledraw 管线的安装门：条件与订阅同居一门，由 GateHub 自动发现与派发。</summary>
    internal static class Gates
    {
        internal struct Pass : IGate
        {
            public static bool Wanted() => DrawWanted();

            public static void Install()
            {
                On.Terraria.GameContent.Drawing.TileDrawing.PostDrawTiles += Draw;
                On.Terraria.GameContent.Drawing.TileDrawing.AddSpecialLegacyPoint_Point += AddPoint;
                On.Terraria.GameContent.Drawing.TileDrawing.AddSpecialLegacyPoint_int_int += AddPointXY;
                Log.Info("[tiledraw] 已挂（方块画完后的 DrawEffects / SpecialDraw）");
            }
        }
    }

    public bool Install() => GateHub<TileDrawPipeline>.Run();

    private static bool DrawWanted() =>
        HookGuard.Wants<ModTile>(HookId.PreDraw, HookId.DrawEffects, HookId.PostDraw, HookId.SpecialDraw) ||
        GlobalTileRegistry.AnyWith(HookId.PreDraw, HookId.DrawEffects, HookId.PostDraw, HookId.SpecialDraw) ||
        ModContent.GetContent<ModPylon>().Count > 0;

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
        Vines.Clear();

        try
        {
            DrawVisible(batch, self);
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

    internal static void AddVine(int i, int j)
    {
        if (_drawing) Vines.Add(new Point(i, j));
    }

    private static void DrawVisible(SpriteBatch batch, TileDrawing self)
    {
        var screen = Main.screenPosition;
        var left = Math.Max(0, (int)(screen.X / 16f) - 3);
        var top = Math.Max(0, (int)(screen.Y / 16f) - 3);
        var right = Math.Min(Main.maxTilesX, left + Main.screenWidth / 16 + 8);
        var bottom = Math.Min(Main.maxTilesY, top + Main.screenHeight / 16 + 8);

        var preDrawGlobals = GlobalTileRegistry.For(HookId.PreDraw);
        var drawEffectsGlobals = GlobalTileRegistry.For(HookId.DrawEffects);
        var postDrawGlobals = GlobalTileRegistry.For(HookId.PostDraw);

        for (var i = left; i < right; i++)
        {
            for (var j = top; j < bottom; j++)
            {
                var tile = Main.tile[i, j];
                if (!tile.active()) continue;

                var type = tile.type;
                var mod = TileTable.Of(type);
                var hasDrawHooks = preDrawGlobals.Count > 0 || drawEffectsGlobals.Count > 0 ||
                                   (mod is not null &&
                                    (mod.HookEnabled(HookId.PreDraw) || mod.HookEnabled(HookId.DrawEffects)));

                if (!hasDrawHooks)
                {
                    if (mod is not null && mod.HookEnabled(HookId.PostDraw))
                    {
                        try { mod.PostDraw(i, j, batch); }
                        catch (Exception e) { Report(mod.FullKey, $"[tiledraw] {mod.Name}.PostDraw 异常", e); }
                    }

                    for (var g = 0; g < postDrawGlobals.Count; g++)
                    {
                        try { postDrawGlobals[g].PostDraw(i, j, type, batch); }
                        catch (Exception e) { Report(null, "[tiledraw] GlobalTile.PostDraw 异常", e); }
                    }

                    continue;
                }

                var draw = true;
                for (var g = 0; g < preDrawGlobals.Count; g++)
                {
                    try
                    {
                        if (!preDrawGlobals[g].PreDraw(i, j, type, batch)) draw = false;
                    }
                    catch (Exception e) { Report(null, "[tiledraw] GlobalTile.PreDraw 异常", e); }
                }

                if (draw && mod is not null && mod.HookEnabled(HookId.PreDraw))
                {
                    try
                    {
                        if (!mod.PreDraw(i, j, batch)) draw = false;
                    }
                    catch (Exception e) { Report(mod.FullKey, $"[tiledraw] {mod.Name}.PreDraw 异常", e); }
                }

                var data = new TileDrawData
                {
                    tileFrameX = tile.frameX,
                    tileFrameY = tile.frameY,
                    typeCache = type,
                    tileLight = Lighting.GetColor(i, j),
                    tileCache = tile,
                };

                for (var g = 0; g < drawEffectsGlobals.Count; g++)
                {
                    try { drawEffectsGlobals[g].DrawEffects(i, j, type, batch, ref data); }
                    catch (Exception e) { Report(null, "[tiledraw] GlobalTile.DrawEffects 异常", e); }
                }

                if (mod is not null && mod.HookEnabled(HookId.DrawEffects))
                {
                    try { mod.DrawEffects(i, j, batch, data); }
                    catch (Exception e) { Report(mod.FullKey, $"[tiledraw] {mod.Name}.DrawEffects 异常", e); }
                }

                if (draw && DrawTile(batch, self, i, j, type, data.tileLight))
                {
                    if (mod is not null && mod.HookEnabled(HookId.PostDraw))
                    {
                        try { mod.PostDraw(i, j, batch); }
                        catch (Exception e) { Report(mod.FullKey, $"[tiledraw] {mod.Name}.PostDraw 异常", e); }
                    }

                    for (var g = 0; g < postDrawGlobals.Count; g++)
                    {
                        try { postDrawGlobals[g].PostDraw(i, j, type, batch); }
                        catch (Exception e) { Report(null, "[tiledraw] GlobalTile.PostDraw 异常", e); }
                    }
                }
            }
        }

        for (var k = 0; k < Special.Count; k++)
        {
            var point = Special[k];
            var mod = TileTable.At(point.X, point.Y);
            var type = Main.tile[point.X, point.Y].type;

            if (mod is not null && mod.HookEnabled(HookId.SpecialDraw))
            {
                try { mod.SpecialDraw(point.X, point.Y, batch); }
                catch (Exception e) { Report(mod.FullKey, $"[tiledraw] {mod.Name}.SpecialDraw 异常", e); }
            }

            var specialGlobals = GlobalTileRegistry.For(HookId.SpecialDraw);
            for (var g = 0; g < specialGlobals.Count; g++)
            {
                try { specialGlobals[g].SpecialDraw(point.X, point.Y, type, batch); }
                catch (Exception e) { Report(null, "[tiledraw] GlobalTile.SpecialDraw 异常", e); }
            }
        }

        for (var k = 0; k < Vines.Count; k++)
        {
            var point = Vines[k];
            var shape = Terraria.ObjectData.TileObjectData.GetTileData(Main.tile[point.X, point.Y]);
            if (shape is null) continue;

            try
            {
                self.DrawMultiTileVinesInWind(Main.screenPosition, Vector2.Zero, point.X, point.Y,
                    shape.Width, shape.Height);
            }
            catch (Exception e) { Report(null, "[tiledraw] 藤蔓随风绘制异常", e); }
        }

        DrawPreviewCrystal(batch);
    }

    private static bool DrawTile(SpriteBatch batch, TileDrawing self, int i, int j, int type, Color fallbackLight)
    {
        try
        {
            var tile = Main.tile[i, j];
            short fx = tile.frameX, fy = tile.frameY;
            self.GetTileDrawData(i, j, tile, (ushort)type, ref fx, ref fy, out var width, out var height, out var top,
                out var halfBrick, out var addFrX, out var addFrY, out var effect, out _, out _, out _);

            if (width <= 0 || height <= halfBrick) return false;

            var source = new Rectangle(fx + addFrX, fy + addFrY, width, height - halfBrick);
            var center = Terraria.ID.TileID.Sets.DoNotAdjustDrawPositionBasedOnTileWidth[type]
                ? 0f
                : (width - 16) * 0.5f;
            var position = new Vector2(i * 16 - center - (int)Main.screenPosition.X,
                j * 16 + top + halfBrick - (int)Main.screenPosition.Y);

            batch.Draw(Terraria.GameContent.TextureAssets.Tile[type].Value, position,
                new Il2CppSystem.Nullable<Rectangle>(source), fallbackLight, 0f, Vector2.Zero, 1f, effect, 0f);
            return true;
        }
        catch (Exception e)
        {
            Report(null, "[tiledraw] 重画异常", e);
            return false;
        }
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

    private static void Report(string? key, string what, Exception e)
    {
        if (key is not null && !Reported.Add(key)) return;
        ContentErrors.Line($"{what}：{Safe.Dump(e)}");
    }
}
