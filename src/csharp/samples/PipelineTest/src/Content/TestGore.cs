using Pylon.ModLoader;
using Terraria;

namespace PipelineTest.Content;

/// <summary>测试血腥粒子。贴图 assets/gores/TestGore.png。</summary>
public sealed class TestGore : ModGore
{
    public static int OnSpawnCount;

    public override void OnSpawn(Gore gore)
    {
        OnSpawnCount++;
        gore.timeLeft = 600;
    }
}
