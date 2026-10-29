using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Pylon.Mod;

namespace Pylon.Managed;

/// <summary>Mod 的发现 / 依赖排序 / 加载 / 失败隔离。</summary>
internal static class ModLoader
{
    private sealed class ModEntry
    {
        public string Id;
        public string Version;
        public string AssemblyPath;   // 主程序集绝对路径
        public List<string> Dependencies = new();
        public bool Enabled = true;
        public ModLoadContext Context;
        public Assembly Assembly;
        public Type ModType;          // 实现了 IPylonMod 的类型
        public object Instance;       // 宿主持有实例（Mod 的状态就存在这里）
        public string LoadError;
    }

    private static readonly List<ModEntry> Mods = new();

    /// <summary>引导线程：扫目录、读清单、加载程序集、发现 Mod 类型（**不执行**任何 Mod 代码）。</summary>
    public static void Prepare(string modsDir)
    {
        Mods.Clear();

        // 宿主把"用户 Mods 目录 + .pmod 解包缓存根"用 Path.PathSeparator 拼在一起传进来。
        // 解包缓存根下面每个子目录就是一个包内的 Mod 目录，扫描规则与用户目录完全一致。
        var roots = (modsDir ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (roots.Length == 0)
        {
            Entrypoint.Log("没有可扫描的 Mod 目录");
            return;
        }

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
            {
                Entrypoint.Log($"Mod 目录不存在：{root}");
                continue;
            }

            foreach (var dir in Directory.GetDirectories(root))
            {
                if (Path.GetFileName(dir) == ".pylon") continue;
                var dll = FindModDll(dir);
                if (dll != null) Mods.Add(ReadManifest(dll, Path.GetFileName(dir)));
            }
            // 松散放在根下的 dll 也算一个 Mod（省事的用法；缓存根里一般没有）
            foreach (var dll in Directory.GetFiles(root, "*.dll"))
            {
                Mods.Add(ReadManifest(dll, Path.GetFileNameWithoutExtension(dll)));
            }
        }

        // 选择文件永远放用户 Mods 目录（roots[0]），包解包缓存也读同一份。
        ApplySelection(roots.FirstOrDefault(Directory.Exists) ?? roots[0]);

        Entrypoint.Log($"Mod 扫描：{string.Join(" + ", roots)} → {Mods.Count} 个" +
                       $"（宿主提供 {HostAssemblies.Count} 个程序集，Mod 不可遮蔽）");
        foreach (var mod in Mods)
        {
            var context = mod.Context != null ? $" [{mod.Context.Name}]" : "";
            var note = mod.LoadError != null
                ? $"（停用：{mod.LoadError}）"
                : mod.Enabled ? "" : "（enabled=false）";
            Entrypoint.Log($"  · {mod.Id} v{mod.Version}{context}{note}");
        }
    }

    /// <summary>启动器写下的勾选结果（Mods/.pylon/selection.json），优先于各 Mod 自己的 manifest enabled；不直接改 manifest，避免踩掉 id / version / dependencies。</summary>
    private static void ApplySelection(string modsDir)
    {
        var path = Path.Combine(modsDir, ".pylon", "selection.json");
        if (!File.Exists(path)) return;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("enabled", out var enabled) ||
                enabled.ValueKind != JsonValueKind.Object)
                return;

