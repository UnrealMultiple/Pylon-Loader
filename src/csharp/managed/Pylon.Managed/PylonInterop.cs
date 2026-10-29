// 层：宿主 —— Il2CppInterop 启动（不装 Harmony）。
//
// 以前这里顺手装 HarmonySupport，现在 hook 只有 Pylon.HookRuntime 一套：
// interop 自己的内部钩子（Class.FromName / 泛型方法解析等）用我们实现的 IDetourProvider
// （PylonDetourProvider，走内核 Dobby），不需要 0Harmony。
using System;
using System.Linq;
using System.Reflection;

namespace Pylon.Managed
{
    internal static class PylonInterop
    {
        private const string RuntimeAssembly = "Il2CppInterop.Runtime";
        private const string RuntimeTypeName = "Il2CppInterop.Runtime.Startup.Il2CppInteropRuntime";
        private const string ConfigTypeName = "Il2CppInterop.Runtime.Startup.RuntimeConfiguration";

        private static bool _ready;

        /// <summary>起 Il2CppInterop 运行时（幂等）：版本探测 + 内部 detour 的 provider。</summary>
        public static void Init()
        {
            if (_ready) return;
            try
            {
                var runtimeType = LoadType(RuntimeAssembly, RuntimeTypeName);
                var configType = LoadType(RuntimeAssembly, ConfigTypeName);
                if (runtimeType == null || configType == null)
                {
                    Entrypoint.Log("Il2CppInterop 运行时缺件，跳过启动（代理类型仍可直接用）");
                    return;
                }

                var config = Activator.CreateInstance(configType);
                configType.GetProperty("UnityVersion")?.SetValue(config, GameUnityVersion());
                configType.GetProperty("DetourProvider")?.SetValue(config, new PylonDetourProvider());

                // Instance 在未初始化时会抛异常，所以 try 住再退回 Create。
                object runtime = null;
                try
                {
                    runtime = runtimeType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)
                        ?.GetValue(null);
                }
                catch
                {
                    runtime = null;
                }
                runtime ??= runtimeType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)
                    ?.Invoke(null, new[] { config });
                if (runtime == null)
                {
                    Entrypoint.Log("拿不到 Il2CppInteropRuntime 实例，跳过启动");
                    return;
                }

                runtimeType.GetMethod("Start", BindingFlags.Public | BindingFlags.Instance)?.Invoke(runtime, null);
                _ready = true;
                Entrypoint.Log("Il2CppInterop 运行时已启动（detour 走后端 Dobby，不带 Harmony）");
            }
            catch (Exception e)
            {
                Entrypoint.Log("启动 Il2CppInterop 运行时失败：" + Entrypoint.Flatten(e));
            }
        }

        private static Version GameUnityVersion()
        {
            var raw = Environment.GetEnvironmentVariable("PYLON_UNITY_VERSION");
            return Version.TryParse(raw, out var parsed) ? parsed : new Version(2021, 3, 56);
        }

        /// <summary>按"程序集名 + 类型全名"取类型；必要时主动加载（TPA 里有 ≠ 已加载）。</summary>
        private static Type LoadType(string assemblyName, string typeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name != assemblyName) continue;
                var type = assembly.GetType(typeName, throwOnError: false);
                if (type != null) return type;
            }
            try
            {
                return Assembly.Load(new AssemblyName(assemblyName)).GetType(typeName, throwOnError: false);
            }
            catch (Exception e)
            {
                Entrypoint.Log($"加载 {assemblyName} 失败：{e.GetType().Name}: {e.Message}");
                return null;
            }
        }
    }
}
