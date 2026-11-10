using System;
using Pylon.ModLoader;

namespace PipelineTest.Content;

internal static class TileSoundProbe
{
    internal static bool Active, ModVeto, GlobalVeto, ThrowMod, ThrowGlobal;
    internal static bool LastFail;
    internal static int ModCalls, GlobalCalls, SoundReads, SoundCalls, LastSound, LastType;
    internal static string Order = "";

    internal static void Reset()
    {
        ModVeto = GlobalVeto = ThrowMod = ThrowGlobal = LastFail = false;
        ModCalls = GlobalCalls = SoundReads = SoundCalls = 0;
        LastSound = LastType = -1;
        Order = "";
    }

    internal static int Sound()
    {
        if (Active)
        {
            SoundReads++;
            Order += "S";
        }
        return 0;
    }
}

public sealed class TestTileSound : ModTile
{
    public override string TexturePath => "assets/tiles/TestTileBlock.png";
    public override int HitSound => TileSoundProbe.Sound();

    public override bool KillSound(int i, int j, bool fail)
    {
        if (!TileSoundProbe.Active) return true;
        TileSoundProbe.ModCalls++;
        TileSoundProbe.LastFail = fail;
        TileSoundProbe.Order += "M";
        if (TileSoundProbe.ThrowMod) throw new InvalidOperationException("tile sound isolation probe");
        return !TileSoundProbe.ModVeto;
    }
}

// 仅覆写属性：验证自定义音效不依赖 KillSound 回调位集。
public sealed class TestTileSoundPropertyOnly : ModTile
{
    public override string TexturePath => "assets/tiles/TestTileBlock.png";
    public override int HitSound => TileSoundProbe.Sound();
}

public sealed class TestTileSoundGlobal : GlobalTile
{
    public override bool KillSound(int i, int j, int type, bool fail)
    {
        if (!TileSoundProbe.Active) return true;
        TileSoundProbe.GlobalCalls++;
        TileSoundProbe.LastType = type;
        TileSoundProbe.LastFail = fail;
        TileSoundProbe.Order += "G";
        if (TileSoundProbe.ThrowGlobal) throw new InvalidOperationException("global tile sound isolation probe");
        return !TileSoundProbe.GlobalVeto;
    }
}
