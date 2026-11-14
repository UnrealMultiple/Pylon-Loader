using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.GameContent.Drawing;

namespace Pylon.ModLoader;

/// <summary>
/// 逐格绘制：游戏的格宽/偏移/行高 switch 只认它自己的家具，mod 家具永远 16px。
/// 钩 GetTileDrawData 两个 16 参重载做形状/动画修正并派发作者三钩子；
/// 另钩 AnimateTiles 推进 mod 动画帧。作者钩子的结果走 4096 槽缓存
/// （键 12 int + 世界/16×16 区域时代 + 5s TTL，区域触碰挂 SquareTileFrame/RangeFrame），
/// special 面（Tile 重载）与 CacheDrawData=false 的类型不缓存。
/// </summary>
internal static class TileDrawHooks
{
    private static bool _installed;
    private static int[] _animHeights = [];
    private static int[] _frameCounts = []; // 0 = 贴图还没就绪，晚点再算
    private static bool[] _uncached = [];   // CacheDrawData=false 的类型（安装期读一次）

    private static IModLog Log => LoaderHost.Current.Log;

    /// <summary>TileTable.Build 之后调用（形状/动画帧高快照就绪）。</summary>
    internal static void Build(IModLog log)
    {
        if (_installed) return;
        _installed = true;

        var total = VanillaCounts.Tile + ModContent.Count(ContentKind.Tile);
        _animHeights = new int[total];
        foreach (var tile in ModContent.GetContent<ModTile>())
        {
            var type = tile.Type;
            if (type < VanillaCounts.Tile || type >= total) continue;
            _animHeights[type] = tile.AnimationFrameHeight;
        }
        _frameCounts = new int[total];

        // 缓存面：有作者绘制钩子且该类型声明可缓存的才装时代触碰钩（无 mod 钩子零开销）。
        _uncached = new bool[total];
        var cacheWanted = false;
        foreach (var tile in ModContent.GetContent<ModTile>())
        {
            var type = tile.Type;
            if (type < VanillaCounts.Tile || type >= total) continue;

            _uncached[type] = !tile.CacheDrawData;
            if (!_uncached[type] &&
                (tile.HookEnabled(HookId.AnimateIndividualTile) ||
                 tile.HookEnabled(HookId.SetDrawPositions) ||
                 tile.HookEnabled(HookId.SetSpriteEffects)))
                cacheWanted = true;
        }

        if (cacheWanted)
        {
            _drawCache = new DrawSlot[DrawCacheSlots];
            _areaEpoch = new uint[AreaEpochs];
            // 方块摆放/破坏/重框都会让邻域的 mod 格画得不一样：游戏在这两个入口重框邻域。
            On.Terraria.WorldGen.SquareTileFrame += SquareTileFrame;
            On.Terraria.WorldGen.RangeFrame += RangeFrame;
            On.Terraria.Map.MapHelper.Initialize += MapInit; // 换世界：缓存从头再来
        }

        // bool halfBrick 那个重载是草/藤的，不挂。
        On.Terraria.GameContent.Drawing.TileDrawing.GetTileDrawData_int_int_TileDrawInfo_ushort_shortRef_shortRef_intRef_intRef_intRef_intRef_intRef_intRef_SpriteEffectsRef_Texture2DRef_RectangleRef_ColorRef += DrawData;
        On.Terraria.GameContent.Drawing.TileDrawing.GetTileDrawData_int_int_Tile_ushort_shortRef_shortRef_intRef_intRef_intRef_intRef_intRef_intRef_SpriteEffectsRef_Texture2DRef_RectangleRef_ColorRef += SpecialDrawData;
        On.Terraria.Main.AnimateTiles += AnimateTiles;
        log.Info("[tile] 逐格绘制已挂（形状 / 动画 / SetDrawPositions" +
                 (cacheWanted ? "；绘制缓存已挂" : "") + "）");
    }

