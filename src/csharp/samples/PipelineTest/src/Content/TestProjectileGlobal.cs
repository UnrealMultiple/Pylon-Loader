using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria;
using Terraria.DataStructures;

namespace PipelineTest.Content;

/// <summary>全局弹幕探针：只在 type 命中测试弹幕时计数；VetoKill 用于 PreKill 否决用例。</summary>
public sealed class TestProjectileGlobal : GlobalProjectile
{
    public static int SetDefaultsCount, OnSpawnCount, PreAiCount, AiCount, PostAiCount;
    public static int PreKillCount, OnKillCount, PreDrawCount, PostDrawCount, GetAlphaCount;
    public static int AnyPreDrawCount;
    public static bool VetoKill;

    public static void Reset()
    {
        SetDefaultsCount = OnSpawnCount = PreAiCount = AiCount = PostAiCount = 0;
        PreKillCount = OnKillCount = PreDrawCount = PostDrawCount = GetAlphaCount = 0;
        AnyPreDrawCount = 0;
        VetoKill = false;
    }

    private static bool Matches(Projectile proj) =>
        proj.type == ModContent.ProjectileType<TestProjectile>();

    public override void SetDefaults(Projectile proj)
    {
        if (Matches(proj)) SetDefaultsCount++;
    }

    public override void OnSpawn(Projectile proj, IEntitySource source)
    {
        if (Matches(proj)) OnSpawnCount++;
    }

    public override bool PreAI(Projectile proj)
    {
        if (Matches(proj)) PreAiCount++;
        return true;
    }

    public override void AI(Projectile proj)
    {
        if (Matches(proj)) AiCount++;
    }

    public override void PostAI(Projectile proj)
    {
        if (Matches(proj)) PostAiCount++;
    }

    public override bool PreKill(Projectile proj, int timeLeft)
    {
        if (!Matches(proj)) return true;
        PreKillCount++;
        return !VetoKill;
    }

    public override void OnKill(Projectile proj, int timeLeft)
    {
        if (Matches(proj)) OnKillCount++;
    }

    public override bool PreDraw(Projectile proj, Player player, ref Color lightColor)
    {
        AnyPreDrawCount++;
        if (Matches(proj)) PreDrawCount++;
        return true;
    }

    public override void PostDraw(Projectile proj, Player player, Color lightColor)
    {
        if (Matches(proj)) PostDrawCount++;
    }

    public override Color? GetAlpha(Projectile proj, Color lightColor)
    {
        if (Matches(proj)) GetAlphaCount++;
        return null;
    }
}
