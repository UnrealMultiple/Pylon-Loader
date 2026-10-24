namespace Pylon.ModLoader.Content;
/// <summary>
/// 一个玩家这一帧的场景效果快照：AnyActive 说有没有激活的效果，各槽是"赢家声明的值 + 它的优先级"，没声明的槽是 -1；
/// 每帧在场景效果管线里重建，PlayerTml.CurrentSceneEffect 返回它。
/// </summary>
public sealed class SceneEffectInstance
{
    /// <summary>一个槽：Value 是声明的内容（水样式号 / 音乐号 / …），Priority 是声明者的优先级。</summary>
    public struct PrioritizedPair
    {
        public static readonly PrioritizedPair Default = new() { Value = -1 };

        public int Value;

        public int Priority;
    }

    public bool AnyActive;

    public PrioritizedPair WaterStyle = PrioritizedPair.Default;

    public PrioritizedPair UndergroundBackground = PrioritizedPair.Default;

    public PrioritizedPair SurfaceBackground = PrioritizedPair.Default;

    public PrioritizedPair Music = PrioritizedPair.Default;

    public PrioritizedPair BiomeTorchItemType = PrioritizedPair.Default;

    public PrioritizedPair BiomeCampfireItemType = PrioritizedPair.Default;

    public string? MapBackground;

    public ModSceneEffect? MapBackgroundSceneEffect;

    public Terraria.Graphics.Capture.CaptureBiome.TileColorStyle TileColorStyle
        = Terraria.Graphics.Capture.CaptureBiome.TileColorStyle.Normal;
}