    private static void DrawData(
        On.Terraria.GameContent.Drawing.TileDrawing.orig_GetTileDrawData_int_int_TileDrawInfo_ushort_shortRef_shortRef_intRef_intRef_intRef_intRef_intRef_intRef_SpriteEffectsRef_Texture2DRef_RectangleRef_ColorRef orig,
        TileDrawing self, int x, int y, TileDrawInfo tileCache, ushort type,
        ref short tileFrameX, ref short tileFrameY, out int tileWidth, out int tileHeight, out int tileTop,
        out int halfBrickHeight, out int addFrX, out int addFrY, out SpriteEffects tileSpriteEffect,
        out Texture2D glowTexture, ref Rectangle glowSourceRect, ref Color glowColor)
    {
        orig(self, x, y, tileCache, type, ref tileFrameX, ref tileFrameY, out tileWidth, out tileHeight,
            out tileTop, out halfBrickHeight, out addFrX, out addFrY, out tileSpriteEffect, out glowTexture,
            ref glowSourceRect, ref glowColor);

        var frameX = tileFrameX;
        var frameY = tileFrameY;
        Apply(x, y, type, special: false, ref frameX, ref frameY, ref tileWidth, ref tileHeight, ref tileTop,
            ref addFrX, ref addFrY, ref tileSpriteEffect);
        tileFrameX = (short)frameX;
        tileFrameY = (short)frameY;
    }

    private static void SpecialDrawData(
        On.Terraria.GameContent.Drawing.TileDrawing.orig_GetTileDrawData_int_int_Tile_ushort_shortRef_shortRef_intRef_intRef_intRef_intRef_intRef_intRef_SpriteEffectsRef_Texture2DRef_RectangleRef_ColorRef orig,
        TileDrawing self, int x, int y, Tile tileCache, ushort type,
        ref short tileFrameX, ref short tileFrameY, out int tileWidth, out int tileHeight, out int tileTop,
        out int halfBrickHeight, out int addFrX, out int addFrY, out SpriteEffects tileSpriteEffect,
        out Texture2D glowTexture, out Rectangle glowSourceRect, out Color glowColor)
    {
        orig(self, x, y, tileCache, type, ref tileFrameX, ref tileFrameY, out tileWidth, out tileHeight,
            out tileTop, out halfBrickHeight, out addFrX, out addFrY, out tileSpriteEffect, out glowTexture,
            out glowSourceRect, out glowColor);

        var frameX = tileFrameX;
        var frameY = tileFrameY;
        Apply(x, y, type, special: true, ref frameX, ref frameY, ref tileWidth, ref tileHeight, ref tileTop,
            ref addFrX, ref addFrY, ref tileSpriteEffect);
        tileFrameX = (short)frameX;
        tileFrameY = (short)frameY;
    }

