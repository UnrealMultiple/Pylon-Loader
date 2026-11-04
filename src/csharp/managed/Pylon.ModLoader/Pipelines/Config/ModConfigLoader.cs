namespace Pylon.ModLoader;

/// <summary>
/// 配置的公开门面：作者与屏幕都按静态方法调用（这是已编译的 API），状态全在 ConfigPipeline 里。
/// </summary>
public static class ModConfigLoader
{
    /// <summary>落盘失败之类的错误往哪写（由 Mod 管线接上日志）。</summary>
    public static Action<string>? ModConfigLog
    {
        get => ConfigPipeline.ModConfigLog;
        set => ConfigPipeline.ModConfigLog = value;
    }

    /// <summary>加载过程的说明往哪写。</summary>
    public static Action<string>? ModConfigTrace
    {
        get => ConfigPipeline.ModConfigTrace;
        set => ConfigPipeline.ModConfigTrace = value;
    }

    /// <summary>全部已注册的配置（按注册顺序）。</summary>
    public static IReadOnlyList<ModConfigEntry> List => ConfigPipeline.Instance.List;

    public static void Add(ModConfig instance) => ConfigPipeline.Instance.Add(instance);

    public static List<ModConfigOption> BuildOptions(ModConfig instance) => ConfigPipeline.BuildOptions(instance);

    public static bool HasValue(ModConfigOption option) => ConfigPipeline.HasValue(option);

    public static double Snap(ModConfigOption option, double value) => ConfigPipeline.Snap(option, value);

    public static void Begin() => ConfigPipeline.Instance.Begin();

    public static ModConfig View(ModConfigEntry entry) => ConfigPipeline.Instance.View(entry);

    public static bool Pending() => ConfigPipeline.Instance.Pending();

    public static bool Pending(IEnumerable<ModConfigEntry> entries) => ConfigPipeline.Instance.Pending(entries);

    public static bool AtDefaults(IEnumerable<ModConfigEntry> entries) => ConfigPipeline.Instance.AtDefaults(entries);

    public static void Defaults(IEnumerable<ModConfigEntry> entries) => ConfigPipeline.Instance.Defaults(entries);

    public static void Apply() => ConfigPipeline.Instance.Apply();

    public static void Discard() => ConfigPipeline.Instance.Discard();

    public static void Set(ModConfigEntry entry, ModConfigOption option, object value) =>
        ConfigPipeline.Instance.Set(entry, option, value);

    public static void Reset(ModConfig instance) => ConfigPipeline.Instance.Reset(instance);

    public static void SetOption(ModConfig instance, string key, object value) =>
        ConfigPipeline.Instance.SetOption(instance, key, value);

    public static bool IsEnabled(ModConfigEntry entry, ModConfigOption option) =>
        ConfigPipeline.Instance.IsEnabled(entry, option);

    public static void Run(ModConfigEntry entry, ModConfigOption option) =>
        ConfigPipeline.Instance.Run(entry, option);

    public static void Flush() => ConfigPipeline.Instance.Flush();

    public static List<ModConfigEntry> Of(Mod mod) => ConfigPipeline.Instance.Of(mod);

    public static ModConfigEntry? EntryOf(ModConfig instance) => ConfigPipeline.Instance.EntryOf(instance);

    public static ModConfigOption? Find(ModConfigEntry? entry, string? key) => ConfigPipeline.Instance.Find(entry, key);

    public static string Flatten(Exception e) => ConfigPipeline.Flatten(e);
}
