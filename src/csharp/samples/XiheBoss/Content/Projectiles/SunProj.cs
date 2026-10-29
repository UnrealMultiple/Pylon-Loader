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
            for (var i = 0; i < 25; i++)
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
                // 源只靠太阳 shader 自身的过曝（DrawSun），没有全屏白 Flash；这里对齐 MEAC，不再加白屏。

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