    /// <summary>原版算完之后的 mod 修正；作者钩子按位集门控（没钩的零开销）。</summary>
    private static void Apply(int x, int y, ushort type, bool special, ref short tileFrameX, ref short tileFrameY,
                              ref int width, ref int height, ref int top, ref int addFrX, ref int addFrY,
                              ref SpriteEffects effects)
    {
        if (type < VanillaCounts.Tile) return;

        var shapes = TileTable.DrawShapes;
        var shape = (uint)type < (uint)shapes.Length ? shapes[type] : default;

        var modTile = TileTable.Of(type);
        var wantsAnimate = modTile is not null && modTile.HookEnabled(HookId.AnimateIndividualTile);
        var wantsPositions = modTile is not null && modTile.HookEnabled(HookId.SetDrawPositions);
        var wantsEffects = modTile is not null && modTile.HookEnabled(HookId.SetSpriteEffects);
        var wantsAny = wantsAnimate || wantsPositions || wantsEffects;

        // 竖向帧表才动 addFrY（横排帧 AnimationFrameHeight = 0，保持原版结果）。
        var anim = (uint)type < (uint)_animHeights.Length ? _animHeights[type] : 0;
        var tileFrame = 0;
        if (anim > 0 || wantsAny)
        {
            var frames = Main.tileFrame;
            if (frames is not null && (uint)type < (uint)frames.Length) tileFrame = frames[type];
        }
        if (anim > 0) addFrY = tileFrame * anim;

        // 这一格在第几行：frameY 按整对象的高折，逐行减去行高 + 间距。
        if (shape.Ok)
        {
            var rest = shape.FullHeight > 0 ? tileFrameY % shape.FullHeight : 0;
            var row = 0;
            while (row + 1 < shape.Rows && rest - shape.Heights[row] - shape.Padding >= 0)
            {
                rest -= shape.Heights[row] + shape.Padding;
                row++;
            }

            width = shape.Width;
            top = shape.YOffset;
            height = shape.Heights[row];
        }

        if (modTile is null || !wantsAny) return;

        var fx = (int)tileFrameX;
        var fy = (int)tileFrameY;
        var w = width;
        var h = height;
        var tp = top;
        var ax = addFrX;
        var ay = addFrY;
        var eff = effects;

        // 缓存键含作者读到的全部输入（位置/类型/当前帧 + 修正后八值）；时代在钩子之前取——
        // 钩子跑的当口世界变了，条目一出生就是旧的。
        var cacheable = !special && _drawCache is not null &&
                        ((uint)type >= (uint)_uncached.Length || !_uncached[type]);
        var epochWorld = cacheable ? _drawEpoch : 0;
        var epochArea = cacheable ? AreaEpochAt(x, y) : 0;
        var now = cacheable ? Environment.TickCount64 : 0;

        if (cacheable && TryGetDraw(x, y, type, tileFrame, fx, fy, w, h, tp, ax, ay, (int)eff,
                                    epochWorld, epochArea, now,
                                    out var oFx, out var oFy, out var oW, out var oH, out var oTp,
                                    out var oAx, out var oAy, out var oEff))
        {
            fx = oFx;
            fy = oFy;
            w = oW;
            h = oH;
            tp = oTp;
            ax = oAx;
            ay = oAy;
            eff = (SpriteEffects)oEff;
        }
        else
        {
            if (wantsAnimate)
            {
                var frX = ax;
                var frY = ay;
                try
                {
                    modTile.AnimateIndividualTile(type, x, y, ref frX, ref frY);
                    ax = frX;
                    ay = frY;
                }
                catch (Exception e)
                {
                    Report(modTile, HookId.AnimateIndividualTile, e);
                }
            }

            if (wantsPositions)
            {
                var pw = w;
                var poff = tp;
                var ph = h;
                var pfx = fx;
                var pfy = fy;
                try
                {
                    modTile.SetDrawPositions(x, y, ref pw, ref poff, ref ph, ref pfx, ref pfy);
                    w = pw;
                    tp = poff;
                    h = ph;
                    fx = pfx;
                    fy = pfy;
                }
                catch (Exception e)
                {
                    Report(modTile, HookId.SetDrawPositions, e);
                }
            }

            if (wantsEffects)
            {
                var peff = eff;
                try
                {
                    modTile.SetSpriteEffects(x, y, ref peff);
                    eff = peff;
                }
                catch (Exception e)
                {
                    Report(modTile, HookId.SetSpriteEffects, e);
                }
            }

            if (cacheable)
                PutDraw(x, y, type, tileFrame, fx, fy, w, h, tp, ax, ay, (int)eff, epochWorld, epochArea, now);
        }

        tileFrameX = (short)fx;
        tileFrameY = (short)fy;
        width = w;
        height = h;
        top = tp;
        addFrX = ax;
        addFrY = ay;
        effects = eff;
    }

    // 绘制路径高频：作者异常只记一次，且这次的结果不回写。
    private static readonly HashSet<long> _reported = [];

    private static void Report(ModTile tile, HookId hook, Exception e)
    {
        if (_reported.Add(HashCode.Combine(tile.Type, hook)))
            ContentErrors.Report($"[tile] {tile.Name}.{hook} 异常：{Safe.Describe(e)}");
    }

    // ===================== 绘制缓存 =====================

    private const int DrawCacheSlots = 4096; // 2 的幂
    private const int AreaShift = 4;         // 16×16 格一个区域
    private const int AreaEpochs = 65536;    // 区域时代哈希槽（两区同槽只多失效一次）
    private const long DrawCacheTtlMs = 5000;

