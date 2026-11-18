using Terraria.UI;

namespace Pylon.ModLoader.Content;

public delegate bool GameInterfaceDrawMethod();

/// <summary>
/// 界面层：游戏把界面按顺序逐层画，一层返回 false 就停在这一层（后面的层都不画）。
/// 原版层由管线包装交给作者；作者层通常用 <see cref="LegacyGameInterfaceLayer"/>：
/// <code>
/// layers.Insert(index, new LegacyGameInterfaceLayer("MyMod: Panel", () => { DrawMyPanel(); return true; }));
/// </code>
/// 也可以继承本类并重写 <see cref="DrawSelf"/>。
/// </summary>
public class GameInterfaceLayer
{
    internal IntPtr Native { get; }

    public string Name { get; }

    public InterfaceScaleType ScaleType { get; set; }

    public bool Active { get; set; } = true;

    public GameInterfaceLayer(string name, InterfaceScaleType scaleType)
    {
        Name = name ?? "";
        ScaleType = scaleType;
    }

    internal GameInterfaceLayer(string name, InterfaceScaleType scaleType, IntPtr native)
    {
        Name = name ?? "";
        ScaleType = scaleType;
        Native = native;
    }

    public bool Draw() => !Active || DrawSelf();

    protected virtual bool DrawSelf() => true;
}

public class LegacyGameInterfaceLayer : GameInterfaceLayer
{
    private readonly GameInterfaceDrawMethod _method;

    public LegacyGameInterfaceLayer(string name, GameInterfaceDrawMethod drawMethod, InterfaceScaleType scaleType = InterfaceScaleType.Game)
        : base(name, scaleType)
    {
        _method = drawMethod;
    }

    protected override bool DrawSelf() => _method();
}
