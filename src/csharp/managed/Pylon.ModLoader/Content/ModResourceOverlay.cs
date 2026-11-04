using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;

namespace Pylon.ModLoader;

/// <summary>
/// 资源条（生命/魔力）的覆盖层：在条画之前/之后各一次，作者往 <c>SpriteBatch</c> 里画自己的东西。
/// 坐标由作者自己算（不同分辨率下的布局由游戏决定，loader 不给坐标）。
/// </summary>
public abstract class ModResourceOverlay : ModType
{
    /// <summary>生命条画之前。</summary>
    public virtual void PreDrawLife(SpriteBatch spriteBatch) { }

    /// <summary>生命条画之后。</summary>
    public virtual void PostDrawLife(SpriteBatch spriteBatch) { }

    /// <summary>魔力条画之前。</summary>
    public virtual void PreDrawMana(SpriteBatch spriteBatch) { }

    /// <summary>魔力条画之后。</summary>
    public virtual void PostDrawMana(SpriteBatch spriteBatch) { }

    /// <summary>生命条悬停提示画之前。</summary>
    public virtual void PreDrawLifeMouseOver(SpriteBatch spriteBatch) { }

    /// <summary>生命条悬停提示画之后。</summary>
    public virtual void PostDrawLifeMouseOver(SpriteBatch spriteBatch) { }

    /// <summary>魔力条悬停提示画之前。</summary>
    public virtual void PreDrawManaMouseOver(SpriteBatch spriteBatch) { }

    /// <summary>魔力条悬停提示画之后。</summary>
    public virtual void PostDrawManaMouseOver(SpriteBatch spriteBatch) { }

    /// <summary>
    /// 单颗心 / 单颗魔力星画之前；返回 false = 这颗不画。
    /// 参数就是游戏画这一颗时手上的三个值：这条里的序号、位置、缩放（不替作者算布局）。
    /// </summary>
    public virtual bool PreDrawResource(int index, Vector2 position, float scale) => true;

    /// <summary>单颗心 / 单颗魔力星画之后。</summary>
    public virtual void PostDrawResource(int index, Vector2 position, float scale) { }

    /// <summary>只放共享数值，不要碰游戏对象。</summary>
    public override void SetStaticDefaults() { }
}
