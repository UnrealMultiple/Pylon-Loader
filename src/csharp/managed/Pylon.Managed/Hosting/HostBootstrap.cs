using System.Runtime.InteropServices;
using Pylon.Managed.Interop;
using Pylon.Managed.Mods;
using Pylon.ModLoader;

namespace Pylon.Managed.Hosting;

/// <summary>宿主启动编排：原生解析 → hook 引擎 → Il2CppInterop → 框架宿主 → 扫描 Mod。</summary>
internal static class HostBootstrap
{
    private const string ModsDirectoryVariable = "PYLON_MODS_DIR";

    public static void Start()
    {
        CrashGuard.Install();
        InstallIl2CppLibraryResolver();
        HookEngine.Attach();
        Il2CppRuntimeBootstrap.Start();
        HostLog.Write($"CoreCLR 已起：{RuntimeInformation.FrameworkDescription}");

        // 框架 UI / ImGui 是加载器能力：没有 Mod 时也要起。
        var roots = ParseRoots(Environment.GetEnvironmentVariable(ModsDirectoryVariable));
        var hostDirectory = roots.Length > 0 ? roots[0] : AppContext.BaseDirectory;
        Framework.Initialize(new ModContext("Pylon", "host", hostDirectory));

        if (roots.Length == 0)
        {
            HostLog.Write($"没有 {ModsDirectoryVariable}，跳过 Mod 扫描（框架 UI 仍会启动）");
            return;
        }

        ModPipeline.Prepare(roots);
    }

    /// <summary>Il2CppInterop 写的是桌面库名 GameAssembly，Android 上是 libil2cpp.so。</summary>
    private static void InstallIl2CppLibraryResolver()
    {
        try
        {
            NativeLibrary.SetDllImportResolver(
                typeof(Il2CppInterop.Runtime.IL2CPP).Assembly,
                (name, _, _) => HostNatives.TryResolve(name, out var handle) ? handle : IntPtr.Zero);
            HostLog.Write($"GameAssembly → {HostNatives.Path ?? "libil2cpp.so"}");
        }
        catch (Exception e)
        {
            HostLog.Write("安装 DllImport 解析器失败：" + HostLog.Describe(e));
        }
    }

    /// <summary>Mod 根目录列表：用户 Mods 目录 + .pmod 解包缓存根，用 Path.PathSeparator 分隔。</summary>
    private static string[] ParseRoots(string? modsDirectory) =>
        string.IsNullOrEmpty(modsDirectory)
            ? []
            : modsDirectory
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
}
