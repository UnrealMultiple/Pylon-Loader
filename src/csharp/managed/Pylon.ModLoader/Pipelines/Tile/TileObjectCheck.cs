using Terraria;
using Terraria.ID;

namespace Pylon.ModLoader.Pipelines;
/// <summary>多格家具的完整性检查与整件毁坏（挂在 TileFrameImportant 上：任一格被重框就查整件）。</summary>
internal static class TileObjectCheck
{
    internal static bool Check(int i, int j, int type)
    {
        if (TileID.Sets.Torches[type] || TileID.Sets.Platforms[type]) return false;

        var data = TileTable.Shapes[type];
        if (data is null) return false;
        if (WorldGen.destroyObject) return true;

        var cell = Main.tile[i, j];
        var frameX = cell.frameX;
        var frameY = cell.frameY;
        var style = TilePipeline.StyleOf(cell);

        var partFrameX = frameX % data.CoordinateFullWidth;
        var partFrameY = frameY % data.CoordinateFullHeight;
        var partX = partFrameX / (data.CoordinateWidth + data.CoordinatePadding);
        var partY = 0;
        var remaining = partFrameY;
        var heights = data.CoordinateHeights;
        while (partY + 1 < data.Height && partY < heights.Length && remaining - heights[partY] - data.CoordinatePadding >= 0)
        {
            remaining -= heights[partY] + data.CoordinatePadding;
            partY++;
        }

        var left = i - partX;
        var top = j - partY;
        var whole = true;
        for (var x = left; x < left + data.Width; x++)
        {
            for (var y = top; y < top + data.Height; y++)
            {
                var part = Main.tile[x, y];
                if (!part.active() || part.type != type) whole = false;
            }
        }

        var breakable = TileID.Sets.BreakableWhenPlacing[type];
        TileID.Sets.BreakableWhenPlacing[type] = true;
        bool stay;
        try
        {
            stay = whole && TileObject.CanPlace(left + data.Origin.X, top + data.Origin.Y, type, style, 0, out _,
                false, new Il2CppSystem.Nullable<int>());
        }
        finally
        {
            TileID.Sets.BreakableWhenPlacing[type] = breakable;
        }

        if (stay) return true;

        WorldGen.destroyObject = true;
        try
        {
            if (data.Width > 1 || data.Height > 1)
                WorldGen.KillTile_DropItems(i, j, cell, true);

            for (var x = left; x < left + data.Width; x++)
            {
                for (var y = top; y < top + data.Height; y++)
                {
                    var part = Main.tile[x, y];
                    if (part.active() && part.type == type)
                        WorldGen.KillTile(x, y, false, false, false);
                }
            }

            TileTable.Of(type)?.KillMultiTile(left, top, frameX - partFrameX, frameY - partFrameY);
        }
        finally
        {
            WorldGen.destroyObject = false;
        }

        for (var x = left - 1; x <= left + data.Width; x++)
        {
            for (var y = top - 1; y <= top + data.Height; y++)
                WorldGen.TileFrame(x, y, false, false);
        }

        return true;
    }
}
