using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace Pylon.Managed
{
    internal static class PylonHarmony
    {
        private const string RuntimeAssembly = "Il2CppInterop.Runtime";
        private const string RuntimeTypeName = "Il2CppInterop.Runtime.Startup.Il2CppInteropRuntime";
        private const string ConfigTypeName = "Il2CppInterop.Runtime.Startup.RuntimeConfiguration";
        private const string SupportAssembly = "Il2CppInterop.HarmonySupport";
        private const string SupportTypeName = "Il2CppInterop.HarmonySupport.HarmonySupport";

        private static bool _ready;

        /// <summary>装上 Harmony 支持（幂等）。</summary>
        public static void Init()
        {
            if (_ready) return;
            try
            {
                var runtimeType = LoadType(RuntimeAssembly, RuntimeTypeName);
                var configType = LoadType(RuntimeAssembly, ConfigTypeName);
                var supportType = LoadType(SupportAssembly, SupportTypeName);
                if (runtimeType == null || configType == null || supportType == null)
                {
                    Entrypoint.Log("Harmony 支持缺件，跳过（Mod 能加载但不能打补丁）");
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
                    ?.Invoke(null, [config]);
                if (runtime == null)
                {
                    Entrypoint.Log("拿不到 Il2CppInteropRuntime 实例，跳过 Harmony 支持");
                    return;
                }

                // 泛型扩展方法：AddHarmonySupport<T>(T host)
                supportType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "AddHarmonySupport" && m.IsGenericMethodDefinition)
                    ?.MakeGenericMethod(runtimeType).Invoke(null, new[] { runtime });
                runtimeType.GetMethod("Start", BindingFlags.Public | BindingFlags.Instance)
                    ?.Invoke(runtime, null);

                _ready = true;
                Entrypoint.Log($"Harmony 支持已装（0Harmony {typeof(Harmony).Assembly.GetName().Version}）");
            }
            catch (Exception e)
            {
                Entrypoint.Log("装 Harmony 支持失败：" + Flatten(e));
            }
        }

        /// <summary>新建一个属于某个 Mod 的 Harmony 实例（补丁由 Mod 自己显式打）。</summary>
        public static Harmony NewHarmony(string modId) => new Harmony("pylon." + modId);

        /// <summary>清点某个 Harmony 实例**实际打上的**补丁（目标方法 + 补丁数量）。</summary>
        public static string DescribePatches(Harmony harmony)
        {
            try
            {
                var entries = new List<string>();
                foreach (var method in harmony.GetPatchedMethods())
                {
                    var info = Harmony.GetPatchInfo(method);
                    if (info == null) continue;
                    var count = info.Prefixes.Count + info.Postfixes.Count + info.Transpilers.Count;
                    entries.Add($"{method.DeclaringType?.Name}::{method.Name}({count})");
                }
                return entries.Count == 0 ? "未打任何补丁" : string.Join("；", entries);
            }
            catch (Exception e)
            {
                return "清点补丁失败：" + Flatten(e);
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

        internal static string Flatten(Exception e)
        {
            var text = new System.Text.StringBuilder();
            var current = e;
            for (var depth = 0; current != null && depth < 6; depth++, current = current.InnerException)
            {
                if (depth > 0) text.Append(" ← ");
                text.Append(current.GetType().Name).Append(": ").Append(current.Message);
            }
            return text.ToString();
        }
    }
}