    private struct DrawSlot
    {
        public int Busy; // 0 空闲；Interlocked 门（别的线程占着 = 算 miss）
        public bool Used;
        public uint EpochWorld, EpochArea;
        public long Stamp;
        // 键 12 int：位置/类型/当前帧 + 修正后的入参
        public int KeyX, KeyY, KeyType, KeyFrame;
        public int InFrameX, InFrameY, InWidth, InHeight, InTop, InAddFrX, InAddFrY, InEffects;
        // 出参 8 int
        public int OutFrameX, OutFrameY, OutWidth, OutHeight, OutTop, OutAddFrX, OutAddFrY, OutEffects;
    }

    private static DrawSlot[]? _drawCache;
    private static uint[]? _areaEpoch;
    private static uint _drawEpoch = 1; // 从 1 起：空槽的 0 时代永远撞不上

    private static void SquareTileFrame(On.Terraria.WorldGen.orig_SquareTileFrame orig, int i, int j, bool resetFrame)
    {
        orig(i, j, resetFrame);
        // SquareTileFrame 重框 3×3 邻域，留 1 格余量（火把看它头顶那格，可能出 3×3）。
        TouchDrawArea(i - 2, j - 2, i + 2, j + 2);
    }

    private static void RangeFrame(On.Terraria.WorldGen.orig_RangeFrame orig, int x0, int y0, int x1, int y1)
    {
        orig(x0, y0, x1, y1);
        TouchDrawArea(x0 - 2, y0 - 2, x1 + 2, y1 + 2);
    }

    private static void MapInit(On.Terraria.Map.MapHelper.orig_Initialize orig)
    {
        orig();
        _drawEpoch++; // 换世界：绘制缓存从头再来
    }

    /// <summary>[x0,x1]×[y0,y1] 里的格子变了；区域太大（成片生长）直接作废全世界。</summary>
    private static void TouchDrawArea(int x0, int y0, int x1, int y1)
    {
        if (_drawCache is null) return;

        var ax0 = x0 >> AreaShift;
        var ax1 = x1 >> AreaShift;
        var ay0 = y0 >> AreaShift;
        var ay1 = y1 >> AreaShift;
        if (ax1 < ax0 || ay1 < ay0) return;

        if ((long)(ax1 - ax0 + 1) * (ay1 - ay0 + 1) > 1024)
        {
            _drawEpoch++;
            return;
        }

        var area = _areaEpoch!;
        for (var ax = ax0; ax <= ax1; ax++)
        {
            for (var ay = ay0; ay <= ay1; ay++)
            {
                var h = (uint)ax * 73856093u ^ (uint)ay * 19349663u;
                h ^= h >> 16;
                area[h & (AreaEpochs - 1)]++;
            }
        }
    }

    private static uint AreaEpochAt(int x, int y)
    {
        var h = (uint)(x >> AreaShift) * 73856093u ^ (uint)(y >> AreaShift) * 19349663u;
        h ^= h >> 16;
        return _areaEpoch![h & (AreaEpochs - 1)];
    }

    private static int SlotOf(int x, int y, int type, int tileFrame, int inFx, int inFy)
    {
        var h = (uint)x * 73856093u ^ (uint)y * 19349663u ^ (uint)type * 83492791u ^
                (uint)tileFrame * 2654435761u ^ (uint)inFx * 40503u ^ (uint)inFy * 2246822519u;
        h ^= h >> 15;
        return (int)(h & (DrawCacheSlots - 1));
    }

