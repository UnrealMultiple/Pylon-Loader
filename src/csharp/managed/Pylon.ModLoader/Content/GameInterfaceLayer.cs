using Terraria.UI;

namespace Pylon.ModLoader;

/// <summary>
/// 界面层：游戏把界面按顺序逐层画，一层返回 false 就停在这一层（后面的层都不画）。
/// 作者通常这样插入：
/// <code>
/// layers.Insert(index, new GameInterfaceLayer("MyMod: Panel", () => { DrawMyPanel(); return true; }));
/// </code>
/// </summary>
public sealed class GameInterfaceLayer
{
    /// <summary>这一层的名字（只用于辨认与日志）。</summary>
    public string Name { get; }

    /// <summary>这一层画在哪种坐标系：<c>Game</c> 跟着世界、<c>UI</c> 固定在屏幕。</summary>
    public InterfaceScaleType ScaleType { get; set; }

    /// <summary>false = 这一层不画（原版层与作者层都可以这样关掉）。</summary>
    public bool Active { get; set; } = true;

    /// <summary>作者层的绘制体；false = 停在这一层。原版层为 null（绘制仍走游戏）。</summary>
    internal Func<bool>? Draw { get; }

    /// <summary>原版层对应的游戏对象指针；作者层为 0（由管线创建外壳）。</summary>
    internal IntPtr Native { get; }

    /// <summary>创建一个作者层。</summary>
    public GameInterfaceLayer(string name, Func<bool> draw, InterfaceScaleType scaleType = InterfaceScaleType.Game)
    {
        Name = name ?? "";
        Draw = draw;
        ScaleType = scaleType;
    }

    internal GameInterfaceLayer(string name, InterfaceScaleType scaleType, IntPtr native)
    {
        Name = name ?? "";
        ScaleType = scaleType;
        Native = native;
    }
}
