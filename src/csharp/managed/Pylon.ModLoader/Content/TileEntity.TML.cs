using Terraria;
using Terraria.ObjectData;

namespace Pylon.ModLoader;

/// <summary>游戏 TileEntity 的静态扩展面：TryGet 按格取 mod 实体（基线 TileEntity.TML.cs 同名同语义）。</summary>
public static class TileEntityTml
{
    /// <summary>(i, j) 所在物体（自动折算到左上角）的 mod 实体；类型不符 = 取不到。</summary>
    public static bool TryGet<T>(int i, int j, out T tileEntity) where T : ModTileEntity
    {
        return TryGet(TileObjectDataTml.TopLeft(i, j), out tileEntity);
    }

    /// <summary>左上角格的 mod 实体；类型不符 = 取不到。</summary>
    public static bool TryGet<T>(Terraria.DataStructures.Point16 position, out T tileEntity) where T : ModTileEntity
    {
        if (TileEntityBinding.At(position.X, position.Y) is T found)
        {
            tileEntity = found;
            return true;
        }

        tileEntity = null!;
        return false;
    }
}
