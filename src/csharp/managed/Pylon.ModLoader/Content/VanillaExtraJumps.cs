using System.Collections.Generic;
using Terraria;

namespace Pylon.ModLoader;

/// <summary>
/// 内置跳跃基类：不参与 mod 跳跃的排序安插（位置固定在 0..9）。
/// 三个状态字段镜像到 Player 自己的字段上；本构建这些字段经代理是属性、不可 ref，
/// 用读/写访问器对替代 ref，Push/Pull 语义不变。
/// </summary>
public abstract class VanillaExtraJump : ExtraJump
{
    public sealed override Position GetDefaultPosition() => null!;

    public sealed override IEnumerable<Position> GetModdedConstraints() => null!;

    /// <summary>false：没有可镜像的 Player 字段（如 Flipper 走 wet 特判）。</summary>
    internal virtual bool Native => true;

    internal abstract bool ReadHasOption(Player player);

    internal abstract void WriteHasOption(Player player, bool value);

    internal abstract bool ReadCanJumpAgain(Player player);

    internal abstract void WriteCanJumpAgain(Player player, bool value);

    internal abstract bool ReadIsPerforming(Player player);

    internal abstract void WriteIsPerforming(Player player, bool value);
}

public sealed class FlipperJump : VanillaExtraJump
{
    public override float GetDurationMultiplier(Player player) => 1f;

    public override bool CanStart(Player player) => (!player.mount.Active || !player.mount.Cart) && player.wet;

    // Native = false：三个访问器在镜像同步里永远走不到（NativeAt 过滤），留空实现。
    internal override bool Native => false;

    internal override bool ReadHasOption(Player player) => false;

    internal override void WriteHasOption(Player player, bool value) { }

    internal override bool ReadCanJumpAgain(Player player) => false;

    internal override void WriteCanJumpAgain(Player player, bool value) { }

    internal override bool ReadIsPerforming(Player player) => false;

    internal override void WriteIsPerforming(Player player, bool value) { }
}

public sealed class BasiliskMountJump : VanillaExtraJump
{
    public override float GetDurationMultiplier(Player player) => 0.75f;

    internal override bool ReadHasOption(Player player) => player.hasJumpOption_Basilisk;
    internal override void WriteHasOption(Player player, bool value) => player.hasJumpOption_Basilisk = value;

    internal override bool ReadCanJumpAgain(Player player) => player.canJumpAgain_Basilisk;
    internal override void WriteCanJumpAgain(Player player, bool value) => player.canJumpAgain_Basilisk = value;

    internal override bool ReadIsPerforming(Player player) => player.isPerformingJump_Basilisk;
    internal override void WriteIsPerforming(Player player, bool value) => player.isPerformingJump_Basilisk = value;
}

public sealed class GoatMountJump : VanillaExtraJump
{
    public override float GetDurationMultiplier(Player player) => 2f;

    internal override bool ReadHasOption(Player player) => player.hasJumpOption_WallOfFleshGoat;
    internal override void WriteHasOption(Player player, bool value) => player.hasJumpOption_WallOfFleshGoat = value;

    internal override bool ReadCanJumpAgain(Player player) => player.canJumpAgain_WallOfFleshGoat;
    internal override void WriteCanJumpAgain(Player player, bool value) => player.canJumpAgain_WallOfFleshGoat = value;

    internal override bool ReadIsPerforming(Player player) => player.isPerformingJump_WallOfFleshGoat;
    internal override void WriteIsPerforming(Player player, bool value) => player.isPerformingJump_WallOfFleshGoat = value;
}

public sealed class SantankMountJump : VanillaExtraJump
{
    public override float GetDurationMultiplier(Player player) => 2f;

    internal override bool ReadHasOption(Player player) => player.hasJumpOption_Santank;
    internal override void WriteHasOption(Player player, bool value) => player.hasJumpOption_Santank = value;

    internal override bool ReadCanJumpAgain(Player player) => player.canJumpAgain_Santank;
    internal override void WriteCanJumpAgain(Player player, bool value) => player.canJumpAgain_Santank = value;

    internal override bool ReadIsPerforming(Player player) => player.isPerformingJump_Santank;
    internal override void WriteIsPerforming(Player player, bool value) => player.isPerformingJump_Santank = value;
}

public sealed class UnicornMountJump : VanillaExtraJump
{
    public override float GetDurationMultiplier(Player player) => 2f;

