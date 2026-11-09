using Terraria;
using Terraria.ObjectData;

namespace Pylon.ModLoader;

/// <summary>
/// Mod 晶塔的 tile entity：作者继承它（空实现也行），
/// 放/拆都通知游戏的晶塔系统立刻刷新列表。
/// </summary>
public class TEModdedPylon : ModTileEntity
{
    /// <summary>只有晶塔左上角那格有效：方块是 ModPylon、frameY == 0、frameX 能被 CoordinateFullWidth 整除。</summary>
    public override bool IsTileValidForEntity(int x, int y)
    {
        var tile = Main.tile[x, y];
        if (!tile.active()) return false;
        if (ModContent.Find(ContentKind.Tile, tile.type) is not ModPylon) return false;

        var data = TileObjectData.GetTileData(tile);
        return data is not null && tile.frameY == 0 && tile.frameX % data.CoordinateFullWidth == 0;
    }

    /// <summary>这实体位置上的晶塔（ModPylon），不是就 null。</summary>
    public ModPylon? TryGetModPylon() => GetModPylonFromCoords(Position.X, Position.Y, out var pylon) ? pylon : null;

    /// <summary>(x, y) 那格的方块是不是 mod 晶塔。</summary>
    public static bool GetModPylonFromCoords(int x, int y, out ModPylon? modPylon)
    {
        var tile = Main.tile[x, y];
        if (tile.active() && ModContent.Find(ContentKind.Tile, tile.type) is ModPylon pylon)
        {
            modPylon = pylon;
            return true;
        }

        modPylon = null;
        return false;
    }

    /// <summary>放置预览的检查钩子（TileObjectData.PlacementHook 用）：返回 0 才能放。</summary>
    public int PlacementPreviewHook_CheckIfCanPlace(int x, int y, int type, int style = 0, int direction = 1, int alternate = 0)
    {
        var pylon = ModContent.Find(ContentKind.Tile, type) as ModPylon;
        return pylon is null || pylon.CanPlacePylon() ? 0 : 1;
    }

    /// <summary>放下即建实体，并让游戏重算晶塔列表。</summary>
    public new int Place(int i, int j)
    {
        var id = base.Place(i, j);
        RequestUpdate();
        return id;
    }

    /// <summary>拆掉即摘实体，并让游戏重算晶塔列表。</summary>
    public new void Kill(int x, int y)
    {
        base.Kill(x, y);
        RequestUpdate();
    }

    /// <summary>单人/服务器才需要通知；客户端等服务器广播。</summary>
    private static void RequestUpdate()
    {
        if (Main.netMode == 1 || Main.gameMenu || Main.PylonSystem is null) return;
        Main.PylonSystem.RequestImmediateUpdate();
    }
}
