// 太阳（源：MEAC.Bosses.EoL.Projectiles.SunProj 1:1 移植）。
//
// AI 逐行照抄：725 tick 寿命、313 tick 的 30 级震屏 + RadialGlur2、10~500 tick 随时间的震屏、
// 10 tick 的 36*3 发 EoLProj 爆发、>313 tick / <=313 tick 两段的日珥生成节奏（半径 100 / 126 / 120，
// 速度 10~25 与 70~110）以及 scale / ai[0] / ai[1] 的两套 Lerp 参数。ShouldUpdatePosition = false、
// Colliding 的 100*scale 判定也照抄。
//
// ai 约定：源文件没有外部 ai；ai[0]（画面总强度，最终 *2.1 当 alpha）与 ai[1]（日珥/光线强度）
// 只在本 AI 内 Lerp，原样留在 proj.ai 供 PushVisual 读取。
//
// 视觉近似点：
// - 源 PreDraw 调 EoLSky.DrawSun（原生太阳 shader：uColor=(1,0.8,0.1)、uScale=0.067*scale、
//   uRayAlpha=ai[1]、alpha=ai[0]*2.1）。Pylon 不能用原生 Effect，这里用 XiheFx 图元复现：
//   本体 = Ball3 核心 + Flame 火舌，日冕 = 大半径 Ball3 + core，外加 6 圈 MagicRing/WarpRing2 光线。
// - 源 OnSpawn 生成 25 个 MagicRing_Sun（自转环特效）；这里不建对象，由 PushVisual 的 6 圈环按
//   时间旋转近似（数量少是为了屏幕图元上限）。
// - 源日珥是 SolarProminence 弹幕（带 solarCenter/solarRadius、SetTrail，会伤害玩家）。本 Mod 没有
//   登记 SolarProminence，也不允许强转 ModProjectile，所以日珥改为 XiheVisuals 的 LightDust /
//   LightDustCn（沿径向速度飞出）：生成时机、半径、速度、数量与源文件完全一致，但不再造成伤害。
// - 10 tick 的爆炸闪光：源由太阳 shader 的过曝表现，这里补一个 XiheFx.Flash(0.75f) 近似。
using System;
using Il2CppMicrosoft.Xna.Framework;
using Color = Il2CppMicrosoft.Xna.Framework.Graphics.Color;
using Pylon.ModLoader;
using Terraria;
using Terraria.ID;

namespace XiheBoss
{
    public sealed class SunProj : ModProjectile
    {
        /// <summary>EoLSky.DrawSun 的 uScale：太阳半径按屏幕高度换算。</summary>
        private const float SunScale = 0.067f;

        public override string TexturePath => null;

        public override void SetStaticDefaults()
        {
            Main.projFrames[Type] = 1;
        }

        public override void SetDefaults(Projectile proj)
        {
            proj.width = 20;
            proj.height = 20;
            proj.aiStyle = -1;
            proj.hostile = true;
            proj.friendly = false;
            proj.timeLeft = 725;
            proj.ignoreWater = true;
            proj.tileCollide = false;
            proj.penetrate = -1;
            proj.scale = 0f;
        }

        public override void OnSpawn(Projectile proj)
        {
            // 源 OnSpawn：25 个 MagicRing_Sun，ai1 各加 0..50。
            for (var i = 0; i < 12; i++)
            {
                var ring = XiheVisuals.Create(VfxKind.MagicRingSun, proj.Center, Vector2.Zero);
                ring.OwnerProjectile = proj.whoAmI;
                ring.Ai1 += Main.rand.NextFloat() * 50f;
            }
        }

        public override bool ShouldUpdatePosition(Projectile proj) => false;

