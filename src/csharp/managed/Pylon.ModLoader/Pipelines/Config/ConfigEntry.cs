namespace Pylon.ModLoader;

/// <summary>一份配置的注册记录：作者那份实例（真值）、选项列表、载荷期克隆、草稿、落盘路径。</summary>
public sealed class ModConfigEntry
{
    internal ModConfigEntry(ModConfig instance, string name, IReadOnlyList<ConfigOption> options)
    {
        Instance = instance;
        Name = name;
        Options = options;
    }

    /// <summary>作者那份配置实例（真值都在它身上）。</summary>
    public ModConfig Instance { get; }

    /// <summary>配置类的名字（也是存盘文件名与本地化键的一段）。</summary>
    public string Name { get; }

    public Mod Mod => Instance.Mod;

    public IReadOnlyList<ConfigOption> Options { get; }

    /// <summary>存盘路径；拿不到存档目录时为 null（不落盘）。</summary>
    internal string? File { get; set; }

    /// <summary>载荷期的克隆：ReloadRequired 的比对基准（基线 loadTimeConfigs）。</summary>
    internal ModConfig? LoadTime { get; set; }

    /// <summary>屏幕开着时的未保存改动（选项名 → 值）；不克隆对象，只存改过的值。</summary>
    internal Dictionary<string, object>? Pending { get; set; }

    public override string ToString() => Name;
}
