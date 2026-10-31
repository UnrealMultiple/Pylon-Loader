using Terraria.Localization;

namespace Pylon.ModLoader;

/// <summary>本地化查询：文本都在游戏的 Language 表里。</summary>
public static class ModLocalization
{
    /// <summary>当前语言的名称（如 en-US）；语言还没就绪时给 en-US。</summary>
    public static string ActiveCultureName => Language.ActiveCulture?.Name ?? "en-US";

    /// <summary>键的文本；没有这段文本时返回键本身。</summary>
    public static string Translate(string path) => Language.Exists(path) ? Language.GetTextValue(path) : path;

    /// <summary>键的文本；没有这段文本时返回空串。</summary>
    public static string TryTranslate(string path) => Language.Exists(path) ? Language.GetTextValue(path) : string.Empty;

    public static string GetTextValue(string key) => Language.GetTextValue(key);

    public static LocalizedText GetText(string key) => Language.GetText(key);

    public static string Key(string path) => path;

    public static bool Exists(string key) => Language.Exists(key);

    /// <summary>登记一段文本；实际写入由内容层的 LocalizationLoader 完成，这里只回传键。</summary>
    public static string Register(string key, string text) => key;
}
