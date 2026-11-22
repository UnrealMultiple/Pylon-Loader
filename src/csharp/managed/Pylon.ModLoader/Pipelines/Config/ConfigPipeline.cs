using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Audio;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Terraria;

namespace Pylon.ModLoader.Pipelines;
/// <summary>
/// 配置管线：反射收选项 → 默认值 → 读盘 → 草稿 → 应用 → 落盘。
/// 值即 public 字段/属性（ConfigOption.Get/Set）；坏值只影响它自己那一项；
/// 载荷期克隆一份 loadTime 配置，ReloadRequired 的比对用它。
/// </summary>
internal sealed class ConfigPipeline : ILoaderModule
{
    private static readonly JsonSerializerOptions DataOptions = new();

    /// <summary>落盘失败之类的错误往哪写（由 Mod 管线接上日志）。</summary>
    internal static Action<string>? ModConfigLog { get; set; }

    /// <summary>加载过程的说明往哪写。</summary>
    internal static Action<string>? ModConfigTrace { get; set; }

    public static ConfigPipeline Instance { get; } = new();

    private readonly List<ModConfigEntry> _entries = [];

    public string Name => "config";

    private static IModLog Log => LoaderHost.Current.Log;

    /// <summary>config 管线的安装门：仅在有 Mod 注册配置类型时就位。</summary>
    internal static class Gates
    {
        internal static bool HasConfigs() => ModContent.GetContent<ModConfig>().Count > 0;

        internal struct Core : IGate
        {
            public static bool Wanted() => HasConfigs();

            public static void Install() => Instance.InstallCore();
        }
    }

    public bool Install() => GateHub<ConfigPipeline>.Run();

    private void InstallCore()
    {
        ModConfigLog ??= message => Log.Error(message);
        ModConfigTrace ??= message => Log.Info(message);
        Log.Info($"[config] 已挂（{_entries.Count} 份配置）");
    }

    internal IReadOnlyList<ModConfigEntry> List => _entries;

    internal bool Pending() => Pending(_entries);

    internal static bool HasValue(ConfigOption option) => option.Kind != ConfigOptionKind.Data;

    private const int MENU_OPEN = 10, MENU_CLOSE = 11;

    #region 注册与读盘

    /// <summary>注册一份配置：反射收选项 → 默认值 → 读盘 → 载荷期克隆 → OnLoaded。</summary>
    internal void Add(ModConfig instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var name = instance.GetType().Name;
        if (EntryOf(instance) is not null) return;

        var options = ConfigReflection.OptionsOf(instance.GetType());
        var entry = new ModConfigEntry(instance, name, options)
        {
            File = FileOf(instance.Mod, name),
        };

        Load(entry);
        entry.LoadTime = entry.Instance.Clone();
        _entries.Add(entry);

        try
        {
            instance.OnLoaded();
        }
        catch (Exception e)
        {
            Report($"{name}.OnLoaded 出错：{Flatten(e)}");
        }
    }

    private static string? FileOf(Mod mod, string name)
    {
        try
        {
            var root = Main.SavePath;
            if (string.IsNullOrEmpty(root)) return null;

            return Path.Combine(root, "ModConfigs", $"{mod?.Name ?? "Mod"}_{name}.json");
        }
        catch (Exception e)
        {
            Report("配置目录取不到：" + Flatten(e));
            return null;
        }
    }

    private void Load(ModConfigEntry entry)
    {
        ApplyDefaults(entry.Instance, entry.Options);

        if (entry.File is null || !File.Exists(entry.File)) return;

        JsonObject? data;
        try
        {
            data = JsonNode.Parse(File.ReadAllText(entry.File)) as JsonObject;
        }
        catch (Exception e)
        {
            Report($"{entry.Name}: {entry.File} 坏了（{e.Message}）；用默认值");
            return;
        }

        if (data != null) Populate(entry.Instance, data);
    }

    private static void ApplyDefaults(ModConfig target, IReadOnlyList<ConfigOption> options)
    {
        ModConfig fresh;
        try
        {
            fresh = (ModConfig)Activator.CreateInstance(target.GetType(), true)!;
        }
        catch (Exception e)
        {
            Report($"{target.FullName}: 默认实例造不出来（{Flatten(e)}）");
            return;
        }

        foreach (var option in options)
        {
            object? value = option.Get(fresh);
            if (option.HasDefault && option.Coerce(option.Default) is { } coerced)
                value = coerced;
            else if (option.Kind == ConfigOptionKind.Range && value != null)
                value = option.Coerce(value);
            option.Set(target, value);
        }
    }

    private static void Populate(ModConfig config, JsonObject data)
    {
        foreach (var option in ConfigReflection.OptionsOf(config.GetType()))
        {
            if (!data.TryGetPropertyValue(option.Name, out var node) || node == null)
                continue;
            try
            {
                object? value = option.Kind switch
                {
                    ConfigOptionKind.Toggle => node.GetValue<bool>(),
                    ConfigOptionKind.Range => option.Coerce(node.GetValue<double>()),
                    ConfigOptionKind.Choice => option.Coerce(node.GetValue<string>()),
                    ConfigOptionKind.Color => option.Coerce(node.GetValue<string>()),
                    _ => node.Deserialize(option.Type, DataOptions),
                };
                if (value != null || option.Kind == ConfigOptionKind.Data)
                    option.Set(config, value);
            }
            catch (Exception e)
            {
                Report($"{config.FullName}.{option.Name}: 存盘值读不出来（{e.Message}）；用默认值");
            }
        }
    }

