using Pylon.ModLoader;
using Terraria;

namespace PipelineTest.Content;

internal static class PlayerUpdateProbe
{
    internal static bool Active, Veto;
    internal static int PreLuck, PostLuck, Misc, Speeds, Movement;
    internal static void Reset() { PreLuck = PostLuck = Misc = Speeds = Movement = 0; Veto = false; }
}

public sealed class TestPlayerUpdate : ModPlayer
{
    public override bool PreModifyLuck(ref float luck)
    {
        if (!PlayerUpdateProbe.Active) return true;
        PlayerUpdateProbe.PreLuck++;
        luck = 0.25f;
        return !PlayerUpdateProbe.Veto;
    }

    public override void ModifyLuck(ref float luck)
    {
        if (!PlayerUpdateProbe.Active) return;
        PlayerUpdateProbe.PostLuck++;
        luck += 0.5f;
    }

    public override void PostUpdateMiscEffects()
    {
        if (PlayerUpdateProbe.Active) PlayerUpdateProbe.Misc++;
    }

    public override void PostUpdateRunSpeeds()
    {
        if (PlayerUpdateProbe.Active) PlayerUpdateProbe.Speeds++;
    }

    public override void PreUpdateMovement()
    {
        if (PlayerUpdateProbe.Active) PlayerUpdateProbe.Movement++;
    }
}
