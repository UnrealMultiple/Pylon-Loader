using Terraria;

namespace Pylon.ModLoader;

/// <summary>TileObjectData 的静态扩展面：按格反查样式与左上角（基线 TileObjectData.TML.cs 同名同语义）。</summary>
public static class TileObjectDataTml
{
    /// <summary>这一格物件的样式号；非 mod 逻辑照原版算法，无效 = -1。</summary>
    public static int GetTileStyle(Tile getTile) =>
        getTile.active() ? TilePipeline.StyleOf(getTile) : -1;

    /// <summary>(i, j) 所在多格物件的左上角；不是物件 = 原样返回。</summary>
    public static Terraria.DataStructures.Point16 TopLeft(int i, int j)
    {
        if (Part(Main.tile[i, j], out var partX, out var partY)) return new(i - partX, j - partY);
        return new Terraria.DataStructures.Point16(i, j);
    }

    /// <summary>(i, j) 所在多格物件的左上角。</summary>
    public static Terraria.DataStructures.Point16 TopLeft(Terraria.DataStructures.Point16 position) =>
        TopLeft(position.X, position.Y);

    /// <summary>这一格是不是物件的左上角。</summary>
    public static bool IsTopLeft(Tile tile) => !Part(tile, out var partX, out var partY) || (partX == 0 && partY == 0);

    /// <summary>(i, j) 是不是物件的左上角。</summary>
    public static bool IsTopLeft(int i, int j) => IsTopLeft(Main.tile[i, j]);

    private static bool Part(Tile tile, out int partX, out int partY)
    {
        partX = partY = 0;
        if (!tile.active()) return false;

        var data = Terraria.ObjectData.TileObjectData.GetTileData(tile.type, 0);
        if (data is null || data.CoordinateFullWidth <= 0 || data.CoordinateFullHeight <= 0) return false;

        var padding = data.CoordinatePadding;
        partX = tile.frameX % data.CoordinateFullWidth / Math.Max(1, data.CoordinateWidth + padding);
        var heights = data.CoordinateHeights;
        var rest = tile.frameY % data.CoordinateFullHeight;
        while (partY + 1 < data.Height && partY < heights.Length && rest - heights[partY] - padding >= 0)
        {
            rest -= heights[partY] + padding;
            partY++;
        }

        return true;
    }
}
