using Pylon.ModLoader;
using Terraria;

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

    public override void SetDefaults(Projectile proj)
    {
        proj.width = 22;
        proj.height = 22;
        proj.damage = 50;
        proj.friendly = true;
        proj.timeLeft = 600;
        proj.penetrate = -1;      // 命中后不消失，方便观察
        proj.light = 0.9f;        // 自发光
        proj.tileCollide = false; // 穿墙，方便观察
    }

    public override void OnSpawn(Projectile proj) => OnSpawnCount++;

    public override bool PreAI(Projectile proj) => true;

    public override void AI(Projectile proj)
    {
        AiCount++;
        proj.rotation += 0.2f;
        proj.scale = 1f + 0.25f * (float)System.Math.Sin(proj.timeLeft * 0.3);
    }

    public override void PostAI(Projectile proj) => PostAiCount++;
}
