namespace Pylon.ModLoader;

/// <summary>游戏侧内容注入的状态。</summary>
public static class GameContent
{
    /// <summary>注入是否已完成（内容类型真的被游戏认识了）。</summary>
    public static bool Installed { get; internal set; }

    /// <summary>已登记待注入的内容数量。</summary>
    public static int Pending { get; internal set; }

    /// <summary>注入不可用时的原因（写日志用；null 表示没失败）。</summary>
    public static string? UnavailableReason { get; internal set; }
}
