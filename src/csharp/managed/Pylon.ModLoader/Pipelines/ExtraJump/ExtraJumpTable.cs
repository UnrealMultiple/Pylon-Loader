using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.Audio;

namespace Pylon.ModLoader;

/// <summary>一次双跳的三态开关：作者经 <see cref="ExtraJumpTable.GetJumpState"/> 直接读写。</summary>
public struct ExtraJumpState
{
    internal bool _enabled;
    internal bool _available;
    internal bool _active;
    internal bool _disabled;

    public bool Enabled => _enabled && !_disabled;

    public bool Available
    {
        get => Enabled && _available;
        set => _available = value;
    }

    public bool Active => Enabled && _active;

    public void Enable() => _enabled = true;

    public void Disable() => _disabled = true;

    internal void Start()
    {
        _available = false;
        _active = true;
    }

    internal void Stop()
    {
        _active = false;
    }

    internal void ResetEnabled()
    {
        _enabled = false;
        _disabled = false;
    }

    internal void CommitEnabledState(out bool jumpEnded)
    {
        jumpEnded = false;
        if (Enabled) return;
        jumpEnded = _active;
        _active = false;
        _available = false;
    }
}

/// <summary>额外跳跃表：发号（内置 0..9 先占，mod 跳跃接后）、排序、每玩家状态镜像与作者链。</summary>
public static class ExtraJumpTable
{
    /// <summary>按玩家指针保管的状态。</summary>
    internal sealed class PlayerJumps
    {
        public ExtraJumpState[] States = Array.Empty<ExtraJumpState>();
        public bool[] SnapEnabled = Array.Empty<bool>(), SnapAvailable = Array.Empty<bool>(), SnapActive = Array.Empty<bool>();
        public bool Block;
    }

    private static readonly PlayerPipeline Players = PlayerPipeline.Instance;

    public static int ExtraJumpCount => ExtraJumps.Count;

    internal static readonly List<ExtraJump> ExtraJumps = new()
    {
        ExtraJump.Flipper,
        ExtraJump.BasiliskMount,
        ExtraJump.GoatMount,
        ExtraJump.SantankMount,
        ExtraJump.UnicornMount,
        ExtraJump.SandstormInABottle,
        ExtraJump.BlizzardInABottle,
        ExtraJump.FartInAJar,
        ExtraJump.TsunamiInABottle,
        ExtraJump.CloudInABottle,
    };

    private static readonly int DefaultExtraJumpCount = ExtraJumps.Count;
    private static readonly Dictionary<nint, PlayerJumps> JumpsByPointer = [];
    private static ExtraJump[]? _ordered;

    static ExtraJumpTable()
    {
        for (var i = 0; i < ExtraJumps.Count; i++) ExtraJumps[i].Type = i;
    }

    public static IReadOnlyList<ExtraJump> OrderedJumps => _ordered ??= Order();

    internal static bool HasModded => ExtraJumps.Count > DefaultExtraJumpCount;

    internal static int DefaultCount => DefaultExtraJumpCount;

    private static int Add(ExtraJump jump)
    {
        lock (ExtraJumps)
        {
            jump.Type = ExtraJumps.Count;
            ExtraJumps.Add(jump);
            _ordered = null;
        }
        return jump.Type;
    }

    internal static void Build(IModLog log)
    {
        var added = 0;
        foreach (var jump in ModContent.GetContent<ExtraJump>())
        {
            if (ExtraJumps.Contains(jump)) continue;
            Add(jump);
            added++;
        }

        for (var i = DefaultExtraJumpCount; i < ExtraJumps.Count; i++)
        {
            var jump = ExtraJumps[i];
            Safe.Run(jump.Name + ".SetStaticDefaults", jump.SetStaticDefaults);
        }

        _ordered = Order();
        log.Info($"[jump] 内置 {DefaultExtraJumpCount}，mod 新增 {added}，排序表 {OrderedJumps.Count}");
    }

    public static ExtraJump? Get(int index) => index < 0 || index >= ExtraJumpCount ? null : ExtraJumps[index];

