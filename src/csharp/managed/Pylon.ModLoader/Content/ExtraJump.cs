using System.Collections.Generic;
using Terraria;

namespace Pylon.ModLoader;

/// <summary>
/// 额外跳跃内容类：一个实例 = 一种双跳（原版五挂载 + 五瓶先占号 0..9，mod 跳跃自动接在后面）。
/// 生命线：注册占号（<see cref="ExtraJumpTable"/> 安装步）→ SetStaticDefaults。
/// </summary>
public abstract class ExtraJump : ModType
{
    public static ExtraJump Flipper { get; } = new FlipperJump();

    public static ExtraJump BasiliskMount { get; } = new BasiliskMountJump();

    public static ExtraJump GoatMount { get; } = new GoatMountJump();

    public static ExtraJump SantankMount { get; } = new SantankMountJump();

    public static ExtraJump UnicornMount { get; } = new UnicornMountJump();

    public static ExtraJump SandstormInABottle { get; } = new SandstormInABottleJump();

    public static ExtraJump BlizzardInABottle { get; } = new BlizzardInABottleJump();

    public static ExtraJump FartInAJar { get; } = new FartInAJarJump();

    public static ExtraJump TsunamiInABottle { get; } = new TsunamiInABottleJump();

    public static ExtraJump CloudInABottle { get; } = new CloudInABottleJump();

    /// <summary>排序锚点：把 mod 跳跃安插在某个原版跳跃的前/后。</summary>
    public abstract class Position
    {
        public ExtraJump Target { get; protected init; } = null!;
    }

    public sealed class Before : Position
    {
        public Before(ExtraJump parent) => Target = parent;
    }

    public sealed class After : Position
    {
        public After(ExtraJump parent) => Target = parent;
    }

    protected static readonly Position BeforeMountJumps = new Before(BasiliskMount);
    protected static readonly Position MountJumpPosition = new After(BasiliskMount);
    protected static readonly Position BeforeBottleJumps = new Before(SandstormInABottle);
    protected static readonly Position AfterBottleJumps = new After(CloudInABottle);

    public override string ToString() => Name;

    /// <summary>安插位置：返回 After/Before 某个原版跳跃（null 目标 = 最前/最后）。</summary>
    public abstract Position GetDefaultPosition();

    /// <summary>同类 mod 跳跃之间的相对顺序约束（参与拓扑排序，成环报错）。</summary>
    public virtual IEnumerable<Position> GetModdedConstraints() => null!;

    /// <summary>跳跃进行中的视觉特效。</summary>
    public virtual void ShowVisuals(Player player) { }

    /// <summary>视觉特效要不要画（默认：竖直方向上正在上升）。</summary>
    public virtual bool CanShowVisuals(Player player) =>
        (player.gravDir == 1f && player.velocity.Y < 0f) || (player.gravDir == -1f && player.velocity.Y > 0f);

    /// <summary>时长倍率（乘在 jumpHeight 上决定还能跳多久）。</summary>
    public abstract float GetDurationMultiplier(Player player);

    /// <summary>false：这次起不了这个跳。</summary>
    public virtual bool CanStart(Player player) => true;

    /// <summary>开始之后；playSound 为 false 压掉起跳音效（只对 mod 跳跃）。</summary>
    public virtual void OnStarted(Player player, ref bool playSound) { }

    /// <summary>结束之后（中止也算）。</summary>
    public virtual void OnEnded(Player player) { }

    /// <summary>跳跃进行中的水平速度修正。</summary>
    public virtual void UpdateHorizontalSpeeds(Player player) { }

    /// <summary>刷新（落地回充）之后。</summary>
    public virtual void OnRefreshed(Player player) { }
}
