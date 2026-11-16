using System.Collections.Generic;
using Pylon.ModLoader;
using Terraria;

namespace PipelineTest.Content;

/// <summary>额外跳跃探针：计数与链路日志只在 Active 时记，避免污染平时游玩。</summary>
internal static class JumpProbe
{
    internal static bool Active;
    internal static bool VetoCloud;
    internal static float CloudFactor = 1f, TestFactor = 1f;
    internal static readonly Dictionary<string, int> Calls = [];
    internal static readonly List<string> Log = [];

    internal static void Reset()
    {
        Calls.Clear();
        Log.Clear();
        VetoCloud = false;
        CloudFactor = TestFactor = 1f;
    }

    internal static int Count(string name) => Calls.GetValueOrDefault(name);

    internal static void Add(string name)
    {
        if (Active) Calls[name] = Calls.GetValueOrDefault(name) + 1;
    }

    internal static bool Logged(string entry) => Log.Contains(entry);

    internal static void Note(string entry)
    {
        if (Active) Log.Add(entry);
    }
}

/// <summary>mod 双跳：排在原版瓶类最后（After Cloud）。</summary>
public sealed class TestJump : ExtraJump
{
    public override Position GetDefaultPosition() => new After(CloudInABottle);

    public override float GetDurationMultiplier(Player player) => 2f;

    public override void OnStarted(Player player, ref bool playSound)
    {
        JumpProbe.Add(nameof(OnStarted));
        playSound = false;
    }

    public override void OnEnded(Player player) => JumpProbe.Add(nameof(OnEnded));

    public override void OnRefreshed(Player player) => JumpProbe.Add(nameof(OnRefreshed));

    public override void ShowVisuals(Player player) => JumpProbe.Add(nameof(ShowVisuals));

    public override void UpdateHorizontalSpeeds(Player player)
    {
        JumpProbe.Add(nameof(UpdateHorizontalSpeeds));
        if (JumpProbe.Active) player.maxRunSpeed *= 2f;
    }
}

/// <summary>ModPlayer 跳跃八钩子探针。</summary>
public sealed class TestJumpPlayer : ModPlayer
{
    private static void Note(string hook, ExtraJump jump)
    {
        if (JumpProbe.Active) JumpProbe.Note($"{hook}:{jump.Name}");
    }

    public override void ModifyExtraJumpDurationMultiplier(ExtraJump jump, ref float duration)
    {
        Note(nameof(ModifyExtraJumpDurationMultiplier), jump);
        if (!JumpProbe.Active) return;
        if (jump == ExtraJump.CloudInABottle) duration *= JumpProbe.CloudFactor;
        if (jump is TestJump) duration *= JumpProbe.TestFactor;
    }

    public override bool CanStartExtraJump(ExtraJump jump)
    {
        Note(nameof(CanStartExtraJump), jump);
        return !(JumpProbe.VetoCloud && jump == ExtraJump.CloudInABottle);
    }

    public override void OnExtraJumpStarted(ExtraJump jump, ref bool playSound) => Note(nameof(OnExtraJumpStarted), jump);

    public override void OnExtraJumpEnded(ExtraJump jump) => Note(nameof(OnExtraJumpEnded), jump);

    public override void OnExtraJumpRefreshed(ExtraJump jump) => Note(nameof(OnExtraJumpRefreshed), jump);

    public override void ExtraJumpVisuals(ExtraJump jump) => Note(nameof(ExtraJumpVisuals), jump);

    public override void OnExtraJumpCleared(ExtraJump jump) => Note(nameof(OnExtraJumpCleared), jump);
}
