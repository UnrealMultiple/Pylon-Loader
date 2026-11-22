using System;
using Il2CppMicrosoft.Xna.Framework;
using Color = Il2CppMicrosoft.Xna.Framework.Graphics.Color;
using Pylon.ModLoader;
using Terraria;
using Terraria.ID;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;

namespace XiheBoss
{
    public sealed class SunProj : ModProjectile
    {

        private const float SunScale = 0.067f;

        public override string TexturePath => null;

        public override void SetStaticDefaults()
        {
            Main.projFrames[Type] = 1;
        }

        public override void SetDefaults()
        {
            var proj = Projectile;
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

        public override void OnSpawn(Terraria.DataStructures.IEntitySource source)
        {
            var proj = Projectile;

            for (var i = 0; i < 25; i++)
            {
                var ring = XiheVisuals.Create(VfxKind.MagicRingSun, proj.Center, Vector2.Zero);
                ring.OwnerProjectile = proj.whoAmI;
                ring.Ai1 += Main.rand.NextFloat() * 50f;
            }
        }

        public override bool ShouldUpdatePosition() => false;

        public override void AI()
        {
            var proj = Projectile;
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

            if (proj.timeLeft > 313)
            {
                if (proj.timeLeft > 364.5f && XiheHelper.RandBool(15))
                {
                    SpawnProminence(proj, 100f, 10, 25, true);
                }

                proj.scale = MathHelper.Lerp(proj.scale, 3.5f, 0.06f);
                proj.ai[0] = MathHelper.Lerp(proj.ai[0], 0.3f, 0.1f);
                proj.ai[1] = MathHelper.Lerp(proj.ai[1], 1.5f, 0.05f);
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

                proj.ai[0] = MathHelper.Lerp(proj.ai[0], 0.275f, 0.1f);
                proj.scale = MathHelper.Lerp(proj.scale, 5.6f, 0.02f);
            }

            if (proj.timeLeft < 10)
            {
                proj.scale = MathHelper.Lerp(proj.scale, 10f, 0.2f);
                proj.ai[0] = MathHelper.Lerp(proj.ai[0], 0f, 0.15f);
                proj.ai[1] = MathHelper.Lerp(proj.ai[1], 0f, 0.1f);
            }

        }

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

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            var proj = Projectile;
            var target = Utils.TopLeft(targetHitbox) + Utils.Size(targetHitbox) / 2f;
            return proj.timeLeft > 10 && Vector2.Distance(target, proj.Center) < 100f * proj.scale;
        }

        public override bool PreDraw(Player player, ref Color lightColor) => false;

        public static void PushVisual(Projectile p)
        {
            
            if (p.scale <= 0.01f) return;

            XiheFx.SunFxAt(p.Center, p.scale, p.ai[1], p.ai[0] * 2.1f);
        }
    }
}
