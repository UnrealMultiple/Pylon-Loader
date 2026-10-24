using Pylon.ModLoader;
using Terraria;
using Pylon.ModLoader.Content;

namespace PipelineTest.Content;

/// <summary>
/// 测试弹幕（Projectile 管线肉眼验证）：
/// - 挥舞"测试物品"发射这颗弹幕
/// - AI：自转 + 缩放脉动 + 自发光（无重力，缓慢飞行）
/// - 贴图 assets/projectiles/TestProjectile.png（一颗黄星）
/// </summary>
public sealed class TestProjectile : ModProjectile
{
    public static int OnSpawnCount;
    public static int AiCount;
    public static int PostAiCount;

    public override void SetDefaults()
    {
        Projectile.width = 22;
        Projectile.height = 22;
        Projectile.damage = 50;
        Projectile.friendly = true;
        Projectile.timeLeft = 600;
        Projectile.penetrate = -1;      // 命中后不消失，方便观察
        Projectile.light = 0.9f;        // 自发光
        Projectile.tileCollide = false; // 穿墙，方便观察
    }

    public override void OnSpawn(Terraria.DataStructures.IEntitySource? source) => OnSpawnCount++;

    public override bool PreAI() => true;

    public override void AI()
    {
        AiCount++;
        Projectile.rotation += 0.2f;
        Projectile.scale = 1f + 0.25f * (float)System.Math.Sin(Projectile.timeLeft * 0.3);
    }

    public override void PostAI() => PostAiCount++;
}
