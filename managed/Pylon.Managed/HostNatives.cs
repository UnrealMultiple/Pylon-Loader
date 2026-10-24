using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Pylon.Managed
{
    /// <summary>宿主提供的原生库解析：Il2CppInterop 写的是桌面库名 `GameAssembly`，Android 上是 `libil2cpp.so`。</summary>
    internal static class HostNatives
    {
        private static readonly Lazy<IntPtr> Il2CppHandle =
            new(() => NativeLibrary.Load(FindLoadedLibrary("libil2cpp.so") ?? "libil2cpp.so"));

        public static string Il2CppPath { get; } = FindLoadedLibrary("libil2cpp.so");

        public static bool IsIl2CppName(string name) =>
            name is "GameAssembly" or "il2cpp" or "libil2cpp.so" or "libil2cpp";

        public static bool TryResolve(string name, out IntPtr handle)
        {
            handle = IntPtr.Zero;
            if (!IsIl2CppName(name)) return false;
            try
            {
                handle = Il2CppHandle.Value;
                return handle != IntPtr.Zero;
            }
            catch (Exception e)
            {
                Entrypoint.Log("加载 libil2cpp.so 失败：" + PylonHarmony.Flatten(e));
                return false;
            }
        }

        /// <summary>从 /proc/self/maps 找已加载库的真实路径（裸名 dlopen 不一定命中）。</summary>
        public static string FindLoadedLibrary(string libraryName)
        {
            try
            {
                foreach (var line in File.ReadLines("/proc/self/maps"))
                {
                    if (!line.Contains(libraryName, StringComparison.Ordinal)) continue;
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 6 && File.Exists(parts[5])) return parts[5];
                }
            }
            catch (Exception e)
            {
                Entrypoint.Log("读 /proc/self/maps 失败：" + e.Message);
            }
            return null;
        }
    }
}
