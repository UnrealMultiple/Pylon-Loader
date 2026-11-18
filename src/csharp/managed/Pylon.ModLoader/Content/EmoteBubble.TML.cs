using Terraria.GameContent.UI;

namespace Pylon.ModLoader;

/// <summary>表情气泡的扩展成员（游戏类型来自 interop 代理，用 extension block 补作者 API）。</summary>
public static class EmoteBubbleTml
{
    extension(EmoteBubble bubble)
    {
        /// <summary>这个气泡编号对应的 ModEmoteBubble 模板；原版表情返回 null。</summary>
        public ModEmoteBubble? ModEmoteBubble => EmoteBubbleTable.Of(bubble.emote);
    }
}
