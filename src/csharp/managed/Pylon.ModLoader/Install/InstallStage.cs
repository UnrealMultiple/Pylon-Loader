namespace Pylon.ModLoader;

/// <summary>安装阶段。枚举顺序即执行顺序，只用于分组与断言。</summary>
internal enum InstallStage
{
    /// <summary>扩内容表。</summary>
    Layout,

    /// <summary>静态默认值与依赖表长的初始化。</summary>
    Static,

    /// <summary>贴图槽、名字、tooltip。</summary>
    Surface,

    /// <summary>ContentSamples 样例与持久 ID。</summary>
    Samples,

    /// <summary>方块与墙壁的实例表。</summary>
    Tiles,

    /// <summary>Mod 与内容的就绪回调。</summary>
    Mods,

    /// <summary>配方。</summary>
    Recipes,
}
