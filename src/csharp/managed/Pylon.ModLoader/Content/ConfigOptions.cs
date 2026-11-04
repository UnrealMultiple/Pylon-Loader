namespace Pylon.ModLoader;

/// <summary>
/// 选项声明的收集器：作者在 <see cref="ModConfig.DeclareOptions"/> 里逐条 Add。
/// 这里不做任何反射——顺序、键名、合法性都由作者与管线在注册期定死。
/// </summary>
public sealed class ConfigOptions
{
    private readonly List<KeyValuePair<string, OptionBuilder>> _declared = [];

    /// <summary>声明一条选项：键名是值、标签、落盘共用的那个名字。</summary>
    public ConfigOptions Add(string key, OptionBuilder option)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(option);

        _declared.Add(new KeyValuePair<string, OptionBuilder>(key, option));
        return this;
    }

    /// <summary>已声明的条目（保持声明顺序）。</summary>
    internal IReadOnlyList<KeyValuePair<string, OptionBuilder>> Declared => _declared;
}
