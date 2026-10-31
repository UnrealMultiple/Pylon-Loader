using Il2CppMicrosoft.Xna.Framework;
using Terraria.GameContent;

namespace Pylon.ModLoader;

/// <summary>坐在/躺在哪儿的参数（对应参考里的 TileRestingInfo）。</summary>
public sealed class TileRestingInfo
{
    /// <summary>锚点格子（tile 坐标）。</summary>
    public Vector2 AnchorTilePosition;

    /// <summary>画玩家时的偏移。</summary>
    public Vector2 VisualOffset = Vector2.Zero;

    /// <summary>朝哪边（1 / -1）。</summary>
    public int TargetDirection;

    /// <summary>按朝向再加的偏移。</summary>
    public int DirectionOffset;

    /// <summary>最后再加的偏移。</summary>
    public Vector2 FinalOffset = Vector2.Zero;

    /// <summary>额外座位信息；IsAToilet 为 true 就是马桶。</summary>
    public ExtraSeatInfo ExtraInfo;

    /// <summary>构造参数：锚点格子、可视偏移、朝向、朝向偏移、末尾偏移。</summary>
    public TileRestingInfo(int anchorX, int anchorY, Vector2 visualOffset, int targetDirection,
                           int directionOffset = 0, Vector2? finalOffset = null)
    {
        AnchorTilePosition = new Vector2(anchorX, anchorY);
        VisualOffset = visualOffset;
        TargetDirection = targetDirection;
        DirectionOffset = directionOffset;
        FinalOffset = finalOffset ?? Vector2.Zero;
        ExtraInfo = new ExtraSeatInfo();
    }
}
