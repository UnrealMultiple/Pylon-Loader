using Terraria.Localization;

namespace Pylon.ModLoader
{
    /// <summary>本地化静态工具：读的文本都在游戏的 Language 表里（Mod 的 Localization/&lt;语言&gt;.json 由内容层按 Mods.&lt;id&gt;.… 注册进去）。</summary>
    public static class ModLocalization
    {
        /// <summary>当前语言的名称（如 en-US）；语言还没就绪时给 en-US。</summary>
        public static string ActiveCultureName
        {
            get
            {
                try { return Language.ActiveCulture.Name; }
                catch { return "en-US"; }
            }
        }

        /// <summary>键的文本（Mod 的短键由 LocalizationLoader 进游戏 Language 表后这里查表即可）；没有则返回键本身。</summary>
        public static string Translate(string path) =>
            Language.Exists(path) ? Language.GetTextValue(path) : path;

        /// <summary>键的文本；没有这段文本时返回 ""。</summary>
        public static string TryTranslate(string path) =>
            Language.Exists(path) ? Language.GetTextValue(path) : "";

        /// <summary>文本：Mod 有就用 Mod 的，否则用游戏的；都没有时返回键本身。</summary>
        public static string GetTextValue(string key) => Language.GetTextValue(key);

        /// <summary>文本对象：Mod 或游戏的；键不存在时游戏给一个值为键本身的。</summary>
        public static LocalizedText GetText(string key) => Language.GetText(key);

        /// <summary>游戏用的完整键；尚无 Mod 本地化注册表，键原样给（短键补全那步没翻）。</summary>
        public static string Key(string path) => path;

        /// <summary>Mod 或游戏有没有这段文本（Mod 的键在表里就算有）。</summary>
        public static bool Exists(string key) => Language.Exists(key);

        /// <summary>在任意键下登记一段文本；返回键本身（实际写入在内容层 LocalizationLoader）。</summary>
        public static string Register(string key, string text) => key;
    }
}