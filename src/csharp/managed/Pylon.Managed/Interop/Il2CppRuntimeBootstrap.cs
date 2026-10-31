using Il2CppInterop.Runtime.Startup;

namespace Pylon.Managed.Interop;

/// <summary>启动 Il2CppInterop 运行时：版本探测 + 内部 detour 的 provider。</summary>
internal static class Il2CppRuntimeBootstrap
{
    private const string UnityVersionVariable = "PYLON_UNITY_VERSION";
    private static readonly Version DefaultUnityVersion = new(2021, 3, 56);

    /// <summary>失败只记日志：代理类型仍可直接使用，游戏不受影响。</summary>
    public static void Start()
    {
        try
        {
            var runtime = Il2CppInteropRuntime.Create(new RuntimeConfiguration
            {
                UnityVersion = ResolveUnityVersion(),
                DetourProvider = new PylonDetourProvider(),
            });
            runtime.Start();
            HostLog.Write("Il2CppInterop 运行时已启动（detour 走后端 Dobby，不带 Harmony）");
        }
        catch (Exception e)
        {
            HostLog.Write("启动 Il2CppInterop 运行时失败：" + HostLog.Describe(e));
        }
    }

    private static Version ResolveUnityVersion() =>
        Version.TryParse(Environment.GetEnvironmentVariable(UnityVersionVariable), out var parsed)
            ? parsed
            : DefaultUnityVersion;
}