    private static JsonObject ToJson(ModConfig config)
    {
        var data = new JsonObject();
        foreach (var option in ConfigReflection.OptionsOf(config.GetType()))
        {
            object? value = option.Get(config);
            data[option.Name] = option.Kind switch
            {
                ConfigOptionKind.Toggle => JsonValue.Create((bool)value!),
                ConfigOptionKind.Range => JsonValue.Create(Convert.ToDouble(value, CultureInfo.InvariantCulture)),
                ConfigOptionKind.Choice => JsonValue.Create(value?.ToString()),
                ConfigOptionKind.Color => JsonValue.Create(ConfigOption.FormatColor((Color)value!)),
                _ => JsonSerializer.SerializeToNode(value, option.Type, DataOptions),
            };
        }

        return data;
    }

    internal void Save(ModConfigEntry entry)
    {
        if (entry.File is null) return;

        try
        {
            var folder = Path.GetDirectoryName(entry.File);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            File.WriteAllText(entry.File, ToJson(entry.Instance).ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e)
        {
            Report($"{entry.Name}: {entry.File} 写不进去（{e.Message}）；本次改动只在内存里");
        }
    }

    #endregion

    #region SaveChanges（基线形状）

    internal ConfigSaveResult SaveChanges(ModConfig active, ModConfig pending, Action<string, Color>? status,
                                          bool silent, bool broadcast)
    {
        var entry = EntryOf(active);
        if (entry is null) throw new InvalidOperationException("SaveChanges 必须在已注册的配置上调用");

        var pendingIsActive = ReferenceEquals(pending, active);

        if (Main.gameMenu)
        {
            if (!silent) SoundEngine.PlaySound(MENU_OPEN);
            SaveEntry(entry, pending, pendingIsActive);
            if (entry.LoadTime is null || !entry.LoadTime.NeedsReload(entry.Instance)) Changed(entry);
        }
        else
        {
            if (pending.Mode == ConfigScope.ServerSide && Main.netMode == 1)
            {
                status?.Invoke("正在让服务器接受改动", new Color(255, 255, 0, 255));
                ConfigNetPipeline.Instance.Request(entry, broadcast, option => option.Get(pending));
                if (pendingIsActive) Load(entry);
                return ConfigSaveResult.RequestSentToServer;
            }

            if (entry.LoadTime is not null && entry.LoadTime.NeedsReload(pending))
            {
                if (!silent) SoundEngine.PlaySound(MENU_CLOSE);
                status?.Invoke("这些改动需要重载游戏才能生效", new Color(255, 0, 0, 255));
                if (pendingIsActive) Load(entry);
                return ConfigSaveResult.NeedsReload;
            }

            if (!silent) SoundEngine.PlaySound(MENU_OPEN);
            SaveEntry(entry, pending, pendingIsActive);
            Changed(entry);
            if (active.Mode == ConfigScope.ServerSide && (Main.netMode & 2) != 0)
                ConfigNetPipeline.Instance.Broadcast(entry, broadcast);
        }

        status?.Invoke("配置已保存", new Color(0, 255, 0, 255));
        return ConfigSaveResult.Success;
    }

    private void SaveEntry(ModConfigEntry entry, ModConfig pending, bool pendingIsActive)
    {
        if (!pendingIsActive) CopyValues(pending, entry.Instance);
        Save(entry);
    }

    private static void CopyValues(ModConfig from, ModConfig to)
    {
        foreach (var option in ConfigReflection.OptionsOf(from.GetType()))
            option.Set(to, option.Get(from));
    }

    private static void Changed(ModConfigEntry entry)
    {
        try
        {
            entry.Instance.OnChanged();
        }
        catch (Exception e)
        {
            Report($"{entry.Name}.OnChanged 出错：{Flatten(e)}");
        }
    }

    #endregion

    #region 状态查询与动作

    internal bool Pending(IEnumerable<ModConfigEntry> entries)
    {
        foreach (var entry in entries)
        {
            var pending = entry.Pending;
            if (pending is null) continue;

            foreach (var option in entry.Options)
            {
                if (!pending.TryGetValue(option.Name, out var value)) continue;
                if (!Equals(option.Get(entry.Instance), value)) return true;
            }
        }

        return false;
    }

    internal bool AtDefaults(IEnumerable<ModConfigEntry> entries)
    {
        foreach (var entry in entries)
        {
            var fresh = Fresh(entry.Instance);
            if (fresh is null) continue;

            foreach (var option in entry.Options)
            {
                if (!Equals(option.Get(entry.Instance), option.Get(fresh))) return false;
            }
        }

        return true;
    }

    /// <summary>把待提交的值填成默认值（不动真值、不落盘）。</summary>
    internal void Defaults(IEnumerable<ModConfigEntry> entries)
    {
        foreach (var entry in entries)
        {
            var fresh = Fresh(entry.Instance);
            if (fresh is null) continue;

            foreach (var option in entry.Options)
            {
                entry.Pending ??= new Dictionary<string, object>(StringComparer.Ordinal);
                entry.Pending[option.Name] = option.Get(fresh)!;
            }
        }
    }

    /// <summary>应用：待提交的值经 Coerce 写回字段 → OnChanged → 落盘；重载项与 ServerSide 各走各的路。</summary>
    internal void Apply()
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            var pending = entry.Pending;
            if (pending is null) continue;

            if (Main.gameMenu)
            {
                ApplyOne(entry, pending);
            }
            else if (entry.Instance.Mode == ConfigScope.ServerSide && Main.netMode == 1)
            {
                ConfigNetPipeline.Instance.Request(entry, lookup: option => option.Coerce(
                    pending.TryGetValue(option.Name, out var value) ? value : option.Get(entry.Instance)));
                Load(entry);
            }
            else if (LoadTimeNeedsReload(entry, pending))
            {
                Load(entry);
            }
            else
            {
                ApplyOne(entry, pending);
            }

            entry.Pending = null;
        }
    }

    private bool LoadTimeNeedsReload(ModConfigEntry entry, Dictionary<string, object> pending)
    {
        var loadTime = entry.LoadTime;
        if (loadTime is null) return false;

        foreach (var option in entry.Options)
        {
            if (!option.ReloadRequired) continue;
            if (!pending.TryGetValue(option.Name, out var value)) continue;
            if (!Equals(option.Get(loadTime), value)) return true;
        }

        return false;
    }

    private void ApplyOne(ModConfigEntry entry, Dictionary<string, object> pending)
    {
        var changed = false;

        foreach (var option in entry.Options)
        {
            if (!pending.TryGetValue(option.Name, out var value)) continue;
            if (Equals(option.Get(entry.Instance), value)) continue;

            option.Set(entry.Instance, value);
            changed = true;
        }

        if (!changed) return;

        try
        {
            entry.Instance.OnChanged();
        }
        catch (Exception e)
        {
            Report($"{entry.Name}.OnChanged 出错：{Flatten(e)}");
        }

        Save(entry);

        if (entry.Instance.Mode == ConfigScope.ServerSide && Main.netMode == 2)
            ConfigNetPipeline.Instance.Broadcast(entry);
    }

    internal void Discard()
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            _entries[i].Pending = null;
        }
    }

    /// <summary>屏幕上的改动：只写待提交表，真值等"应用"。</summary>
    internal void Set(ModConfigEntry entry, ConfigOption option, object value)
    {
        var coerced = option.Coerce(value);
        if (coerced is null) return;

        entry.Pending ??= new Dictionary<string, object>(StringComparer.Ordinal);
        entry.Pending[option.Name] = coerced;
    }

    /// <summary>开屏：给每份配置挂一张"待提交"表，屏幕只往它里面写；真值一个字节都不动。</summary>
    internal void Begin()
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            _entries[i].Pending = new Dictionary<string, object>(StringComparer.Ordinal);
        }
    }

    internal ModConfig View(ModConfigEntry entry) => entry.Instance;

    internal List<ModConfigEntry> Of(Mod mod)
    {
        var result = new List<ModConfigEntry>();
        for (var i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Instance.Mod == mod) result.Add(_entries[i]);
        }

        return result;
    }

    internal ModConfigEntry? EntryOf(ModConfig instance)
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            if (ReferenceEquals(_entries[i].Instance, instance)) return _entries[i];
        }

        return null;
    }

    internal ConfigOption? Find(ModConfigEntry? entry, string? name)
    {
        if (entry is null || name is null) return null;

        foreach (var option in entry.Options)
        {
            if (string.Equals(option.Name, name, StringComparison.Ordinal)) return option;
        }

        return null;
    }

    internal static object? Read(ModConfigEntry entry, ConfigOption option)
    {
        if (entry.Pending is { } pending && pending.TryGetValue(option.Name, out var value)) return value;
        return option.Get(entry.Instance);
    }

    /// <summary>屏幕关闭时的兜底：把全部配置再落一次盘（草稿已在 Apply 时落过）。</summary>
    internal void Flush()
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Instance.Mode == ConfigScope.ServerSide && Main.netMode == 1) continue;
            Save(_entries[i]);
        }
    }

    #endregion

    #region 内部

    private static ModConfig? Fresh(ModConfig target)
    {
        try
        {
            return (ModConfig)Activator.CreateInstance(target.GetType(), true)!;
        }
        catch (Exception e)
        {
            Report($"{target.FullName}: 默认实例造不出来（{Flatten(e)}）");
            return null;
        }
    }

    internal static string Flatten(Exception e) => Safe.Describe(e);

    private static void Report(string message) => (ModConfigLog ?? Log.Error)(message);

    #endregion
}

