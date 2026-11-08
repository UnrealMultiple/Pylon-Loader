using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria;

namespace PipelineTest.Content;

/// <summary>测试尘土。贴图 assets/dusts/TestDust.png。</summary>
public sealed class TestDust : ModDust
{
    public static int OnSpawnCount;
    public static int UpdateCount;

    public override void OnSpawn(Dust dust)
    {
        OnSpawnCount++;
        dust.noGravity = true;
    }

    public override bool Update(Dust dust)
    {
        UpdateCount++;
        return true; // 走默认更新
    }

    public override Il2CppMicrosoft.Xna.Framework.Graphics.Color GetAlpha(Il2CppMicrosoft.Xna.Framework.Graphics.Color lightColor) =>
        new(255, 0, 200, 255);
}