    private static ExtraJump[] Order()
    {
        if (!HasModded) return ExtraJumps.ToArray();
        var slots = new List<ExtraJump>[DefaultExtraJumpCount + 1];
        for (var i = 0; i < slots.Length; i++) slots[i] = [];
        for (var i = DefaultExtraJumpCount; i < ExtraJumps.Count; i++)
        {
            var jump = ExtraJumps[i];
            switch (jump.GetDefaultPosition())
            {
                case ExtraJump.After after:
                    if (after.Target is not null and not VanillaExtraJump)
                        throw new ArgumentException($"ExtraJump {jump} 的 GetDefaultPosition() 没有指向原版跳跃");
                    slots[after.Target is null ? 0 : after.Target.Type + 1].Add(jump);
                    break;
                case ExtraJump.Before before:
                    if (before.Target is not null and not VanillaExtraJump)
                        throw new ArgumentException($"ExtraJump {jump} 的 GetDefaultPosition() 没有指向原版跳跃");
                    slots[before.Target is null ? slots.Length - 1 : before.Target.Type].Add(jump);
                    break;
                default:
                    throw new ArgumentException($"ExtraJump {jump} 的 GetDefaultPosition() 返回了未知位置");
            }
        }
        var sorted = new List<ExtraJump>();
        for (var i = 0; i < slots.Length; i++)
        {
            sorted.AddRange(Sort(slots[i]));
            if (i < DefaultExtraJumpCount) sorted.Add(ExtraJumps[i]);
        }
        return sorted.ToArray();
    }

    private static List<ExtraJump> Sort(List<ExtraJump> elements)
    {
        var after = elements.ToDictionary(e => e, _ => new HashSet<ExtraJump>());
        foreach (var jump in elements)
        {
            foreach (var position in jump.GetModdedConstraints() ?? Enumerable.Empty<ExtraJump.Position>())
            {
                if (!after.ContainsKey(position.Target)) continue;
                if (position is ExtraJump.After) after[jump].Add(position.Target);
                else if (position is ExtraJump.Before) after[position.Target].Add(jump);
            }
        }
        var result = new List<ExtraJump>();
        var left = new List<ExtraJump>(elements);
        while (left.Count > 0)
        {
            var next = left.FirstOrDefault(j => after[j].All(result.Contains));
            if (next == null) throw new ArgumentException($"ExtraJump 排序成环：{string.Join(", ", left)}");
            result.Add(next);
            left.Remove(next);
        }
        return result;
    }

    internal static PlayerJumps Of(Player player)
    {
        if (JumpsByPointer.TryGetValue(player.Pointer, out var jumps) && jumps.States.Length == ExtraJumpCount) return jumps;

        jumps ??= new PlayerJumps();
        var count = ExtraJumpCount;
        Array.Resize(ref jumps.States, count);
        Array.Resize(ref jumps.SnapEnabled, count);
        Array.Resize(ref jumps.SnapAvailable, count);
        Array.Resize(ref jumps.SnapActive, count);
        JumpsByPointer[player.Pointer] = jumps;
        for (var i = 0; i < DefaultExtraJumpCount; i++) Pull(player, jumps, i);
        return jumps;
    }

    private static VanillaExtraJump? NativeAt(int index) =>
        index < DefaultExtraJumpCount && ExtraJumps[index] is VanillaExtraJump { Native: true } v ? v : null;

    private static void Push(Player player, PlayerJumps jumps, int index)
    {
        if (NativeAt(index) is not { } jump) return;
        var state = jumps.States[index];
        if (state._disabled)
        {
            jump.WriteHasOption(player, false);
            jump.WriteCanJumpAgain(player, false);
        }
        else if (state._enabled && !jumps.SnapEnabled[index]) jump.WriteHasOption(player, true);
        if (state._available != jumps.SnapAvailable[index]) jump.WriteCanJumpAgain(player, state._available);
        if (state._active != jumps.SnapActive[index]) jump.WriteIsPerforming(player, state._active);
    }

    private static void Pull(Player player, PlayerJumps jumps, int index)
    {
        if (NativeAt(index) is not { } jump) return;
        var state = jumps.States[index];
        state._enabled = jumps.SnapEnabled[index] = jump.ReadHasOption(player);
        state._available = jumps.SnapAvailable[index] = jump.ReadCanJumpAgain(player);
        state._active = jumps.SnapActive[index] = jump.ReadIsPerforming(player);
        jumps.States[index] = state;
    }

    internal static void Sync(Player player, PlayerJumps jumps)
    {
        for (var i = 0; i < DefaultExtraJumpCount; i++)
        {
            Push(player, jumps, i);
            Pull(player, jumps, i);
        }
    }

    internal static void PullAll(Player player, PlayerJumps jumps)
    {
        for (var i = 0; i < DefaultExtraJumpCount; i++) Pull(player, jumps, i);
    }

    public static ref ExtraJumpState GetJumpState(Player player, ExtraJump jump)
    {
        var jumps = Of(player);
        if (jump.Type < DefaultExtraJumpCount)
        {
            Push(player, jumps, jump.Type);
            Pull(player, jumps, jump.Type);
        }
        return ref jumps.States[jump.Type];
    }

    public static ref ExtraJumpState GetJumpState<T>(Player player) where T : ExtraJump =>
        ref GetJumpState(player, ModContent.GetInstance<T>() ?? throw new InvalidOperationException(
            $"{typeof(T).FullName} 没有注册（要派生对应 ExtraJump 且被自动发现）"));

