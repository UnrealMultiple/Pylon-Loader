using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Terraria;

namespace Pylon.ModLoader;

/// <summary>
/// 配置管线：声明 → 读盘 → 草稿 → 应用 → 落盘。
/// 值都是标量（bool/double/string），落盘是扁平 JSON；坏值只影响它自己那一项。
/// </summary>
internal sealed class ConfigPipeline : ILoaderModule
{
    private static readonly OptionKind[] ValueKinds =
    [
        OptionKind.Toggle, OptionKind.Range, OptionKind.Radio,
        OptionKind.Dropdown, OptionKind.Cycle, OptionKind.Color,
    ];

    private const int MaxDropdown = 12;

    /// <summary>落盘失败之类的错误往哪写（由 Mod 管线接上日志）。</summary>
    internal static Action<string>? ModConfigLog { get; set; }

    /// <summary>加载过程的说明往哪写。</summary>
    internal static Action<string>? ModConfigTrace { get; set; }

    public static ConfigPipeline Instance { get; } = new();

    private readonly List<ModConfigEntry> _entries = [];
    private readonly HashSet<ModConfigEntry> _dirty = [];

    public string Name => "config";

    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        ModConfigLog ??= message => Log.Error(message);
        ModConfigTrace ??= message => Log.Info(message);
        Log.Info($"[config] 已挂（{_entries.Count} 份配置）");
        return true;
    }

    internal IReadOnlyList<ModConfigEntry> List => _entries;

    #region 注册

    /// <summary>注册一份配置：校验声明 → 读盘 → 写初值 → OnLoaded。</summary>
    internal void Add(ModConfig instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var name = instance.GetType().Name;
        if (EntryOf(instance) is not null) return;

        var options = BuildOptions(instance);
        if (options.Count == 0)
        {
            throw new InvalidOperationException($"{name}: 请用静态 Options 声明选项（ModConfig.Toggle/Range/...）");
        }

        instance.__SetOptions(options);

        var entry = new ModConfigEntry(instance, name, options)
        {
            File = FileOf(instance.Mod, name),
        };

        Load(entry);
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

    private string? FileOf(Mod mod, string name)
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

    /// <summary>
    /// 让作者自己把选项声明出来（DeclareOptions），这里只做校验。
    /// 不走反射：声明顺序 = 代码顺序，键名与默认值都在编译期就定下来了。
    /// </summary>
    internal static List<ModConfigOption> BuildOptions(ModConfig instance)
    {
        var declared = new ConfigOptions();
        instance.__DeclareOptions(declared);

        var built = new List<ModConfigOption>(declared.Declared.Count);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var owner = instance.GetType().Name;

        foreach (var pair in declared.Declared) AddOption(built, keys, owner, pair.Key, pair.Value);

        return built;
    }

    private static void AddOption(List<ModConfigOption> built, HashSet<string> keys, string owner, string? key,
                                  object? declaration)
    {
        if (string.IsNullOrEmpty(key)) throw new InvalidOperationException($"{owner}: 选项的键名不能为空");

        if (!keys.Add(key)) throw new InvalidOperationException($"{owner}: 选项 '{key}' 重复声明");

        if (declaration is not OptionBuilder builder)
        {
            throw new InvalidOperationException($"{owner}.{key}: 声明必须是 ModConfig.Toggle/Range/... 的返回值");
        }

        var option = builder.Build(key);

        if (string.IsNullOrEmpty(option.Label)) option.Label = ModConfig.SplitWords(key);
        if (option.ChoiceLabels.Length == 0) option.ChoiceLabels = option.Choices;

        switch (option.Kind)
        {
            case OptionKind.Range:
                if (!(option.Step > 0)) throw new InvalidOperationException($"{owner}.{key}: Range 的 step 必须大于 0");
                if (option.Max < option.Min) throw new InvalidOperationException($"{owner}.{key}: Range 的 max 小于 min");
                option.Default = Snap(option, ValueConverter.Convert(option.Default, option.Min));
                break;
            case OptionKind.Dropdown:
                if (option.Choices.Length > MaxDropdown)
                {
                    throw new InvalidOperationException($"{owner}.{key}: Dropdown 最多 {MaxDropdown} 项（游戏的下拉只有这么多格）");
                }

                goto case OptionKind.Radio;
            case OptionKind.Radio:
            case OptionKind.Cycle:
                if (option.Choices.Length == 0) throw new InvalidOperationException($"{owner}.{key}: 需要至少一个选择");
                if (!Contains(option.Choices, option.Default)) option.Default = option.Choices[0];
                break;
        }

        // Header / Button / Link 没有值，但仍要进列表（屏幕按顺序画）。
        built.Add(option);
    }

    private static bool Contains(string[] choices, object? value)
    {
        var text = System.Convert.ToString(value, CultureInfo.InvariantCulture);
        for (var i = 0; i < choices.Length; i++)
        {
            if (string.Equals(choices[i], text, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    #endregion

    #region 值与校验

    internal static bool HasValue(ModConfigOption option) => Array.IndexOf(ValueKinds, option.Kind) >= 0;

    /// <summary>Range 的值：先夹到界内，再按 step 对齐，再夹一次（对齐可能越界）。</summary>
    internal static double Snap(ModConfigOption option, double value)
    {
        if (double.IsNaN(value)) return option.Min;

        if (value < option.Min) value = option.Min;
        if (value > option.Max) value = option.Max;

        if (option.Step > 0)
        {
            var steps = Math.Round((value - option.Min) / option.Step, MidpointRounding.AwayFromZero);
            value = option.Min + steps * option.Step;
        }

        if (value < option.Min) value = option.Min;
        if (value > option.Max) value = option.Max;

        // 幂等：对齐后再对齐一次必须还是自己（否则屏幕的滑条会抖）。
        return Math.Abs(value) < 1e-9 ? 0 : value;
    }

    /// <summary>把一个外部来的值收成合法值；收不动返回 false（Range 例外：夹进来就算成功）。</summary>
    internal static bool Coerce(ModConfigOption option, object? value, out object result)
    {
        result = option.Default;

        switch (option.Kind)
        {
            case OptionKind.Toggle:
                if (value is bool flag) { result = flag; return true; }
                if (value is string text && bool.TryParse(text, out var parsed)) { result = parsed; return true; }
                return false;

            case OptionKind.Range:
            {
                var number = ValueConverter.Convert(value, double.NaN);
                if (double.IsNaN(number)) return false;

                result = Snap(option, number);
                return true;
            }

            case OptionKind.Radio:
            case OptionKind.Dropdown:
            case OptionKind.Cycle:
            {
                var choice = System.Convert.ToString(value, CultureInfo.InvariantCulture);
                if (!Contains(option.Choices, choice)) return false;

                result = choice!;
                return true;
            }

            case OptionKind.Color:
            {
                var hex = System.Convert.ToString(value, CultureInfo.InvariantCulture);
                if (!IsHex(hex)) return false;

                result = hex!.ToUpperInvariant();
                return true;
            }

            default:
                return false;
        }
    }

    private static bool IsHex(string? value)
    {
        if (value is null || value.Length != 7 || value[0] != '#') return false;

        for (var i = 1; i < value.Length; i++)
        {
            var c = value[i];
            var hex = c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
            if (!hex) return false;
        }

        return true;
    }

    #endregion

    #region 读盘与落盘

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

        if (data is null) return;

        for (var i = 0; i < entry.Options.Count; i++)
        {
            var option = entry.Options[i];
            if (!HasValue(option)) continue;
            if (!data.TryGetPropertyValue(option.Key, out var node) || node is null) continue;

            try
            {
                object? raw = option.Kind switch
                {
                    OptionKind.Toggle => node.GetValue<bool>(),
                    OptionKind.Range => node.GetValue<double>(),
                    _ => node.GetValue<string>(),
                };

                if (Coerce(option, raw, out var value)) entry.Instance.__SetValueRaw(option.Key, value);
                else Report($"{entry.Name}.{option.Key}: 存盘值不合法，用默认值");
            }
            catch (Exception e)
            {
                Report($"{entry.Name}.{option.Key}: 存盘值读不出来（{e.Message}）；用默认值");
            }
        }
    }

    private static void ApplyDefaults(ModConfig target, IReadOnlyList<ModConfigOption> options)
    {
        for (var i = 0; i < options.Count; i++)
        {
            var option = options[i];
            if (!HasValue(option)) continue;

            target.__SetValueRaw(option.Key, option.Default ?? DefaultOf(option));
        }
    }

    private static object DefaultOf(ModConfigOption option) => option.Kind switch
    {
        OptionKind.Toggle => false,
        OptionKind.Range => 0d,
        _ => "",
    };

    private static JsonObject ToJson(ModConfigEntry entry)
    {
        var data = new JsonObject();

        for (var i = 0; i < entry.Options.Count; i++)
        {
            var option = entry.Options[i];
            if (!HasValue(option)) continue;

            var value = entry.Instance.GetValue(option.Key);
            data[option.Key] = option.Kind switch
            {
                OptionKind.Toggle => JsonValue.Create(ValueConverter.Convert(value, false)),
                OptionKind.Range => JsonValue.Create(ValueConverter.Convert(value, 0d)),
                _ => JsonValue.Create(System.Convert.ToString(value, CultureInfo.InvariantCulture)),
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

            File.WriteAllText(entry.File, ToJson(entry).ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e)
        {
            Report($"{entry.Name}: {entry.File} 写不进去（{e.Message}）；本次改动只在内存里");
        }
    }

    #endregion

    #region 草稿

    /// <summary>
    /// 开屏：给每份配置挂一张"待提交"表，屏幕只往它里面写；真值一个字节都不动，
    /// 所以"取消"永远是零成本丢弃。不克隆配置对象——那需要反射造实例。
    /// </summary>
    internal void Begin()
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];

            entry.Pending = new Dictionary<string, object>(StringComparer.Ordinal);
            entry.Instance.__Overlay = entry.Pending;
        }
    }

    internal ModConfig View(ModConfigEntry entry) => entry.Instance;

    internal bool Pending() => Pending(_entries);

    internal bool Pending(IEnumerable<ModConfigEntry> entries)
    {
        foreach (var entry in entries)
        {
            var pending = entry.Pending;
            if (pending is null) continue;

            foreach (var pair in pending)
            {
                if (!Same(pair.Value, entry.Instance.__StoredValue(pair.Key))) return true;
            }
        }

        return false;
    }

    internal bool AtDefaults(IEnumerable<ModConfigEntry> entries)
    {
        foreach (var entry in entries)
        {
            var options = entry.Options;

            for (var i = 0; i < options.Count; i++)
            {
                var option = options[i];
                if (!HasValue(option)) continue;
                if (!Same(entry.Instance.GetValue(option.Key), option.Default)) return false;
            }
        }

        return true;
    }

    /// <summary>把待提交的值填成默认值（不动真值、不落盘）。</summary>
    internal void Defaults(IEnumerable<ModConfigEntry> entries)
    {
        foreach (var entry in entries)
        {
            var pending = entry.Pending;
            if (pending is null) continue;

            var options = entry.Options;
            for (var i = 0; i < options.Count; i++)
            {
                var option = options[i];
                if (!HasValue(option)) continue;

                pending[option.Key] = option.Default ?? DefaultOf(option);
            }
        }
    }

    /// <summary>应用：待提交的值写回真值 → 只对变了的项发 OnChanged → OnApply → 落盘。</summary>
    internal void Apply()
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            var pending = entry.Pending;
            if (pending is null) continue;

            ApplyOne(entry, pending);

            entry.Pending = null;
            entry.Instance.__Overlay = null;
        }

        _dirty.Clear();
    }

    private void ApplyOne(ModConfigEntry entry, Dictionary<string, object> pending)
    {
        var options = entry.Options;
        var changed = false;

        for (var i = 0; i < options.Count; i++)
        {
            var option = options[i];
            if (!HasValue(option)) continue;
            if (!pending.TryGetValue(option.Key, out var value)) continue;
            if (Same(value, entry.Instance.__StoredValue(option.Key))) continue;

            entry.Instance.__SetValueRaw(option.Key, value);
            changed = true;

            try
            {
                entry.Instance.OnChanged(option.Key);
            }
            catch (Exception e)
            {
                Report($"{entry.Name}.OnChanged 出错：{Flatten(e)}");
            }
        }

        if (!changed) return;

        try
        {
            entry.Instance.OnApply();
        }
        catch (Exception e)
        {
            Report($"{entry.Name}.OnApply 出错：{Flatten(e)}");
        }

        Save(entry);

        // 服务器说了算的那份：本地只是暂时值，得让服务器裁决并在别的端也生效。
        if (entry.Instance.Scope == ConfigScope.ServerSide && Main.netMode == 1)
            ConfigNetPipeline.Instance.Request(entry);
    }

    internal void Discard()
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];

            entry.Pending = null;
            entry.Instance.__Overlay = null;
        }
    }

    /// <summary>屏幕上的改动：只写待提交表，真值等"应用"。</summary>
    internal void Set(ModConfigEntry entry, ModConfigOption option, object value)
    {
        if (option is null || !HasValue(option)) return;
        if (!Coerce(option, value, out var coerced)) return;

        var pending = entry.Pending;
        if (pending is null)
        {
            pending = new Dictionary<string, object>(StringComparer.Ordinal);
            entry.Pending = pending;
            entry.Instance.__Overlay = pending;
        }

        pending[option.Key] = coerced;
        _dirty.Add(entry);
    }

    /// <summary>恢复默认并立刻落盘（给代码用，不走草稿）。</summary>
    internal void Reset(ModConfig instance)
    {
        var entry = EntryOf(instance);
        if (entry is null) return;

        ApplyDefaults(instance, entry.Options);

        var options = entry.Options;
        for (var i = 0; i < options.Count; i++)
        {
            if (!HasValue(options[i])) continue;

            try
            {
                instance.OnChanged(options[i].Key);
            }
            catch (Exception e)
            {
                Report($"{entry.Name}.OnChanged 出错：{Flatten(e)}");
            }
        }

        Save(entry);
    }

    /// <summary>改一个选项：校验 → 落盘 → OnChanged（世界内立刻生效，不走草稿）。</summary>
    internal void SetOption(ModConfig instance, string key, object value)
    {
        var entry = EntryOf(instance);
        if (entry is null) return;

        var option = Find(entry, key);
        if (option is null) throw new KeyNotFoundException($"{entry.Name}: 没有 '{key}' 这个选项");

        if (!Coerce(option, value, out var coerced)) return;

        try
        {
            if (instance.NeedsReload(option.Key)) return; // 作者声明这个键要重启才能改：拒绝并保留旧值
        }
        catch (Exception e)
        {
            Report($"{entry.Name}.NeedsReload 出错：{Flatten(e)}");
            return;
        }

        instance.__SetValueRaw(option.Key, coerced);
        Save(entry);

        try
        {
            instance.OnChanged(option.Key);
        }
        catch (Exception e)
        {
            Report($"{entry.Name}.OnChanged 出错：{Flatten(e)}");
        }
    }

    #endregion

    #region 状态查询与动作

    internal bool IsEnabled(ModConfigEntry entry, ModConfigOption option)
    {
        if (option.EnabledWhen is not null)
        {
            try
            {
                return option.EnabledWhen(View(entry));
            }
            catch (Exception e)
            {
                Report($"{entry.Name}.{option.Key}.enabledWhen 出错：{Flatten(e)}");
                return true;
            }
        }

        if (string.IsNullOrEmpty(option.EnabledWhenKey)) return true;

        var other = Find(entry, option.EnabledWhenKey);
        if (other is null) return true;

        return ValueConverter.Convert(View(entry).GetValue(other.Key), false);
    }

    /// <summary>Button / Link 被点：动作是声明时给的委托，不做任何按名字找方法。</summary>
    internal void Run(ModConfigEntry entry, ModConfigOption option)
    {
        if (option.ActionDelegate is not null)
        {
            try
            {
                option.ActionDelegate(View(entry));
            }
            catch (Exception e)
            {
                Report($"{entry.Name}.{option.Key} 出错：{Flatten(e)}");
            }

            return;
        }

        if (option.Kind == OptionKind.Link) OpenUrl(option.Url);
    }

    private static void OpenUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            Report($"打开链接失败（{url}）：{Flatten(e)}");
        }
    }

    /// <summary>把标脏的配置写盘（屏幕关闭时调）。</summary>
    internal void Flush()
    {
        foreach (var entry in _dirty) Save(entry);
        _dirty.Clear();
    }

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

    internal ModConfigOption? Find(ModConfigEntry? entry, string? key)
    {
        if (entry is null || key is null) return null;

        var options = entry.Options;
        for (var i = 0; i < options.Count; i++)
        {
            if (string.Equals(options[i].Key, key, StringComparison.Ordinal)) return options[i];
        }

        return null;
    }

    #endregion

    private static bool Same(object? a, object? b)
    {
        if (a is null || b is null) return a is null && b is null;

        if (a is double left && b is double right) return Math.Abs(left - right) < 1e-9;

        return a.Equals(b);
    }

    internal static string Flatten(Exception e) => Safe.Describe(e);

    private static void Report(string message) => (ModConfigLog ?? Log.Error)(message);
}
