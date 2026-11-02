using System;

namespace Pylon.ModLoader;

/// <summary>一份配置的注册记录。</summary>
public sealed class ModConfigEntry
{
    internal ModConfigEntry(ModConfig instance, string name, IReadOnlyList<ModConfigOption> options)
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

    public IReadOnlyList<ModConfigOption> Options { get; }

    /// <summary>存盘路径；拿不到 Mod 数据目录时为 null（不落盘）。</summary>
    internal string? File { get; set; }

    /// <summary>屏幕开着时的草稿（克隆一份，屏幕只碰它）。</summary>
    internal ModConfig? Draft { get; set; }

    /// <summary>世界里显不显示这份配置。</summary>
    public bool VisibleInWorld => Instance.VisibleInWorld;

    public override string ToString() => Name;
}
