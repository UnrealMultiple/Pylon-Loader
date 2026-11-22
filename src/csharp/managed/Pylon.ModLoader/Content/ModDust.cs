using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Pipeline;
using Pylon.ModLoader.Pipelines;
using Terraria;

namespace Pylon.ModLoader.Content;
/// <summary>
/// 自定义尘土。mod 尘土真进游戏的 <c>Main.dust</c> 池：类型号从原版表长（332）起按注册序发，
/// 原版表随号段扩容，原版更新循环跑物理与消亡，管线在循环前后派发 <see cref="Update"/> / <see cref="MidUpdate"/>，绘制由管线的自开批次负责。
/// 用 <see cref="Spawn"/> 生成，用 <see cref="Update"/> 控制寿命；<c>dust.active = false</c> = 杀不变。
/// </summary>
public abstract class ModDust : ModType
{
    /// <summary>相对 ModDust 覆写了哪些钩子：注册时算一次挂在实例上，分派期只做位运算。</summary>
    internal HookMask Hooks { get; set; }

    /// <summary>这个钩子要不要分派给本实例。</summary>
    internal bool HookEnabled(HookId hook) => Hooks.Has(hook);

    /// <summary>贴图相对 Mod 目录的路径（默认 assets/dusts/&lt;Name&gt;.png）。</summary>
    public override string TexturePath => $"assets/dusts/{Name}.png";

    /// <summary>生成一枚本类型的尘土；返回 Dust 实例，可继续改字段。池满（6000 槽用尽）时静默返回 null。</summary>
    public Dust? Spawn(Vector2 position, Vector2 velocity, int alpha = 0, Color? color = null, float scale = 1f) =>
        DustPipeline.Spawn(this, position, velocity, alpha, color, scale);

    /// <summary>生成时一次（Dust.NewDust 池内落位之后由管线触发；color 缺省随原版 default(Color)）。</summary>
    public virtual void OnSpawn(Dust dust) { }

    /// <summary>每帧一次，先于原版更新循环；返回 true = 交给原版物理；返回 false = 本帧藏起跳过原版循环，尘土存活、下一帧继续派发；要回收尘土请置 dust.active = false。</summary>
    public virtual bool Update(Dust dust) => true;

    /// <summary>原版更新循环之后（Update 返回 false 被藏起的尘土同样会收到）；返回 true = 抵消原版循环刚做的横向刹车（velocity.X /= 0.99），缺省与异常都不抵消。</summary>
    public virtual bool MidUpdate(Dust dust) => false;

    /// <summary>画之前（先于管线批次里的常规绘制，作者在这里画的会垫在下面）；false = 管线本帧不画这张精灵。</summary>
    public virtual bool PreDraw(Dust dust) => true;

    /// <summary>这个尘土画出来的颜色；null = 用 dust.color。lightColor 目前传的是 dust.color 而非光照色（原版 GetAlpha 接入在后续版本）。</summary>
    public virtual Color? GetAlpha(Dust dust, Color lightColor) => null;

    /// <summary>只放共享数值，不要碰游戏对象。</summary>
    public override void SetStaticDefaults() { }
}
