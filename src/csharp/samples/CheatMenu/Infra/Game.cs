using Terraria;

namespace CheatMenu.Infra;

/// <summary>游戏对象的安全入口：边界、空引用、菜单态与图格头位都在这里收口。</summary>
internal static class Game
{
    public static bool InWorld => !Main.gameMenu;

    public static Player LocalPlayer
    {
        get
        {
            if (Main.gameMenu) return null;
            var index = Main.myPlayer;
            var players = Main.player;
            if (players is null || index < 0 || index >= players.Length) return null;
            var player = players[index];
            return player is { active: true } ? player : null;
        }
    }

    public static bool IsLocal(Player player) =>
        player is not null && !Main.gameMenu && player.whoAmI == Main.myPlayer;

    /// <summary>图格扁平下标：原版 TileData 的静态方法都按 <c>width * y + x</c> 取。</summary>
    public static int TileIndex(int x, int y)
    {
        var tile = Main.tile;
        if (tile is null) return -1;
        var width = tile._width;
        if (width <= 0) return -1;
        return width * y + x;
    }

    /// <summary>与 TileData::active 同义：SHeader 位 0x60 == 0x20。</summary>
    public static bool TileActive(int x, int y)
    {
        var index = TileIndex(x, y);
        if (index < 0) return false;
        var header = Terraria.TileData.GetSHeader(index);
        return (header & 0x60) == 0x20;
    }

    /// <summary>与 TileData::inActive 同义：SHeader 第 6 位。</summary>
    public static bool TileInActive(int x, int y)
    {
        var index = TileIndex(x, y);
        if (index < 0) return false;
        var header = Terraria.TileData.GetSHeader(index);
        return ((header >> 6) & 1) != 0;
    }

    public static int TileType(int x, int y)
    {
        var index = TileIndex(x, y);
        return index < 0 ? -1 : Terraria.TileData.GetType(index);
    }
}