using System;
using Pylon.ModLoader;
using Terraria;
using Terraria.ID;

namespace PipelineTest.Content;

internal static class TileInteractionProbe
{
    internal static bool Active, Allow = true, Manual, Throw, GlobalVeto, ThrowGlobal;
    internal static int Shift = 18, Locks, Unlocks, Sitting, Sleeping, Slopes, GlobalSlopes;
    internal static string Order = "";
    internal static void Reset()
    {
        Allow = true;
        Manual = Throw = GlobalVeto = ThrowGlobal = false;
        Locks = Unlocks = Sitting = Sleeping = Slopes = GlobalSlopes = 0;
        Shift = 18;
        Order = "";
    }
}

public sealed class TestTileInteraction : ModTile
{
    public override string TexturePath => "assets/tiles/TestTileBlock.png";
    public override void SetStaticDefaults()
    {
        TileID.Sets.CanBeSatOnForPlayers[Type] = true;
        TileID.Sets.CanBeSleptIn[Type] = true;
        Main.tileSolid[Type] = true;
    }

    public override bool IsLockedChest(int i, int j)
    {
        if (TileInteractionProbe.Active && TileInteractionProbe.Throw) throw new InvalidOperationException("lock probe");
        return Main.tile[i, j].frameX != 0;
    }

    public override bool LockChest(int i, int j, ref int frameXAdjustment, ref bool manual)
    {
        if (!TileInteractionProbe.Active) return false;
        TileInteractionProbe.Locks++;
        if (TileInteractionProbe.Throw) throw new InvalidOperationException("lock probe");
        frameXAdjustment = TileInteractionProbe.Shift;
        manual = TileInteractionProbe.Manual;
        if (manual && TileInteractionProbe.Allow)
        {
            var tile = Main.tile[i, j];
            tile.frameX = 123;
        }
        return TileInteractionProbe.Allow;
    }

    public override bool UnlockChest(int i, int j, ref int frameXAdjustment, ref int dustType, ref bool manual)
    {
        if (!TileInteractionProbe.Active) return false;
        TileInteractionProbe.Unlocks++;
        if (TileInteractionProbe.Throw) throw new InvalidOperationException("unlock probe");
        frameXAdjustment = -TileInteractionProbe.Shift;
        dustType = -1;
        manual = TileInteractionProbe.Manual;
        if (manual && TileInteractionProbe.Allow)
        {
            var tile = Main.tile[i, j];
            tile.frameX = 0;
        }
        return TileInteractionProbe.Allow;
    }

    public override void ModifySittingTargetInfo(int i, int j, TileRestingInfo info)
    {
        if (!TileInteractionProbe.Active) return;
        TileInteractionProbe.Sitting++;
        Modify(info);
    }

    public override void ModifySleepingTargetInfo(int i, int j, TileRestingInfo info)
    {
        if (!TileInteractionProbe.Active) return;
        TileInteractionProbe.Sleeping++;
        Modify(info);
    }

    private static void Modify(TileRestingInfo info)
    {
        info.AnchorTilePosition = new(10, 20);
        info.TargetDirection = -1;
        info.DirectionOffset = 3;
        info.FinalOffset = new(4, 5);
        info.VisualOffset = new(6, 7);
        info.ExtraInfo = new() { IsAToilet = true };
        if (TileInteractionProbe.Throw) throw new InvalidOperationException("resting probe");
    }

    public override bool Slope(int i, int j)
    {
        if (!TileInteractionProbe.Active) return true;
        TileInteractionProbe.Slopes++;
        TileInteractionProbe.Order += "M";
        if (TileInteractionProbe.Throw) throw new InvalidOperationException("slope probe");
        return TileInteractionProbe.Allow;
    }
}

public sealed class TestTileInteractionGlobal : GlobalTile
{
    public override bool Slope(int i, int j, int type)
    {
        if (!TileInteractionProbe.Active) return true;
        TileInteractionProbe.GlobalSlopes++;
        TileInteractionProbe.Order += "G";
        if (TileInteractionProbe.ThrowGlobal) throw new InvalidOperationException("global slope probe");
        return !TileInteractionProbe.GlobalVeto;
    }
}