    public static Span<ExtraJumpState> ExtraJumpStates(Player player)
    {
        var jumps = Of(player);
        Sync(player, jumps);
        return jumps.States.AsSpan();
    }

    public static bool GetBlockExtraJumps(Player player) => Of(player).Block;

    public static void SetBlockExtraJumps(Player player, bool value) => Of(player).Block = value;

    public static bool AnyExtraJumpUsable(Player player)
    {
        var jumps = Of(player);
        if (jumps.Block) return false;
        Sync(player, jumps);
        foreach (var jump in OrderedJumps)
        {
            if (jumps.States[jump.Type].Available && CanStart(player, jump)) return true;
        }
        return false;
    }

    public static void StopExtraJumpInProgress(Player player)
    {
        var jumps = Of(player);
        Sync(player, jumps);
        var anyCancelled = false;
        foreach (var jump in OrderedJumps)
        {
            if (!jumps.States[jump.Type].Active) continue;
            StopJump(jump, player, jumps);
            anyCancelled = true;
        }
        Sync(player, jumps);
        if (anyCancelled) player.jump = 0;
    }

    public static void ConsumeAllExtraJumps(Player player)
    {
        var jumps = Of(player);
        Sync(player, jumps);
        foreach (var jump in ExtraJumps)
        {
            if (!jumps.States[jump.Type].Available) continue;
            EachCleared(player, jump);
            jumps.States[jump.Type].Available = false;
        }
        Sync(player, jumps);
    }

    public static void UpdateHorizontalSpeeds(Player player)
    {
        if (!HasModded) return;
        var jumps = Of(player);
        foreach (var jump in OrderedJumps)
        {
            if (jump.Type >= DefaultExtraJumpCount && jumps.States[jump.Type].Active) jump.UpdateHorizontalSpeeds(player);
        }
    }

    private static void Report(ModPlayer mod, HookId hook, Exception e) =>
        ContentErrors.Report($"[jump] {mod.GetType().Name}.{hook} 异常:{Safe.Describe(e)}");

    internal static bool CanStart(Player player, ExtraJump jump)
    {
        if (!jump.CanStart(player)) return false;
        if (!Players.ModsActive(HookId.CanStartExtraJump)) return true;
        var result = true;
        var mods = Players.ModsOf(player);
        foreach (var mod in mods)
        {
            if (!mod.HookEnabled(HookId.CanStartExtraJump)) continue;
            try { result &= mod.CanStartExtraJump(jump); }
            catch (Exception e) { Report(mod, HookId.CanStartExtraJump, e); }
        }
        return result;
    }

    internal static float Duration(Player player, ExtraJump jump)
    {
        var duration = jump.GetDurationMultiplier(player);
        if (!Players.ModsActive(HookId.ModifyExtraJumpDurationMultiplier)) return duration;
        var mods = Players.ModsOf(player);
        foreach (var mod in mods)
        {
            if (!mod.HookEnabled(HookId.ModifyExtraJumpDurationMultiplier)) continue;
            try { mod.ModifyExtraJumpDurationMultiplier(jump, ref duration); }
            catch (Exception e) { Report(mod, HookId.ModifyExtraJumpDurationMultiplier, e); }
        }
        return duration;
    }

    internal static bool Started(Player player, ExtraJump jump, bool vanilla)
    {
        var playSound = true;
        if (!vanilla) jump.OnStarted(player, ref playSound);
        if (Players.ModsActive(HookId.OnExtraJumpStarted))
        {
            var mods = Players.ModsOf(player);
            foreach (var mod in mods)
            {
                if (!mod.HookEnabled(HookId.OnExtraJumpStarted)) continue;
                try { mod.OnExtraJumpStarted(jump, ref playSound); }
                catch (Exception e) { Report(mod, HookId.OnExtraJumpStarted, e); }
            }
        }
        return playSound;
    }

    internal static void EachEnded(Player player, ExtraJump jump)
    {
        if (!Players.ModsActive(HookId.OnExtraJumpEnded)) return;
        var mods = Players.ModsOf(player);
        foreach (var mod in mods)
        {
            if (!mod.HookEnabled(HookId.OnExtraJumpEnded)) continue;
            try { mod.OnExtraJumpEnded(jump); }
            catch (Exception e) { Report(mod, HookId.OnExtraJumpEnded, e); }
        }
    }

    internal static void EachRefreshed(Player player, ExtraJump jump)
    {
        if (!Players.ModsActive(HookId.OnExtraJumpRefreshed)) return;
        var mods = Players.ModsOf(player);
        foreach (var mod in mods)
        {
            if (!mod.HookEnabled(HookId.OnExtraJumpRefreshed)) continue;
            try { mod.OnExtraJumpRefreshed(jump); }
            catch (Exception e) { Report(mod, HookId.OnExtraJumpRefreshed, e); }
        }
    }

