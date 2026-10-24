using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Pylon.Mod;

namespace Pylon.Managed
{
    public static class Entrypoint
    {
        private const string LibPylon = "pylon";

        [DllImport(LibPylon, CharSet = CharSet.Ansi)]
        private static extern void PylonMod_Log(string utf8);

        [DllImport(LibPylon)]
        private static extern int PylonMod_ApiVersion();

        internal static void Log(string message)
        {
            try { PylonMod_Log("[managed] " + message); }
            catch { Console.Error.WriteLine("[managed] " + message); }
        }

        public static void Start()
        {
            InstallIl2CppResolver();
            PylonHarmony.Init();
            Log($"CoreCLR 已起：{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");

            var modsDir = Environment.GetEnvironmentVariable("PYLON_MODS_DIR");

            // 框架 UI / ImGui 是加载器能力，先绑宿主上下文：即使没有 Mod，晚阶段也会起菜单与悬浮窗。
            // modsDir 是"用户 Mods 目录 + .pmod 解包缓存根"的列表，框架设置只写第一个根。
            var roots = string.IsNullOrEmpty(modsDir)
                ? Array.Empty<string>()
                : modsDir.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            var hostDir = roots.Length > 0 ? roots[0] : AppContext.BaseDirectory;
            Pylon.ModLoader.Framework.Initialize(
                new ModContext("Pylon", "host", hostDir, PylonHarmony.NewHarmony("pylon.host")));

            if (string.IsNullOrEmpty(modsDir))
            {
                Log("没有 PYLON_MODS_DIR，跳过 Mod 扫描（框架 UI 仍会启动）");
                return;
            }
            ModLoader.Prepare(modsDir);
        }

        public static void StartMods() => ModLoader.RunOnGameThread();

        /// <summary>Il2CppInterop 写的是桌面库名 GameAssembly，Android 上是 libil2cpp.so。</summary>
        private static void InstallIl2CppResolver()
        {
            try
            {
                System.Runtime.InteropServices.NativeLibrary.SetDllImportResolver(
                    typeof(Il2CppInterop.Runtime.IL2CPP).Assembly,
                    (name, _, _) => HostNatives.TryResolve(name, out var handle) ? handle : IntPtr.Zero);
                Log($"GameAssembly → {HostNatives.Il2CppPath ?? "libil2cpp.so"}");
            }
            catch (Exception e)
            {
                Log("装 DllImport 解析器失败：" + PylonHarmony.Flatten(e));
            }
        }
    }
}
