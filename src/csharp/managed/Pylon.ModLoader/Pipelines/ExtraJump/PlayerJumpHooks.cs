using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Audio;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Pipeline;
using Terraria;
using Terraria.Audio;

namespace Pylon.ModLoader.Pipelines;
/// <summary>额外跳跃的六个游戏入口 + 起跳音效门；热路径靠 Wanted 短路（等价基线的按需安装）。</summary>
internal static class PlayerJumpHooks
{
    private static readonly PlayerPipeline Players = PlayerPipeline.Instance;

    /// <summary>JumpMovement 的 orig 窗口；按嵌套深度复用，稳态零分配。</summary>
    private sealed class JumpScope
    {
        public Player Player = null!;
        public ExtraJumpTable.PlayerJumps Jumps = null!;
        public readonly bool[] Before = new bool[VanillaCount];
        public readonly bool[] Announced = new bool[VanillaCount];
    }

    private static readonly List<JumpScope> Scopes = [];
    private static int _depth;

    private static JumpScope Current => Scopes[_depth - 1];

    private static JumpScope Enter(Player self, ExtraJumpTable.PlayerJumps jumps)
    {
        if (_depth == Scopes.Count) Scopes.Add(new JumpScope());
        var scope = Scopes[_depth++];
        scope.Player = self;
        scope.Jumps = jumps;
        for (var i = 0; i < VanillaCount; i++) scope.Before[i] = Native(i)?.ReadIsPerforming(self) ?? false;
        Array.Clear(scope.Announced);
        return scope;
    }

    private static readonly HookId[] GateHooks =
    [
        HookId.ModifyExtraJumpDurationMultiplier,
        HookId.CanStartExtraJump,
        HookId.OnExtraJumpStarted,
        HookId.OnExtraJumpEnded,
        HookId.OnExtraJumpRefreshed,
        HookId.ExtraJumpVisuals,
        HookId.CanShowExtraJumpVisuals,
        HookId.OnExtraJumpCleared,
    ];

    internal static void Install()
    {
        On.Terraria.Player.ResetEffects += ResetEffects;
        On.Terraria.Player.UpdateJumpHeight += UpdateJumpHeight;
        On.Terraria.Player.RefreshDoubleJumps += RefreshDoubleJumps;
        On.Terraria.Player.JumpMovement += JumpMovement;
        On.Terraria.Player.CancelAllJumpVisualEffects += CancelAllJumpVisualEffects;
        On.Terraria.Player.DoubleJumpVisuals += DoubleJumpVisuals;
        On.Terraria.Audio.SoundEngine.PlaySound_int_int_int_int_float_float += PlaySoundAt;
        On.Terraria.Audio.SoundEngine.PlaySound_LegacySoundStyle_Vector2_float_float += PlaySoundAtStyle;
    }

    private static int VanillaCount => ExtraJumpTable.DefaultCount;

    internal static bool Wanted()
    {
        if (ExtraJumpTable.HasModded) return true;
        for (var i = 0; i < GateHooks.Length; i++)
        {
            if (Players.ModsActive(GateHooks[i])) return true;
        }
        return false;
    }

    private static VanillaExtraJump? Native(int index) =>
        ExtraJumpTable.ExtraJumps[index] is VanillaExtraJump { Native: true } jump ? jump : null;

    private static void ResetEffects(On.Terraria.Player.orig_ResetEffects orig, Player self)
    {
        orig(self);
        if (!Wanted()) return;
        ExtraJumpTable.ResetEnableFlags(self, ExtraJumpTable.Of(self));
    }

    private static void UpdateJumpHeight(On.Terraria.Player.orig_UpdateJumpHeight orig, Player self)
    {
        if (!Wanted()) { orig(self); return; }
        var jumps = ExtraJumpTable.Of(self);
        ExtraJumpTable.Sync(self, jumps);
        var blocked = self.mount.Active && self.mount.BlockExtraJumps;
        var grounded = self.velocity.Y == 0f || self.sliding;
        for (var i = 0; i < VanillaCount; i++)
        {
            if (Native(i) is not { } jump) continue;
            if (jumps.States[i]._disabled && jump.ReadIsPerforming(self))
            {
                jump.WriteIsPerforming(self, false);
                self.jump = 0;
                ExtraJumpTable.EachEnded(self, jump);
            }
            if (jump.ReadCanJumpAgain(self) && (blocked || (!grounded && !jump.ReadHasOption(self))))
                ExtraJumpTable.EachCleared(self, jump);
        }
        if (blocked) ExtraJumpTable.ConsumeModded(self, jumps);
        else if (!grounded) ExtraJumpTable.ConsumeAndStopUnavailableModded(self, jumps);
        ExtraJumpTable.PullAll(self, jumps);
        orig(self);
    }

    private static void RefreshDoubleJumps(On.Terraria.Player.orig_RefreshDoubleJumps orig, Player self)
    {
        orig(self);
        if (!Wanted()) return;
        var jumps = ExtraJumpTable.Of(self);
        ExtraJumpTable.PullAll(self, jumps);
        foreach (var jump in ExtraJumpTable.OrderedJumps)
        {
            if (Native(jump.Type) is { } vanilla && vanilla.ReadHasOption(self))
                ExtraJumpTable.EachRefreshed(self, jump);
        }
        ExtraJumpTable.RefreshModded(self, jumps);
    }

    private static bool Attempting(Player self)
    {
        if (!self.controlJump || self.jump != 0 || !self.releaseJump) return false;
        if (self.velocity.Y == 0f || self.sliding) return false;
        if (self.wet && self.accFlipper && (!self.mount.Active || !self.mount.Cart)) return false;
        if (self.mount.Active && self.mount.IsConsideredASlimeMount && self.wetSlime > 0) return false;
        if (self.hasDeadCellsDownDash && self.controlDown && !self.isPerformingJump_DownDash && !self.mount.Active) return false;
        return true;
    }

