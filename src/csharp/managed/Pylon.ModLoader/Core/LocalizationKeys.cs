using Terraria;
using Terraria.Localization;

namespace Pylon.ModLoader;

/// <summary>
/// 把 LocalizedText 登记进游戏语言表。
/// 时机是硬约束：游戏加载期间会遍历 _localizedTexts 跑拷贝命令，此时写入会让它
/// 在未就绪的语言状态上取值并中断加载（真机踩过），所以只在内容就绪后写。
/// </summary>
internal static class LocalizationKeys
{
    internal static void Register(string key, LocalizedText? text)
    {
        if (string.IsNullOrEmpty(key) || text is null) return;
        if (!Main._isAsyncLoadComplete) return;

        var texts = LanguageManager.Instance?._localizedTexts;
        if (texts is null) return;

        texts[key] = text;
    }
}