    private static bool TryGetDraw(int x, int y, int type, int tileFrame,
                                   int inFx, int inFy, int inW, int inH, int inTop, int inAx, int inAy, int inEff,
                                   uint epochWorld, uint epochArea, long now,
                                   out int oFx, out int oFy, out int oW, out int oH, out int oTop,
                                   out int oAx, out int oAy, out int oEff)
    {
        oFx = oFy = oW = oH = oTop = oAx = oAy = oEff = 0;

        ref var slot = ref _drawCache![SlotOf(x, y, type, tileFrame, inFx, inFy)];
        if (Interlocked.CompareExchange(ref slot.Busy, 1, 0) != 0) return false;

        try
        {
            var hit = slot.Used && slot.EpochWorld == epochWorld && slot.EpochArea == epochArea &&
                      now - slot.Stamp < DrawCacheTtlMs &&
                      slot.KeyX == x && slot.KeyY == y && slot.KeyType == type && slot.KeyFrame == tileFrame &&
                      slot.InFrameX == inFx && slot.InFrameY == inFy && slot.InWidth == inW &&
                      slot.InHeight == inH && slot.InTop == inTop && slot.InAddFrX == inAx &&
                      slot.InAddFrY == inAy && slot.InEffects == inEff;
            if (!hit) return false;

            oFx = slot.OutFrameX;
            oFy = slot.OutFrameY;
            oW = slot.OutWidth;
            oH = slot.OutHeight;
            oTop = slot.OutTop;
            oAx = slot.OutAddFrX;
            oAy = slot.OutAddFrY;
            oEff = slot.OutEffects;
            return true;
        }
        finally
        {
            Volatile.Write(ref slot.Busy, 0);
        }
    }

    private static void PutDraw(int x, int y, int type, int tileFrame,
                                int inFx, int inFy, int inW, int inH, int inTop, int inAx, int inAy, int inEff,
                                uint epochWorld, uint epochArea, long now)
    {
        ref var slot = ref _drawCache![SlotOf(x, y, type, tileFrame, inFx, inFy)];
        if (Interlocked.CompareExchange(ref slot.Busy, 1, 0) != 0) return;

        try
        {
            slot.KeyX = x;
            slot.KeyY = y;
            slot.KeyType = type;
            slot.KeyFrame = tileFrame;
            slot.InFrameX = inFx;
            slot.InFrameY = inFy;
            slot.InWidth = inW;
            slot.InHeight = inH;
            slot.InTop = inTop;
            slot.InAddFrX = inAx;
            slot.InAddFrY = inAy;
            slot.InEffects = inEff;
            slot.OutFrameX = inFx;
            slot.OutFrameY = inFy;
            slot.OutWidth = inW;
            slot.OutHeight = inH;
            slot.OutTop = inTop;
            slot.OutAddFrX = inAx;
            slot.OutAddFrY = inAy;
            slot.OutEffects = inEff;
            slot.EpochWorld = epochWorld;
            slot.EpochArea = epochArea;
            slot.Stamp = now;
            slot.Used = true;
        }
        finally
        {
            Volatile.Write(ref slot.Busy, 0);
        }
    }

    private static void AnimateTiles(On.Terraria.Main.orig_AnimateTiles orig)
    {
        orig();

        try
        {
            AnimateModTiles();
        }
        catch (Exception e)
        {
            Safe.Once("tile.animate", "[tile] 动画推进异常：" + Safe.Describe(e), true);
        }
    }

    // 原版只推进自己的类型：默认按帧高计数、贴图高环回；作者的 AnimateTile 推进后可再调。
    private static void AnimateModTiles()
    {
        var frames = Main.tileFrame;
        var counters = Main.tileFrameCounter;
        if (frames is null || counters is null) return;

        foreach (var tile in ModContent.GetContent<ModTile>())
        {
            var type = tile.Type;
            if (type < VanillaCounts.Tile || type >= frames.Length || type >= counters.Length) continue;

            var frameHeight = (uint)type < (uint)_animHeights.Length ? _animHeights[type] : 0;
            if (frameHeight <= 0) continue;

            var frame = frames[type];
            var counter = counters[type];

            if (++counter >= frameHeight)
            {
                counter = 0;
                frame++;
            }

            var count = FrameCount(type, frameHeight);
            if (count > 0 && frame >= count) frame = 0;

            tile.AnimateTile(ref frame, ref counter);

            frames[type] = frame;
            counters[type] = counter;
        }
    }

    private static int FrameCount(int type, int frameHeight)
    {
        var count = (uint)type < (uint)_frameCounts.Length ? _frameCounts[type] : 0;
        if (count > 0) return count;

        var texture = TextureAssets.Tile[type]?.Value;
        if (texture is null || texture.Height <= 0) return 0;

        count = texture.Height / frameHeight;
        if (count > 0) _frameCounts[type] = count;
        return count;
    }
}
