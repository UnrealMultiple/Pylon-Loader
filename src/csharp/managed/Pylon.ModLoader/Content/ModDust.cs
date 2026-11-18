using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Pipeline;
using Pylon.ModLoader.Pipelines;
using Terraria;

namespace Pylon.ModLoader.Content;
/// <summary>
/// 自定义尘土。mod 尘土由本管线自己更新、自己画（不进游戏的图集），所以不受原版尘土类型号限制：
/// 用 <see cref="DustPipeline.Spawn"/> 生成，用 <see cref="Update"/> 控制寿命。
/// </summary>
public abstract class ModDust : ModType
{
    /// <summary>相对 ModDust 覆写了哪些钩子：注册时算一次挂在实例上，分派期只做位运算。</summary>
    internal HookMask Hooks { get; set; }

    /// <summary>这个钩子要不要分派给本实例。</summary>
    internal bool HookEnabled(HookId hook) => Hooks.Has(hook);

    /// <summary>贴图相对 Mod 目录的路径（默认 assets/dusts/&lt;Name&gt;.png）。</summary>
    public override string TexturePath => $"assets/dusts/{Name}.png";

    /// <summary>生成一枚本类型的尘土；返回 Dust 实例，可继续改字段。</summary>
    public Dust? Spawn(Vector2 position, Vector2 velocity, int alpha = 0, Color? color = null, float scale = 1f) =>
        DustPipeline.Spawn(this, position, velocity, alpha, color, scale);

    /// <summary>生成时一次。</summary>
    public virtual void OnSpawn(Dust dust) { }

    /// <summary>每帧一次；返回 false = 让它消失。</summary>
    public virtual bool Update(Dust dust) => true;

    /// <summary>更新相位之后（管线自己的物理跑完）；返回 true = 追加一次原版的横向刹车（velocity.X /= 0.99）。</summary>
    public virtual bool MidUpdate(Dust dust) => false;

    /// <summary>画之前（先于管线批次里的常规绘制，作者在这里画的会垫在下面）；false = 管线不画这张精灵。</summary>
    public virtual bool PreDraw(Dust dust) => true;

    /// <summary>这个尘土画出来的颜色；null = 用 dust.color。</summary>
    public virtual Color? GetAlpha(Dust dust, Color lightColor) => null;

    /// <summary>只放共享数值，不要碰游戏对象。</summary>
    public override void SetStaticDefaults() { }
}
