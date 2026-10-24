using System;
using Il2CppMicrosoft.Xna.Framework.Graphics;

namespace Pylon.ModLoader
{
    /// <summary>自定义表层背景风格：抢到场景时在游戏 16 个风格之后占一个编号，挑各层贴图、管透明度。</summary>
    public abstract class ModSurfaceBackgroundStyle : ModType
    {
        /// <summary>风格编号：游戏 16 个（SurfaceBackgroundID）之后从 17 开始，Main.bgStyle 会变成它；管线按注册顺序分。</summary>
        public int Slot { get; internal set; }

        /// <summary>每帧在屏幕上时调；fades = Main.bgAlphaFrontLayer，本风格加 transitionSpeed、别的减，各自夹在 0..1。</summary>
        public virtual void ModifyFarFades(float[] fades, float transitionSpeed)
        {
            for (var i = 0; i < fades.Length; i++)
            {
                fades[i] = i == Slot
                    ? Math.Min(1f, fades[i] + transitionSpeed)
                    : Math.Max(0f, fades[i] - transitionSpeed);
            }
        }

        /// <summary>最远的贴图层（中景山后面）的贴图编号；-1 = 不画这层。每帧调，可以动画。</summary>
        public virtual int ChooseFarTexture() => -1;

        /// <summary>中间的贴图层（背景树后面）的贴图编号；-1 = 不画这层。每帧调，可以动画。</summary>
        public virtual int ChooseMiddleTexture() => -1;

        /// <summary>最前面那层画之前；false = 不画这层，自己画的画进 spriteBatch。</summary>
        public virtual bool PreDrawCloseBackground(SpriteBatch spriteBatch) => true;

        /// <summary>最前面那层的贴图编号和参数（可改；默认 scale 1.25、parallax 0.37、a 1800、b 1750；高度 = a × 位置 + b）；-1 = 不画。</summary>
        public virtual int ChooseCloseTexture(ref float scale, ref float parallax, ref float a, ref float b) => -1;

        /// <summary>只放共享数值，不要碰游戏对象。</summary>
        public override void SetStaticDefaults() { }

        // register 故意没翻：Pylon 的注册由 ModType 发现自动做，编号分配与 SetStaticDefaults 的调用在管线里。
    }
}