using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader;
using Terraria;
using Terraria.ID;

namespace PipelineTest.Tests;

/// <summary>
/// 管线 28：Projectile —— 注册面 + 功能面。
/// Ready（主菜单）：发号 / SetDefaults 落位 / 贴图进表（不生成实体，菜单期没有合法 owner）。
/// World（进世界）：NewProjectile 真生成 → OnSpawn 钩子；再扇出一排弹幕肉眼验证 AI/绘制。
/// </summary>
internal static class ProjectileTests
{
    public static void Register()
    {
        PT.Group("projectile", Phase.Ready, RunReady);
        PT.Group("projectile-w", Phase.World, RunWorld);
    }

    private static void RunReady()
    {
        var type = ModContent.ProjectileType<Content.TestProjectile>();

        PT.Case("PROJ-R1", c =>
        {
            c.True(type >= ProjectileID.Count, $"type={type}（原版 {ProjectileID.Count}）");
            c.NotNull(ModContent.GetInstance<Content.TestProjectile>(), "GetInstance");
        });

        PT.Case("PROJ-S1", c =>
        {
            var proj = new Projectile();
            proj.SetDefaults(type);
            c.Eq(proj.width, 22, "width");
            c.Eq(proj.height, 22, "height");
            c.Eq(proj.damage, 50, "damage");
            c.True(proj.friendly, "friendly");
            c.Eq(proj.timeLeft, 600, "timeLeft");
            c.True(proj.active, "active");
        });

        // T1：贴图进表
        PT.Case("PROJ-T1", c =>
        {
            var table = Terraria.GameContent.TextureAssets.Projectile;
            var len = table?.Length ?? 0;
            c.True(len > type, $"弹幕贴图表长={len}（应 > {type}）");
            if (len > type) c.NotNull(table![type], $"[{type}] 槽位");
        });
    }

    /// <summary>进世界后：真生成一发验证 OnSpawn，再扇出一排弹幕让玩家肉眼看到。</summary>
    private static void RunWorld()
    {
        var type = ModContent.ProjectileType<Content.TestProjectile>();
        var player = Main.player[Main.myPlayer];
        if (player is null || !player.active)
        {
            PT.SkipCase("PROJ-H1", "玩家还没就绪");
            PT.SkipCase("PROJ-W1", "玩家还没就绪");
            return;
        }

        // H1 功能：NewProjectile 真实生成 → OnSpawn 钩子被调（On.NewProjectile_1 挂钩验证）
        PT.Case("PROJ-H1", c =>
        {
            var before = Content.TestProjectile.OnSpawnCount;
            var source = player.GetProjectileSource_Misc(0);
            var idx = Projectile.NewProjectile(source,
                player.Center.X, player.Center.Y - 60f, 0f, -4f, type, 50, 0f, player.whoAmI);
            c.True(idx >= 0, $"NewProjectile 返回 {idx}");
            c.True(Content.TestProjectile.OnSpawnCount > before,
                $"OnSpawn 未触发（{before} -> {Content.TestProjectile.OnSpawnCount}）");
        });

        // W1：一排 5 发，扇形向上
        PT.Case("PROJ-W1", c =>
        {
            var origin = player.Center + new Vector2(0f, -60f);
            var beforeSpawn = Content.TestProjectile.OnSpawnCount;
            var source = player.GetProjectileSource_Misc(0);
            for (var i = 0; i < 5; i++)
            {
                var angle = -MathHelper.PiOver2 + (i - 2) * 0.35f;
                var velocity = new Vector2((float)System.Math.Cos(angle), (float)System.Math.Sin(angle)) * 5f;
                var idx = Projectile.NewProjectile(source,
                    origin.X, origin.Y, velocity.X, velocity.Y, type, 50, 0f, player.whoAmI);
                c.True(idx >= 0, $"第 {i + 1} 发 NewProjectile 返回 {idx}");
            }
            c.Eq(Content.TestProjectile.OnSpawnCount - beforeSpawn, 5, "OnSpawn 次数");
        });
    }
}