    private static void JumpMovement(On.Terraria.Player.orig_JumpMovement orig, Player self)
    {
        if (!Wanted()) { orig(self); return; }
        var jumps = ExtraJumpTable.Of(self);
        ExtraJumpTable.Sync(self, jumps);
        var outer = _depth;
        var scope = Enter(self, jumps);
        Span<bool> hidden = stackalloc bool[VanillaCount];
        hidden.Clear();
        if (Attempting(self))
        {
            ExtraJump? chosen = null;
            if (!jumps.Block)
            {
                foreach (var jump in ExtraJumpTable.OrderedJumps)
                {
                    if (jump == ExtraJump.Flipper || !jumps.States[jump.Type].Available || !ExtraJumpTable.CanStart(self, jump)) continue;
                    chosen = jump;
                    break;
                }
            }
            for (var i = 0; i < VanillaCount; i++)
            {
                if (Native(i) is not { } jump || ExtraJumpTable.ExtraJumps[i] == chosen || !jump.ReadCanJumpAgain(self)) continue;
                jump.WriteCanJumpAgain(self, false);
                hidden[i] = true;
            }
            if (chosen != null && chosen.Type >= VanillaCount)
            {
                ExtraJumpTable.PerformJump(chosen, self, jumps);
                self.releaseJump = false;
            }
        }
        try { orig(self); }
        finally
        {
            _depth = outer;
            for (var i = 0; i < VanillaCount; i++)
            {
                if (hidden[i]) Native(i)!.WriteCanJumpAgain(self, true);
            }
        }
        for (var i = 0; i < VanillaCount; i++)
        {
            if (Native(i) is not { } jump) continue;
            var now = jump.ReadIsPerforming(self);
            if (now && !scope.Before[i])
            {
                var duration = ExtraJumpTable.Duration(self, jump);
                if (duration != jump.GetDurationMultiplier(self)) self.jump = Math.Max(0, (int)(Player.jumpHeight * duration));
                Announce(scope, i);
            }
            else if (!now && scope.Before[i]) ExtraJumpTable.EachEnded(self, jump);
        }
        ExtraJumpTable.PullAll(self, jumps);
    }

    private static bool Announce(JumpScope scope, int index)
    {
        if (scope.Announced[index]) return true;
        scope.Announced[index] = true;
        return ExtraJumpTable.Started(scope.Player, ExtraJumpTable.ExtraJumps[index], true);
    }

    private static bool SoundAllowed()
    {
        if (_depth == 0) return true;
        if (!Players.ModsActive(HookId.OnExtraJumpStarted)) return true;
        var scope = Current;
        var sound = true;
        for (var i = 0; i < VanillaCount; i++)
        {
            if (Native(i) is not { } jump || scope.Before[i] || scope.Announced[i] || !jump.ReadIsPerforming(scope.Player)) continue;
            if (!Announce(scope, i)) sound = false;
        }
        return sound;
    }

    private static SoundEffectInstance PlaySoundAt(On.Terraria.Audio.SoundEngine.orig_PlaySound_int_int_int_int_float_float orig,
                                                   int type, int x, int y, int style, float volumeScale, float pitchOffset) =>
        SoundAllowed() ? orig(type, x, y, style, volumeScale, pitchOffset) : null!;

    private static SoundEffectInstance PlaySoundAtStyle(On.Terraria.Audio.SoundEngine.orig_PlaySound_LegacySoundStyle_Vector2_float_float orig,
                                                        LegacySoundStyle type, Vector2 position, float pitchOffset, float volumeScale) =>
        SoundAllowed() ? orig(type, position, pitchOffset, volumeScale) : null!;

    private static void CancelAllJumpVisualEffects(On.Terraria.Player.orig_CancelAllJumpVisualEffects orig, Player self, bool includeDownDash)
    {
        if (!Wanted()) { orig(self, includeDownDash); return; }
        Span<bool> before = stackalloc bool[VanillaCount];
        for (var i = 0; i < VanillaCount; i++) before[i] = Native(i)?.ReadIsPerforming(self) ?? false;
        orig(self, includeDownDash);
        var jumps = ExtraJumpTable.Of(self);
        for (var i = 0; i < VanillaCount; i++)
        {
            if (before[i] && Native(i) is { } jump && !jump.ReadIsPerforming(self)) ExtraJumpTable.EachEnded(self, jump);
        }
        ExtraJumpTable.StopModdedJumps(self, jumps);
        ExtraJumpTable.PullAll(self, jumps);
    }

    private static void DoubleJumpVisuals(On.Terraria.Player.orig_DoubleJumpVisuals orig, Player self)
    {
        if (!Wanted()) { orig(self); return; }
        var jumps = ExtraJumpTable.Of(self);
        Span<bool> hidden = stackalloc bool[VanillaCount];
        hidden.Clear();
        for (var i = 0; i < VanillaCount; i++)
        {
            if (Native(i) is not { } jump || !jump.ReadIsPerforming(self)) continue;
            if (!ExtraJumpTable.AllCanShowVisuals(self, jump))
            {
                hidden[i] = true;
                jump.WriteIsPerforming(self, false);
            }
            else ExtraJumpTable.EachVisuals(self, jump);
        }
        try { orig(self); }
        finally
        {
            for (var i = 0; i < VanillaCount; i++)
            {
                if (hidden[i]) Native(i)!.WriteIsPerforming(self, true);
            }
        }
        ExtraJumpTable.ModdedVisuals(self, jumps);
    }
}