    internal static void EachVisuals(Player player, ExtraJump jump)
    {
        if (!Players.ModsActive(HookId.ExtraJumpVisuals)) return;
        var mods = Players.ModsOf(player);
        foreach (var mod in mods)
        {
            if (!mod.HookEnabled(HookId.ExtraJumpVisuals)) continue;
            try { mod.ExtraJumpVisuals(jump); }
            catch (Exception e) { Report(mod, HookId.ExtraJumpVisuals, e); }
        }
    }

    internal static bool AllCanShowVisuals(Player player, ExtraJump jump)
    {
        if (!Players.ModsActive(HookId.CanShowExtraJumpVisuals)) return true;
        var result = true;
        var mods = Players.ModsOf(player);
        foreach (var mod in mods)
        {
            if (!mod.HookEnabled(HookId.CanShowExtraJumpVisuals)) continue;
            try { result &= mod.CanShowExtraJumpVisuals(jump); }
            catch (Exception e) { Report(mod, HookId.CanShowExtraJumpVisuals, e); }
        }
        return result;
    }

    internal static void EachCleared(Player player, ExtraJump jump)
    {
        if (!Players.ModsActive(HookId.OnExtraJumpCleared)) return;
        var mods = Players.ModsOf(player);
        foreach (var mod in mods)
        {
            if (!mod.HookEnabled(HookId.OnExtraJumpCleared)) continue;
            try { mod.OnExtraJumpCleared(jump); }
            catch (Exception e) { Report(mod, HookId.OnExtraJumpCleared, e); }
        }
    }

    internal static void ModdedVisuals(Player player, PlayerJumps jumps)
    {
        foreach (var jump in OrderedJumps)
        {
            if (jump.Type < DefaultExtraJumpCount || !jumps.States[jump.Type].Active || !jump.CanShowVisuals(player)) continue;
            if (!AllCanShowVisuals(player, jump)) continue;
            jump.ShowVisuals(player);
            EachVisuals(player, jump);
        }
    }

    internal static void RefreshModded(Player player, PlayerJumps jumps)
    {
        foreach (var jump in OrderedJumps)
        {
            if (jump.Type < DefaultExtraJumpCount || !jumps.States[jump.Type].Enabled) continue;
            jump.OnRefreshed(player);
            EachRefreshed(player, jump);
            jumps.States[jump.Type].Available = true;
        }
    }

    internal static void StopModdedJumps(Player player, PlayerJumps jumps)
    {
        for (var i = DefaultExtraJumpCount; i < jumps.States.Length; i++)
        {
            if (jumps.States[i].Active) StopJump(ExtraJumps[i], player, jumps);
        }
    }

    internal static void ResetEnableFlags(Player player, PlayerJumps jumps)
    {
        jumps.Block = false;
        for (var i = 0; i < jumps.States.Length; i++) jumps.States[i].ResetEnabled();
        PullAll(player, jumps);
    }

    internal static void ConsumeAndStopUnavailableModded(Player player, PlayerJumps jumps)
    {
        for (var i = DefaultExtraJumpCount; i < jumps.States.Length; i++)
        {
            var state = jumps.States[i];
            if (state._available && !state.Enabled) EachCleared(player, ExtraJumps[i]);
            state.CommitEnabledState(out var jumpEnded);
            jumps.States[i] = state;
            if (!jumpEnded) continue;
            StopJump(ExtraJumps[i], player, jumps);
            player.jump = 0;
        }
    }

    internal static void ConsumeModded(Player player, PlayerJumps jumps)
    {
        for (var i = DefaultExtraJumpCount; i < jumps.States.Length; i++)
        {
            if (!jumps.States[i].Available) continue;
            EachCleared(player, ExtraJumps[i]);
            jumps.States[i].Available = false;
        }
    }

    internal static void PerformJump(ExtraJump jump, Player player, PlayerJumps jumps)
    {
        jumps.States[jump.Type].Start();
        var duration = Duration(player, jump);
        var velocity = player.velocity;
        velocity.Y = -Player.jumpSpeed * player.gravDir;
        player.velocity = velocity;
        player.jump = (int)(Player.jumpHeight * duration);
        if (Started(player, jump, false)) Terraria.Audio.SoundEngine.PlaySound(16, (int)player.position.X, (int)player.position.Y, 1, 1f, 0f);
    }

    private static void StopJump(ExtraJump jump, Player player, PlayerJumps jumps)
    {
        if (jump.Type >= DefaultExtraJumpCount) jump.OnEnded(player);
        EachEnded(player, jump);
        jumps.States[jump.Type].Stop();
    }
}
