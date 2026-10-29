// 层：Mod 框架 / Core —— 语言键注册（LocalizationLoader.Register 的移植）。
//
// 游戏按 key 从 LanguageManager._localizedTexts 取文本；mod 的 DisplayName/Tooltip/前缀名
// 要先登记 key，再把同一 LocalizedText 写进对应缓存数组（Lang.prefix / _itemNameCache…），
// 换语言/重建缓存时还能拿到。
//
// ⚠ 时机是硬约束：游戏加载中会遍历 _localizedTexts 跑 ProcessCopyCommandsInTexts，提前塞条目
// 会让它在未就绪的语言状态上取值 → NullReferenceException → 加载中断 → 卡启动画面（真机踩过）。
// 只在游戏内容就绪后写；早了跳过，调用方在 Setup/Tables 阶段会再写一次。
using System;

namespace Pylon.ModLoader
{
    /// <summary>把 LocalizedText 登记进语言表（只在游戏语言就绪之后才真正写入）。</summary>
    internal static class LocalizationKeys
    {
        internal static void Register(string key, Terraria.Localization.LocalizedText text)
        {
            if (string.IsNullOrEmpty(key) || text == null) return;
            if (!GameLanguageReady()) return;
            try
            {
                var manager = Terraria.Localization.LanguageManager.Instance;
                if (manager == null) return;
                var texts = manager._localizedTexts;
                if (texts == null) return;
                texts[key] = text;
            }
            catch (Exception) { /* 语言表还没建：缓存数组里那份仍然有效 */ }
        }

        /// <summary>游戏的语言表建好了没有（加载期间一律不写）。</summary>
        private static bool GameLanguageReady()
        {
            try
            {
                if (!Terraria.Main._isAsyncLoadComplete) return false;
                return Terraria.Localization.LanguageManager.Instance != null;
            }
            catch { return false; }
        }
    }
}
