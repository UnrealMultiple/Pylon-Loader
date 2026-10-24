using System.Runtime.InteropServices;

namespace Pylon.Managed.Interop;

/// <summary>把 Unity 的 libil2cpp.so 递给写死桌面库名的 Il2CppInterop。</summary>
internal static class HostNatives
{
    private static readonly string? Il2CppLibraryPath = FindLoadedLibrary("libil2cpp.so");

    private static readonly Lazy<IntPtr> Il2CppHandle =
        new(() => NativeLibrary.Load(Il2CppLibraryPath ?? "libil2cpp.so"));

    /// <summary>已加载的 libil2cpp.so 路径；找不到时为 null。</summary>
    public static string? Path => Il2CppLibraryPath;

    public static bool IsIl2CppName(string name) =>
        name is "GameAssembly" or "il2cpp" or "libil2cpp.so" or "libil2cpp";

    public static bool TryResolve(string name, out IntPtr handle)
    {
        handle = IntPtr.Zero;
        if (!IsIl2CppName(name)) return false;

        try
        {
            handle = Il2CppHandle.Value;
        }
        catch (Exception e)
        {
            HostLog.Write("加载 libil2cpp.so 失败：" + HostLog.Describe(e));
        }
        return handle != IntPtr.Zero;
    }

    /// <summary>从 /proc/self/maps 找已加载库的真实路径（裸名 dlopen 不一定命中）。</summary>
    private static string? FindLoadedLibrary(string libraryName)
    {
        try
        {
            foreach (var line in File.ReadLines("/proc/self/maps"))
            {
                if (!line.Contains(libraryName, StringComparison.Ordinal)) continue;
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 6 && File.Exists(parts[5])) return parts[5];
            }
        }
        catch (Exception e)
        {
            HostLog.Write("读 /proc/self/maps 失败：" + e.Message);
        }
        return null;
    }
}