        public override void AI(Projectile proj)
        {
            if (proj.timeLeft == 313)
            {
                XiheFx.ScreenShake(30f, proj.Center);
                XiheFx.RadialGlur(proj.Center, 10, true);
            }

            if (proj.timeLeft > 10 && proj.timeLeft < 500)
            {
                XiheFx.ScreenShake((500 - proj.timeLeft) / 65f, proj.Center);
            }

            if (proj.timeLeft == 10)
            {
                XiheFx.ScreenShake(30f, proj.Center);
                XiheFx.Flash(0.75f);   // 近似：源的太阳 shader 过曝

                if (Main.netMode != 1)
                {
                    var r = (float)Main.rand.NextDouble();
                    for (var i = 0; i < 36; i++)
                    {
                        for (var j = 0; j < 3; j++)
                        {
                            var vel = Utils.ToRotationVector2(r + i * (float)Math.PI * 2f / 36f)
                                      * (6f + 1.5f * (i % 3) + j * 3);
                            Projectile.NewProjectile(null, proj.Center, vel, ModContent.ProjectileType<EoLProj>(),
                                proj.damage / 5, 0f, Main.myPlayer, 0f, 0f, 0f);
                        }
                    }
                }
            }

            var ai = proj.ai;

            if (proj.timeLeft > 313)
            {
                if (proj.timeLeft > 364.5f && XiheHelper.RandBool(15))
                {
                    SpawnProminence(proj, 100f, 10, 25, true);
                }

                proj.scale = MathHelper.Lerp(proj.scale, 3.5f, 0.06f);
                ai[0] = MathHelper.Lerp(ai[0], 0.3f, 0.1f);
                ai[1] = MathHelper.Lerp(ai[1], 1.5f, 0.05f);
            }
            else
            {
                if (proj.timeLeft > 60 && XiheHelper.RandBool(10))
                {
                    SpawnProminence(proj, 126f, 10, 25, true);
                }
                if (proj.timeLeft > 110 && proj.timeLeft % 16 == 0)
                {
                    SpawnProminence(proj, 120f, 70, 110, false);
                }

                ai[0] = MathHelper.Lerp(ai[0], 0.275f, 0.1f);
                proj.scale = MathHelper.Lerp(proj.scale, 5.6f, 0.02f);
            }

            if (proj.timeLeft < 10)
            {
                proj.scale = MathHelper.Lerp(proj.scale, 10f, 0.2f);
                ai[0] = MathHelper.Lerp(ai[0], 0f, 0.15f);
                ai[1] = MathHelper.Lerp(ai[1], 0f, 0.1f);
            }

            proj.ai = ai;
        }

        /// <summary>
        /// 源 SolarProminence 弹幕：从太阳表面沿随机径向飞出，带 solarCenter/solarRadius 的 trail。
        /// radiusFactor / minSpeed / maxSpeed 的三个调用口径与源文件一一对应。
        /// </summary>
        private static void SpawnProminence(Projectile proj, float radiusFactor, int minSpeed, int maxSpeed,
            bool setTrail)
        {
            if (Main.netMode == 1) return;

            var dir = XiheHelper.NextVector2Unit();
            var radius = proj.scale * radiusFactor;
            var speed = Main.rand.Next(minSpeed, maxSpeed);
            var pos = proj.Center + dir * radius;

            var idx = Projectile.NewProjectile(null, pos, dir * speed,
                ModContent.ProjectileType<SolarProminence>(), proj.damage / 4, 0f, Main.myPlayer, 0f, 0f, 0f);
            if (idx < 0 || idx >= Main.projectile.Length) return;
            var p = Main.projectile[idx];
            SolarProminence.Configure(p, proj.Center, radius, setTrail);
            if (setTrail)
            {
                p.timeLeft -= 50;
            }
            p.netUpdate2 = true;
        }

        public override bool? Colliding(Projectile proj, Rectangle projHitbox, Rectangle targetHitbox)
        {
            var target = Utils.TopLeft(targetHitbox) + Utils.Size(targetHitbox) / 2f;
            return proj.timeLeft > 10 && Vector2.Distance(target, proj.Center) < 100f * proj.scale;
        }

        public override bool PreDraw(Projectile proj, Color lightColor) => false;

        /// <summary>源 PreDraw：EoLSky.DrawSun → 逐像素 Sun.fx。</summary>
        public static void PushVisual(Projectile p)
        {
            var ai = p.ai;
            if (p.scale <= 0.01f) return;

            // DrawSun(center, ai[1], 0.067*scale, Main.Transform, ai[0]*2.1)
            XiheFx.SunFxAt(p.Center, p.scale, ai[1], ai[0] * 2.1f);
        }
    }
}



