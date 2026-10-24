using System;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent.UI;

namespace Pylon.ModLoader
{
    /// <summary>自定义表情；一个类型一个实例（类型在游戏 151 个之后；贴图是并排两个 34×28 的图标，白泡泡来自游戏的表）。</summary>
    public abstract class ModEmoteBubble : ModType
    {
        /// <summary>贴图相对 Mod 目录的路径（默认 assets/emotebubbles/&lt;Name&gt;.png）。</summary>
        public override string TexturePath => $"assets/emotebubbles/{Name}.png";

        /// <summary>当前这次调用的那个游戏 EmoteBubble（只在钩子方法里有效）。</summary>
        public EmoteBubble EmoteBubble { get; internal set; }

        /// <summary>占号之后一次，只放共享数值（AddToCategory 也在这里调）。</summary>
        public override void SetStaticDefaults() { }

        /// <summary>把它放进表情菜单的哪个分类（数字照游戏的分类号，0..6）。</summary>
        public virtual void AddToCategory(int categoryId) { }

        /// <summary>菜单里要不要出现；false = 不出现。</summary>
        public virtual bool IsUnlocked() => true;

        /// <summary>泡泡生出来时一次（EmoteBubble.NewBubble）。</summary>
        public virtual void OnSpawn() { }

        /// <summary>每帧；false = 这个泡泡自己动画（改 EmoteBubble.frame 和 frameCounter），true = 游戏那两个帧的步子。</summary>
        public virtual bool UpdateFrame() => true;

        /// <summary>贴图里取哪一块；null = 游戏那样（贴图的一半，按 EmoteBubble.frame 取）。</summary>
        public virtual Rectangle? GetFrame() => null;

        /// <summary>菜单里取哪一块（frame 是菜单的两个帧之一，frameCounter 是菜单的帧计数）；null = 贴图的一半。</summary>
        public virtual Rectangle? GetFrameInEmoteMenu(int frame, int frameCounter) => null;

        /// <summary>画这个泡泡之前；false = 不画（PostDraw 照跑）。</summary>
        public virtual bool PreDraw(SpriteBatch spriteBatch, Texture2D texture, Vector2 position, Rectangle frame,
            Vector2 origin, SpriteEffects spriteEffects) => true;

        /// <summary>画完这个泡泡之后。</summary>
        public virtual void PostDraw(SpriteBatch spriteBatch, Texture2D texture, Vector2 position, Rectangle frame,
            Vector2 origin, SpriteEffects spriteEffects) { }

        // static register 没翻：校验类、Templates.Adopt、ContentTexture、EmoteBubbleLoader.Add（占号 151+、挂绘制与菜单钩子）都是注册管线的事
    }
}