    internal override bool ReadHasOption(Player player) => player.hasJumpOption_Unicorn;
    internal override void WriteHasOption(Player player, bool value) => player.hasJumpOption_Unicorn = value;

    internal override bool ReadCanJumpAgain(Player player) => player.canJumpAgain_Unicorn;
    internal override void WriteCanJumpAgain(Player player, bool value) => player.canJumpAgain_Unicorn = value;

    internal override bool ReadIsPerforming(Player player) => player.isPerformingJump_Unicorn;
    internal override void WriteIsPerforming(Player player, bool value) => player.isPerformingJump_Unicorn = value;
}

public sealed class SandstormInABottleJump : VanillaExtraJump
{
    public override float GetDurationMultiplier(Player player) => 3f;

    internal override bool ReadHasOption(Player player) => player.hasJumpOption_Sandstorm;
    internal override void WriteHasOption(Player player, bool value) => player.hasJumpOption_Sandstorm = value;

    internal override bool ReadCanJumpAgain(Player player) => player.canJumpAgain_Sandstorm;
    internal override void WriteCanJumpAgain(Player player, bool value) => player.canJumpAgain_Sandstorm = value;

    internal override bool ReadIsPerforming(Player player) => player.isPerformingJump_Sandstorm;
    internal override void WriteIsPerforming(Player player, bool value) => player.isPerformingJump_Sandstorm = value;
}

public sealed class BlizzardInABottleJump : VanillaExtraJump
{
    public override float GetDurationMultiplier(Player player) => 1.5f;

    internal override bool ReadHasOption(Player player) => player.hasJumpOption_Blizzard;
    internal override void WriteHasOption(Player player, bool value) => player.hasJumpOption_Blizzard = value;

    internal override bool ReadCanJumpAgain(Player player) => player.canJumpAgain_Blizzard;
    internal override void WriteCanJumpAgain(Player player, bool value) => player.canJumpAgain_Blizzard = value;

    internal override bool ReadIsPerforming(Player player) => player.isPerformingJump_Blizzard;
    internal override void WriteIsPerforming(Player player, bool value) => player.isPerformingJump_Blizzard = value;
}

public sealed class FartInAJarJump : VanillaExtraJump
{
    public override float GetDurationMultiplier(Player player) => 2f;

    internal override bool ReadHasOption(Player player) => player.hasJumpOption_Fart;
    internal override void WriteHasOption(Player player, bool value) => player.hasJumpOption_Fart = value;

    internal override bool ReadCanJumpAgain(Player player) => player.canJumpAgain_Fart;
    internal override void WriteCanJumpAgain(Player player, bool value) => player.canJumpAgain_Fart = value;

    internal override bool ReadIsPerforming(Player player) => player.isPerformingJump_Fart;
    internal override void WriteIsPerforming(Player player, bool value) => player.isPerformingJump_Fart = value;
}

public sealed class TsunamiInABottleJump : VanillaExtraJump
{
    public override float GetDurationMultiplier(Player player) => 1.25f;

    internal override bool ReadHasOption(Player player) => player.hasJumpOption_Sail;
    internal override void WriteHasOption(Player player, bool value) => player.hasJumpOption_Sail = value;

    internal override bool ReadCanJumpAgain(Player player) => player.canJumpAgain_Sail;
    internal override void WriteCanJumpAgain(Player player, bool value) => player.canJumpAgain_Sail = value;

    internal override bool ReadIsPerforming(Player player) => player.isPerformingJump_Sail;
    internal override void WriteIsPerforming(Player player, bool value) => player.isPerformingJump_Sail = value;
}

public sealed class CloudInABottleJump : VanillaExtraJump
{
    public override float GetDurationMultiplier(Player player) => 0.75f;

    internal override bool ReadHasOption(Player player) => player.hasJumpOption_Cloud;
    internal override void WriteHasOption(Player player, bool value) => player.hasJumpOption_Cloud = value;

    internal override bool ReadCanJumpAgain(Player player) => player.canJumpAgain_Cloud;
    internal override void WriteCanJumpAgain(Player player, bool value) => player.canJumpAgain_Cloud = value;

    internal override bool ReadIsPerforming(Player player) => player.isPerformingJump_Cloud;
    internal override void WriteIsPerforming(Player player, bool value) => player.isPerformingJump_Cloud = value;
}
