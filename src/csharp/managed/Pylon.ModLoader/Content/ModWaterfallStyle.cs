namespace Pylon.ModLoader;

/// <summary>自定义瀑布样式：类同名 PNG 做贴图；占游戏 28 个之后的一号，由 ModWaterStyle.ChooseWaterfallStyle 选中。</summary>
public abstract class ModWaterfallStyle : ModType
{
    /// <summary>占用的瀑布号（管线发号：游戏 28 个之后）。</summary>
    public int Slot => Type;

    /// <summary>每帧一次：给这风格在屏幕上的每个瀑布点加光（Lighting.AddLight）。</summary>
    public virtual void AddLight(int i, int j) { }

    public virtual void ColorMultiplier(ref float r, ref float g, ref float b, float a) { }

    /// <summary>只放共享数值，不要碰游戏对象。</summary>
    public override void SetStaticDefaults() { }
}

// 未移植 register：桥接入口（实例化 + WaterfallStyleLoader.Add + Safe.Run）；Pylon 的注册由 ModType 自动发现，
// 编号分配在管线里。
