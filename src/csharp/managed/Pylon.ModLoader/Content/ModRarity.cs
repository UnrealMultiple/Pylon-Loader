using Il2CppMicrosoft.Xna.Framework.Graphics;

namespace Pylon.ModLoader;

/// <summary>自定义稀有度：类型在注册时排在游戏的 12 个之后，写进 item.rare。</summary>
public abstract class ModRarity : ModType
{
    /// <summary>只放共享数值，不要碰游戏对象。</summary>
    public override void SetStaticDefaults() { }

    /// <summary>稀有度的颜色（每次绘制都读，可以用 Main.DiscoR/G/B 闪）。</summary>
    public virtual Color RarityColor => Color.White;

    /// <summary>前缀把稀有度升降时的新稀有度；offset 是 -2..+2 的档位，默认不动。</summary>
    public virtual int GetPrefixedRarity(int offset, float valueMult) => Type;
}
