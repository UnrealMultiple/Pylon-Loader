using System;
using Pylon.ModLoader;
using Terraria;
using Terraria.DataStructures;

namespace PipelineTest.Content;

/// <summary>测试血腥粒子。贴图 assets/gores/TestGore.png；OnSpawn 记下生成源有没有到。</summary>
public sealed class TestGore : ModGore
{
    public static int OnSpawnCount;
    public static bool SawSource;
    public static bool SawEmpty;

    public override void OnSpawn(Gore gore, IEntitySource? source)
    {
        OnSpawnCount++;
        if (source is not null && source.Pointer != IntPtr.Zero) SawSource = true;
        else SawEmpty = true;
        gore.timeLeft = 600;
    }
}