            var byId = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in enabled.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.True ||
                    property.Value.ValueKind == JsonValueKind.False)
                    byId[property.Name] = property.Value.GetBoolean();
            }

            var applied = 0;
            foreach (var mod in Mods)
            {
                if (!byId.TryGetValue(mod.Id, out var value)) continue;
                mod.Enabled = value;
                applied++;
            }
            Entrypoint.Log($"Mod 选择：{path}（覆盖 {applied} 个）");
        }
        catch (Exception e)
        {
            // 选择文件坏掉不能拖垮启动：按各 Mod 清单的 enabled 继续。
            Entrypoint.Log($"Mod 选择文件解析失败（忽略）：{e.Message}");
        }
    }

    /// <summary>游戏线程：按依赖顺序加载，单个失败只停用它自己。</summary>
    public static void RunOnGameThread()
    {
        // 0 个 Mod 也必须继续：框架 UI / ImGui 不依赖 Mod，晚阶段探测要照常武装。
        if (Mods.Count == 0) Entrypoint.Log("没有 Mod 需要加载（框架 UI 仍会启动）");

        int ok = 0, failed = 0;
        foreach (var mod in SortByDependencies())
        {
            if (!mod.Enabled)
            {
                Entrypoint.Log($"跳过 {mod.Id}（已停用）");
                continue;
            }
            if (mod.LoadError != null)
            {
                Entrypoint.Log($"跳过 {mod.Id}（{mod.LoadError}）");
                failed++;
                continue;
            }

            bool loaded;
            try
            {
                loaded = Load(mod);
                Console.Out.Flush();
            }
            catch (Exception e)
            {
                // 带堆栈：只打类型+消息时，"谁在里面空引用了"完全看不出来。
                Entrypoint.Log($"✗ {mod.Id} 加载失败（已停用）：{Entrypoint.Flatten(e)}");
                if (e.StackTrace != null) Entrypoint.Log(e.StackTrace.TrimEnd());
                loaded = false;
            }
            if (loaded) ok++; else failed++;
        }
        Entrypoint.Log($"Mod 加载完成：成功 {ok}，失败/跳过 {failed}");

        // 早阶段到此为止：内容注入与"更晚的 hook"要等游戏把自己的表建好（见 GameReadyWatcher）。
        // 无条件武装：没有 Mod 时它负责触发框架 UI（ImGui / 菜单），不能因为没有 Mod 就跳过。
        GameReadyWatcher.Arm();
    }

    /// <summary>延迟阶段（游戏线程，游戏内容就绪后一次）：先起框架 UI，再把更晚的注册时机交给每个 Mod。</summary>
    public static void RunContentReadyOnGameThread()
    {
        // 框架自己的 UI（ImGui 悬浮窗、游戏配置屏、触屏守卫）与 Mod 数量无关。
        Pylon.ModLoader.Framework.EnsureContentReady();

        foreach (var mod in SortByDependencies())
        {
            if (mod.Instance == null || mod.LoadError != null) continue;
            try
            {
                var context = new ModContext(mod.Id, mod.Version,
                    Path.GetDirectoryName(mod.AssemblyPath) ?? ".");
                ((IPylonMod)mod.Instance).OnContentReady(context);
                Console.Out.Flush();
            }
            catch (Exception e)
            {
                Entrypoint.Log($"✗ {mod.Id} 延迟阶段失败：{Entrypoint.Flatten(e)}");
                if (e.StackTrace != null) Entrypoint.Log(e.StackTrace.TrimEnd());
            }
        }
        Entrypoint.Log("延迟注册阶段结束");
    }

    /// <summary>建实例并把上下文交给它。</summary>
    private static bool Load(ModEntry mod)
    {
        object instance;
        try
        {
            instance = Activator.CreateInstance(mod.ModType);
        }
        catch (Exception e)
        {
            Entrypoint.Log($"✗ {mod.Id} 实例化失败（{mod.ModType.FullName} 需要公开的无参构造函数）：" +
                           Entrypoint.Flatten(e));
            return false;
        }

        mod.Instance = instance;
        if (instance == null)
        {
            // Activator 理论上不会返回 null；真返回了就说明发现出来的类型不能用，直接说清楚。
            Entrypoint.Log($"✗ {mod.Id} 实例化返回 null（类型 {mod.ModType.FullName}）");
            return false;
        }
        var directory = Path.GetDirectoryName(mod.AssemblyPath) ?? ".";
        var context = new ModContext(mod.Id, mod.Version, directory);
        ((IPylonMod)instance).Load(context);
        // 挂上哪些钩子要看得见：否则"钩子没挂上"与"目标没被调用"无法区分。
        Entrypoint.Log($"  {mod.Id} 钩子：{Il2CppInterop.RuntimeDetour.HookManager.Describe()}");
        Entrypoint.Log($"✓ {mod.Id} v{mod.Version} → {mod.ModType.FullName}.Load(context)");
        return true;
    }

    /// <summary>读清单并加载程序集；任何一步失败都记进 LoadError（不抛，免得拖垮其他 Mod）。</summary>
    private static ModEntry ReadManifest(string dllPath, string fallbackId)
    {
        var entry = new ModEntry
        {
            Id = fallbackId,
            Version = "0.0.0",
            AssemblyPath = dllPath,
        };

        var manifestPath = Path.Combine(Path.GetDirectoryName(dllPath) ?? ".", "manifest.json");
        if (File.Exists(manifestPath)) ReadManifestFields(manifestPath, entry);
        if (entry.LoadError != null) return entry;

        try
        {
            // 每个 Mod 一个加载上下文：私有依赖留在它里面，宿主的程序集仍共享（见 ModLoadContext）。
            var full = Path.GetFullPath(dllPath);
            entry.Context = new ModLoadContext(entry.Id, full);
            entry.Assembly = entry.Context.LoadFromAssemblyPath(full);
            entry.ModType = ModContext.FindModType(entry.Assembly);
        }
        catch (Exception e)
        {
            entry.LoadError = "程序集加载/发现失败：" + Entrypoint.Flatten(e);
        }
        return entry;
    }

    /// <summary>清单只有元数据：id / version / enabled / dependencies（代码坐标由反射得到）。</summary>
    private static void ReadManifestFields(string manifestPath, ModEntry entry)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = doc.RootElement;
            entry.Id = GetString(root, "id", entry.Id);
            entry.Version = GetString(root, "version", entry.Version);

            if (root.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.False)
                entry.Enabled = false;

            if (root.TryGetProperty("dependencies", out var deps) && deps.ValueKind == JsonValueKind.Array)
            {
                foreach (var dep in deps.EnumerateArray())
                    if (dep.ValueKind == JsonValueKind.String) entry.Dependencies.Add(dep.GetString());
            }
        }
        catch (Exception e)
        {
            entry.LoadError = "manifest.json 解析失败：" + e.Message;
        }
    }

    /// <summary>在 Mod 目录里找**主程序集**。</summary>
    private static string FindModDll(string dir)
    {
        var preferred = Path.Combine(dir, "mod.dll");
        if (File.Exists(preferred)) return preferred;

        var named = Path.Combine(dir, Path.GetFileName(dir) + ".dll");
        if (File.Exists(named)) return named;

        var dlls = Directory.GetFiles(dir, "*.dll");
        Array.Sort(dlls, StringComparer.OrdinalIgnoreCase);
        if (dlls.Length == 1) return dlls[0];
        if (dlls.Length == 0) return null;

        Entrypoint.Log(
            $"{Path.GetFileName(dir)} 里有 {dlls.Length} 个 dll，无法判断哪个是 Mod 本体：" +
            $"{string.Join(", ", Array.ConvertAll(dlls, Path.GetFileName))}；" +
            "请把主程序集命名为 mod.dll 或与目录同名，私有依赖放进 lib/");
        return null;
    }

    /// <summary>依赖优先排序；缺失依赖只记日志，成环则跳过环上节点。</summary>
    private static List<ModEntry> SortByDependencies()
    {
        var byId = new Dictionary<string, ModEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in Mods) byId[mod.Id] = mod;

        var ordered = new List<ModEntry>();
        var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);  // 0 未访问 1 访问中 2 完成

        void Visit(ModEntry mod)
        {
            if (state.TryGetValue(mod.Id, out var seen))
            {
                if (seen == 1) { Entrypoint.Log($"依赖成环：跳过 {mod.Id}"); return; }
                if (seen == 2) return;
            }
            state[mod.Id] = 1;
            foreach (var depId in mod.Dependencies)
            {
                if (byId.TryGetValue(depId, out var dep)) Visit(dep);
                else Entrypoint.Log($"{mod.Id} 依赖的 {depId} 不存在");
            }
            state[mod.Id] = 2;
            ordered.Add(mod);
        }

        foreach (var mod in Mods) Visit(mod);
        return ordered;
    }

    private static string GetString(JsonElement root, string key, string fallback)
        => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : fallback;
}