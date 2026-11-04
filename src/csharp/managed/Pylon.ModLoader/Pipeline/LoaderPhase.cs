namespace Pylon.ModLoader;

/// <summary>管线的整体阶段。只用于断言与日志，不参与分派。</summary>
internal enum LoaderPhase
{
    /// <summary>还没有 Mod，也没有内容。</summary>
    Idle,

    /// <summary>正在注册内容并装配模块。</summary>
    Registering,

    /// <summary>内容已占号，等游戏表就绪。</summary>
    Installing,

    /// <summary>内容已装进游戏。</summary>
    Ready,
}
