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

        // W2：全局链 —— GlobalProjectile 的 OnSpawn/PreAI/AI/PostAI/绘制钩子都收到（差值断言，30 帧）
        PT.Case("PROJ-W2", c =>
        {
            Content.TestProjectileGlobal.Reset();
            var source = player.GetProjectileSource_Misc(0);
            var spawn0 = Content.TestProjectileGlobal.OnSpawnCount;
            var preAi0 = Content.TestProjectileGlobal.PreAiCount;
            var ai0 = Content.TestProjectileGlobal.AiCount;
            var postAi0 = Content.TestProjectileGlobal.PostAiCount;
            var preDraw0 = Content.TestProjectileGlobal.PreDrawCount;
            var postDraw0 = Content.TestProjectileGlobal.PostDrawCount;
            var alpha0 = Content.TestProjectileGlobal.GetAlphaCount;
            var anyPreDraw0 = Content.TestProjectileGlobal.AnyPreDrawCount;
            var proj = Projectile.NewProjectileDirect(source,
                player.Center + new Vector2(0f, -60f), new Vector2(0f, -4f), type, 50, 0f, player.whoAmI);
            c.True(proj is { active: true }, "NewProjectile 生成");
            PT.Later("PROJ-W2", 30, check =>
            {
                PT.Info($"[probe] AnyPreDraw={Content.TestProjectileGlobal.AnyPreDrawCount - anyPreDraw0}" +
                        $" PreDraw={Content.TestProjectileGlobal.PreDrawCount - preDraw0}" +
                        $" PostDraw={Content.TestProjectileGlobal.PostDrawCount - postDraw0}" +
                        $" GetAlpha={Content.TestProjectileGlobal.GetAlphaCount - alpha0}");
                check.True(Content.TestProjectileGlobal.OnSpawnCount - spawn0 > 0, "全局 OnSpawn");
                check.True(Content.TestProjectileGlobal.PreAiCount - preAi0 > 0, "全局 PreAI");
                check.True(Content.TestProjectileGlobal.AiCount - ai0 > 0, "全局 AI");
                check.True(Content.TestProjectileGlobal.PostAiCount - postAi0 > 0, "全局 PostAI");
                check.True(Content.TestProjectileGlobal.PreDrawCount - preDraw0 > 0, "全局 PreDraw");
                check.True(Content.TestProjectileGlobal.PostDrawCount - postDraw0 > 0, "全局 PostDraw");
                check.True(Content.TestProjectileGlobal.GetAlphaCount - alpha0 > 0, "全局 GetAlpha");
            });
        });

        // W3：死亡链 —— Kill() 同步触发 PreKill/OnKill（差值断言，不受后续用例 Reset 影响）
        PT.Case("PROJ-W3", c =>
        {
            var preKill0 = Content.TestProjectileGlobal.PreKillCount;
            var onKill0 = Content.TestProjectileGlobal.OnKillCount;
            var source = player.GetProjectileSource_Misc(0);
            var proj = Projectile.NewProjectileDirect(source,
                player.Center + new Vector2(0f, -60f), new Vector2(0f, -4f), type, 50, 0f, player.whoAmI);
            c.True(proj is { active: true }, "NewProjectile 生成");
            proj.Kill();
            c.True(Content.TestProjectileGlobal.PreKillCount - preKill0 > 0, "全局 PreKill");
            c.True(Content.TestProjectileGlobal.OnKillCount - onKill0 > 0, "全局 OnKill");
        });

        // W4：PreKill 否决 —— 同步断言：不跑 OnKill，弹幕立即静默熄灭
        PT.Case("PROJ-W4", c =>
        {
            Content.TestProjectileGlobal.VetoKill = true;
            var preKill0 = Content.TestProjectileGlobal.PreKillCount;
            var onKill0 = Content.TestProjectileGlobal.OnKillCount;
            var source = player.GetProjectileSource_Misc(0);
            var proj = Projectile.NewProjectileDirect(source,
                player.Center + new Vector2(0f, -60f), new Vector2(0f, -4f), type, 50, 0f, player.whoAmI);
            c.True(proj is { active: true }, "NewProjectile 生成");
            proj.Kill();
            Content.TestProjectileGlobal.VetoKill = false;
            c.True(Content.TestProjectileGlobal.PreKillCount - preKill0 > 0, "全局 PreKill 收到");
            c.Eq(Content.TestProjectileGlobal.OnKillCount - onKill0, 0, "OnKill 不应触发");
            c.True(proj is not { active: true }, "弹幕已熄灭");
        });
    }
}
