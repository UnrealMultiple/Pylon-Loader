using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;

namespace Pylon.ModLoader;

/// <summary>
/// 自定义尘土。mod 尘土由本管线自己更新、自己画（不进游戏的图集），所以不受原版尘土类型号限制：
/// 用 <see cref="DustPipeline.Spawn"/> 生成，用 <see cref="Update"/> 控制寿命。
/// </summary>
public abstract class ModDust : ModType
{
    /// <summary>贴图相对 Mod 目录的路径（默认 assets/dusts/&lt;Name&gt;.png）。</summary>
    public override string TexturePath => $"assets/dusts/{Name}.png";

    /// <summary>生成一枚本类型的尘土；返回 Dust 实例，可继续改字段。</summary>
    public Dust? Spawn(Vector2 position, Vector2 velocity, int alpha = 0, Color? color = null, float scale = 1f) =>
        DustPipeline.Spawn(this, position, velocity, alpha, color, scale);

    /// <summary>生成时一次。</summary>
    public virtual void OnSpawn(Dust dust) { }

    /// <summary>每帧一次；返回 false = 让它消失。</summary>
    public virtual bool Update(Dust dust) => true;

    /// <summary>这个尘土画出来的颜色（默认就是 dust.color）。</summary>
    public virtual Color GetAlpha(Color lightColor) => lightColor;

    /// <summary>只放共享数值，不要碰游戏对象。</summary>
    public override void SetStaticDefaults() { }
}
