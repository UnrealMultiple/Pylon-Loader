using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader;
using Terraria;
using Terraria.ID;

namespace PipelineTest.Tests;

/// <summary>
/// Projectile 作者面 API：TML 扩展（GetGlobalProjectile/TryGetOwner/CloneDefaults/NewProjectileDirect/GetDefaultDrawData）
/// 与 ModProjectile 声明面（LocalizationCategory/CooldownSlot/GlowTexture）。
/// </summary>
internal static class ProjectileApiTests
{
    public static void Register()
    {
        PT.Group("projectile-api", Phase.Ready, RunReady);
        PT.Group("projectile-api-w", Phase.World, RunWorld);
    }

    private static void RunReady()
    {
        var mod = ModContent.GetInstance<Content.TestProjectile>()!;

        PT.Case("PROJAPI-1", c =>
        {
            c.Eq(mod.LocalizationCategory, "Projectiles", "LocalizationCategory");
            c.Eq(mod.CooldownSlot, -1, "CooldownSlot 默认 -1");
            c.Eq(mod.AIType, 0, "AIType 默认 0");
            c.Eq(mod.GlowTexture, "assets/projectiles/TestProjectile_Glow.png", "GlowTexture 约定");
        });

        PT.Case("PROJAPI-2", c =>
        {
            var type = ModContent.ProjectileType<Content.TestProjectile>();
            var proj = new Projectile();
            proj.SetDefaults(type);
            c.True(proj.ModProjectile is Content.TestProjectile, "Projectile.ModProjectile 指回实例");
            c.True(proj.TryGetGlobalProjectile<Content.TestProjectileGlobal>(out _), "TryGetGlobalProjectile（模板态）");
            var frames = Main.projFrames;
            c.True(frames is not null && frames[type] == 1, $"AutoStaticDefaults 写 projFrames=1（实际 {frames?[type]}）");
        });
    }

    private static void RunWorld()
    {
        var player = Main.LocalPlayer;
        if (player is null || !player.active)
        {
            PT.SkipCase("PROJAPI-W1", "玩家还没就绪");
            PT.SkipCase("PROJAPI-W2", "玩家还没就绪");
            return;
        }

        var type = ModContent.ProjectileType<Content.TestProjectile>();
        var source = player.GetProjectileSource_Misc(0);

        // W1：TML 面 —— NewProjectileDirect / EntityGlobals / GetGlobalProjectile / TryGetOwner
        PT.Case("PROJAPI-W1", c =>
        {
            var proj = Projectile.NewProjectileDirect(source,
                player.Center + new Vector2(0f, -60f), new Vector2(0f, -4f), type, 50, 0f, player.whoAmI);
            c.True(proj is { active: true }, "NewProjectileDirect 返回激活弹幕");
            c.Eq(proj.type, type, "弹幕类型");
            c.True(proj.EntityGlobals.Count > 0, "EntityGlobals 非空");
            c.True(proj.GetGlobalProjectile<Content.TestProjectileGlobal>() is Content.TestProjectileGlobal,
                "GetGlobalProjectile<T>");
            c.True(proj.TryGetOwner(out var owner) && owner!.whoAmI == Main.myPlayer, "TryGetOwner 指向本地玩家");
        });

        // W2：CloneDefaults —— 抄原版箭的默认值，但类型号与 ModProjectile 绑定不变
        PT.Case("PROJAPI-W2", c =>
        {
            var reference = new Projectile();
            reference.SetDefaults(ProjectileID.WoodenArrowFriendly);

            var proj = Projectile.NewProjectileDirect(source,
                player.Center + new Vector2(0f, -60f), new Vector2(0f, -4f), type, 50, 0f, player.whoAmI);
            proj.CloneDefaults(ProjectileID.WoodenArrowFriendly);

            c.Eq(proj.type, type, "CloneDefaults 后类型号不变");
            c.Eq(proj.damage, reference.damage, "伤害抄自箭");
            c.Eq(proj.width, reference.width, "宽度抄自箭");
            c.True(proj.ModProjectile is Content.TestProjectile, "ModProjectile 绑定保持");
        });
    }
}
