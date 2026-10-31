using Pylon.Managed.Hosting;
using Pylon.Mod;
using Pylon.ModLoader;

namespace Pylon.Managed.Mods;

/// <summary>Mod 生命周期：发现（引导线程）、加载（游戏线程）、内容就绪回调（游戏线程）。</summary>
internal static class ModPipeline
{
    private static ModCatalog? _catalog;

    /// <summary>引导线程：扫目录、读清单、加载程序集、发现 Mod 类型，不执行任何 Mod 代码。</summary>
    public static void Prepare(IReadOnlyList<string> roots) => _catalog = ModCatalog.Discover(roots);

    /// <summary>游戏线程：按依赖顺序加载，单个失败只停用它自己。</summary>
    public static void LoadAll()
    {
        // 0 个 Mod 也必须继续：框架 UI / ImGui 不依赖 Mod，晚阶段探测要照常武装。
        if (_catalog is null || _catalog.Ordered.Count == 0)
            HostLog.Write("没有 Mod 需要加载（框架 UI 仍会启动）");

        var succeeded = 0;
        var failed = 0;
        foreach (var entry in _catalog?.Ordered ?? [])
        {
            if (!entry.Enabled)
            {
                HostLog.Write($"跳过 {entry.Id}（已停用）");
                continue;
            }

            if (entry.Error is { } error)
            {
                HostLog.Write($"跳过 {entry.Id}（{error}）");
                failed++;
                continue;
            }

            try
            {
                Load(entry);
                succeeded++;
            }
            catch (Exception e)
            {
                HostLog.Failure($"✗ {entry.Id} 加载失败（已停用）", e);
                failed++;
            }
        }

        HostLog.Write($"Mod 加载完成：成功 {succeeded}，失败/跳过 {failed}");

        // 早阶段到此为止：内容注入与更晚的 hook 等游戏把表建好。无条件武装：没有 Mod 时它负责触发框架 UI。
        GameReadyWatcher.Arm();
    }

    /// <summary>晚阶段：先起框架 UI，再把更晚的注册时机交给每个 Mod。</summary>
    public static void RunContentReady()
    {
        // 框架自己的 UI（ImGui 悬浮窗、游戏配置屏、触屏守卫）与 Mod 数量无关。
        Framework.EnsureContentReady();

        foreach (var entry in _catalog?.Ordered ?? [])
        {
            if (entry.Instance is not IPylonMod mod || entry.Context is null) continue;
            try
            {
                mod.OnContentReady(entry.Context);
            }
            catch (Exception e)
            {
                HostLog.Failure($"✗ {entry.Id} 延迟阶段失败", e);
            }
        }
        HostLog.Write("延迟注册阶段结束");
    }

    private static void Load(ModEntry entry)
    {
        var type = entry.ModType ?? throw new InvalidOperationException("Mod 类型未发现");

        object instance;
        try
        {
            instance = Activator.CreateInstance(type)!;
        }
        catch (Exception e)
        {
            throw new InvalidOperationException(
                $"实例化失败（{type.FullName} 需要公开的无参构造函数）", e);
        }

        entry.Instance = instance;
        entry.Context ??= new ModContext(entry.Id, entry.Version, entry.Directory);

        ((IPylonMod)instance).Load(entry.Context);

        // 挂上哪些钩子要看得见：否则"钩子没挂上"与"目标没被调用"无法区分。
        HostLog.Write($"  {entry.Id} 钩子：{Il2CppInterop.RuntimeDetour.HookManager.Describe()}");
        HostLog.Write($"✓ {entry.Id} v{entry.Version} → {type.FullName}.Load(context)");
    }
}
