using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;

namespace Pylon.ModLoader;

/// <summary>层被画的时候套在哪种变换里（与游戏"躯干/坐骑"两组变换对应；不套时画出来会与身体错位）。</summary>
[Flags]
public enum LayerTransform
{
    /// <summary>不套变换（默认）。</summary>
    None = 0,

    /// <summary>躯干组：画之前推躯干变换、画之后还原（衣服/手/配饰都在这组里）。</summary>
    Torso = 1,

    /// <summary>坐骑组：在躯干组之外再套一层（骑乘时上下位移）。</summary>
    Mount = 2,
}

/// <summary>
/// 层位：这个层插在原版绘制链的哪个位置。
/// 三件事可组合：在某层之前 / 某层之后 / 两层之间，锚点可以是原版层（<see cref="VanillaDrawLayer"/>）
/// 或另一个 mod 层的名字（<see cref="AfterMod"/> / <see cref="BeforeMod"/>）。
/// </summary>
public sealed class LayerPosition
{
    /// <summary>锚点：原版层的名字（<see cref="VanillaDrawLayer.None"/> = 不按原版层锚）。</summary>
    public VanillaDrawLayer AfterVanilla { get; private init; }

    /// <summary>锚点：原版层的名字（结束侧）。</summary>
    public VanillaDrawLayer BeforeVanilla { get; private init; }

    /// <summary>锚点：另一个 mod 层的名字（可为 null）。</summary>
    public string? AfterModLayer { get; private init; }

    /// <summary>锚点：另一个 mod 层的名字（结束侧，可为 null）。</summary>
    public string? BeforeModLayer { get; private init; }

    /// <summary>画在这一层之前。</summary>
    public static LayerPosition Before(VanillaDrawLayer layer) => new() { BeforeVanilla = layer };

    /// <summary>画在这一层之后。</summary>
    public static LayerPosition After(VanillaDrawLayer layer) => new() { AfterVanilla = layer };

    /// <summary>画在这两层之间（顺序错了会被纠正并报一次）。</summary>
    public static LayerPosition Between(VanillaDrawLayer after, VanillaDrawLayer before) =>
        new() { AfterVanilla = after, BeforeVanilla = before };

    /// <summary>画在另一个 mod 层之后（同一个位置里再按依赖排）。</summary>
    public static LayerPosition AfterMod(string layerName) => new() { AfterModLayer = layerName };

    /// <summary>画在另一个 mod 层之前（同一个位置里再按依赖排）。</summary>
    public static LayerPosition BeforeMod(string layerName) => new() { BeforeModLayer = layerName };
}

/// <summary>
/// 玩家绘制层：每个玩家画一次。
/// 两种用法：给了 <see cref="Position"/> 就插进原版绘制链的对应位置（能夹在头盔与胸甲之间）；
/// 不给就还是原来的两个层位——<see cref="DrawOverPlayer"/> 决定在整玩家之前还是之后。
/// </summary>
public abstract class ModPlayerDrawLayer : ModType
{
    /// <summary>true：画在玩家身前；false：画在玩家身后。只在 <see cref="Position"/> 为 null 时生效。</summary>
    public virtual bool DrawOverPlayer => false;

    /// <summary>插入位置；null = 用两个层位的旧行为。</summary>
    public virtual LayerPosition? Position => null;

    /// <summary>套哪种变换；只有插进链里的层用得上。</summary>
    public virtual LayerTransform Transform => LayerTransform.None;

    /// <summary>这一帧画不画（插链层用；false = 跳过，头尾变换也不用套）。</summary>
    public virtual bool Visible(PlayerDrawSet drawInfo) => true;

    /// <summary>插进链里时每帧一次；<paramref name="drawInfo"/> 是这一帧的绘制数据（可读可写）。</summary>
    public virtual void Draw(ref PlayerDrawSet drawInfo) { }

    /// <summary>两个层位的旧入口：这个玩家画之前/之后一次；<paramref name="position"/> 是玩家在屏幕上的位置。</summary>
    public virtual void Draw(SpriteBatch spriteBatch, Player player, Vector2 position, float rotation, float scale) { }

    /// <summary>只放共享数值，不要碰游戏对象。</summary>
    public override void SetStaticDefaults() { }
}
