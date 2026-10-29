#nullable disable
// 层：RuntimeDetour —— 原生 detour 的抽象后端。
//
// 引擎只负责"生成桩、维护链、调 handler"；真正改写内存的那一步交给宿主：
//   Pylon 实现 DetourBackend，把 Apply/Remove 接到内核的 Dobby 导出
//   （PylonMod_HookApply / PylonMod_HookRemove）。
// 这样 RuntimeDetour 不依赖任何具体平台，桌面探针也能换一个假后端跑通整条链。
using System;

namespace Il2CppInterop.RuntimeDetour
{
    /// <summary>原生 detour 后端：宿主提供（Pylon → 内核 Dobby）。</summary>
    public abstract class DetourBackend
    {
        /// <summary>进程内唯一的后端；宿主启动时必须设置。</summary>
        public static DetourBackend Current { get; set; }

        /// <summary>后端是否可用（Dobby 未加载时返回 false，不抛）。</summary>
        public abstract bool IsAvailable { get; }

        /// <summary>
        /// 把 <paramref name="target"/> 跳到 <paramref name="detour"/>，通过
        /// <paramref name="trampoline"/> 交回可调用的原函数入口。
        /// </summary>
        /// <returns>0 成功；非 0 由 <see cref="Hook"/> 转成可读异常。</returns>
        public abstract int Apply(IntPtr target, IntPtr detour, out IntPtr trampoline);

        /// <summary>撤销；没挂过返回 -1，不抛。</summary>
        public abstract int Remove(IntPtr target);
    }
}
