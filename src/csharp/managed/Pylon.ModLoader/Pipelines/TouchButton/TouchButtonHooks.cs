namespace Pylon.ModLoader;

/// <summary>游戏侧接入的公开门面（UI 启动流程用）：真正的挂钩在 TouchButtonPipeline 里。</summary>
public static class TouchButtonHooks
{
    /// <summary>已挂钩并套用过保存的布局。</summary>
    public static bool Applied => TouchButtonPipeline.Instance.Applied;

    /// <summary>登记安装（游戏 UI 就绪后再真正挂）。</summary>
    public static void RequestInstall(ModLogger logger) => TouchButtonPipeline.Instance.RequestInstall(logger);

    /// <summary>每帧由 UI 驱动：就绪了就挂钩子。</summary>
    public static void TryInstallPending() => TouchButtonPipeline.Instance.TryInstallPending();

    /// <summary>把保存的布局套回去。</summary>
    public static void ApplySaved() => TouchButtonPipeline.Instance.ApplySaved();
}
