using System;

namespace Pylon.ModLoader
{
    /// <summary>游戏侧注入的接缝：类型表扩容、编译期上限改写、原版方法 hook、贴图与名字写入、旁路存档。</summary>
    public static class GameContent
    {
        /// <summary>注入是否已完成（内容类型真的被游戏认识了）。</summary>
        public static bool Installed { get; internal set; }

        /// <summary>已登记待注入的内容数量。</summary>
        public static int Pending { get; internal set; }

        /// <summary>注入不可用时的原因（写日志用；null = 没失败）。</summary>
        public static string UnavailableReason { get; internal set; }
    }
}
