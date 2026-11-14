using System;
using System.Runtime.InteropServices;

namespace Pylon.NaviteAPI.Host;

/// <summary>内核 SystemStages 中体桥出口：桥体/分派驻留内核（mod/SystemBridges.cpp），阶段表在 mod/SystemBridges.h。</summary>
internal static unsafe partial class SystemBridgeNative
{
    /// <summary>指纹核对（16 字节）+ 挂桥 + 跳板登记（0 成功；-1 参数 / -2 指纹不符 / -3 挂钩失败）。</summary>
    [LibraryImport("pylon", EntryPoint = "PylonMod_SystemBridgeInstall")]
    internal static partial int Install(int stage, IntPtr body, int byteOffset, uint[] expected,
        out IntPtr trampoline);

    /// <summary>注册阶段回调（每阶段一个槽；null = 退订）。value 随阶段而定（时钟 double / 染料商 DyeFrame），见 ModApi.h。</summary>
    [LibraryImport("pylon", EntryPoint = "PylonMod_SystemBridgeSubscribe")]
    internal static partial void Subscribe(int stage, delegate* unmanaged<int, IntPtr, void> callback);
}
