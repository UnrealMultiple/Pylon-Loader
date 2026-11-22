using System;
using System.Collections.Generic;
using Il2CppMicrosoft.Xna.Framework;
using Color = Il2CppMicrosoft.Xna.Framework.Graphics.Color;
using Pylon.ModLoader;
using Pylon.ModLoader.Content;
using Terraria;
using Terraria.GameContent;

namespace XiheBoss.EoC
{
    public sealed class EocProj : ModProjectile
    {
        private const int TrailCache = 20;

        private static Vector2[][] _oldPos;

        public override string TexturePath => null;

        public override void SetStaticDefaults()
        {
            Main.projFrames[Type] = 1;
        }

        public override void SetDefaults()
        {
            var proj = Projectile;
            proj.width = 8;
            proj.height = 15;
            proj.aiStyle = -1;
            proj.hostile = true;
            proj.friendly = false;
            proj.timeLeft = 300;
            proj.ignoreWater = true;
            proj.tileCollide = true;
            proj.penetrate = -1;
            proj.scale = 1.1f;
            proj.alpha = 100;
        }

        public override void OnSpawn(Terraria.DataStructures.IEntitySource source) => Alloc(Projectile);

        public override void OnKill(int timeLeft)
        {
            Clear(Projectile);
            var proj = Projectile;
            for (int i = 0; i < 20; i++)
            {
                Dust obj = Dust.NewDustDirect(proj.Center, 15, 15, 5, 0f, 0f, 0, default(Color), 2f);
                obj.noGravity = true;
                obj.velocity = Utils.NextVector2Unit(Main.rand, 0f, (float)Math.PI * 2f) * Main.rand.Next(10);
            }
        }

        private static Vector2[] TrailOf(Projectile proj)
        {
            var count = Main.projectile.Length;
            if (_oldPos == null || _oldPos.Length != count) return null;
            var i = proj.whoAmI;
            return i >= 0 && i < count ? _oldPos[i] : null;
        }

        private static void Alloc(Projectile proj)
        {
            var count = Main.projectile.Length;
            if (_oldPos == null || _oldPos.Length != count) _oldPos = new Vector2[count][];
            var i = proj.whoAmI;
            if (i < 0 || i >= count) return;
            _oldPos[i] = new Vector2[TrailCache];
        }

        private static void Clear(Projectile proj)
        {
            var count = Main.projectile.Length;
            var i = proj.whoAmI;
            if (i < 0 || i >= count) return;
            if (_oldPos != null && _oldPos.Length == count) _oldPos[i] = null;
        }

        public override bool OnTileCollide(Vector2 oldVelocity)
        {
            Projectile.ai[1] = 1f;
            return false;
        }

        public override void AI()
        {
            var proj = Projectile;
            var lai = XiheHelper.ReadLocalAi(proj);
            var trail = TrailOf(proj);

            if (proj.ai[1] == 1f)
            {
                proj.velocity = Vector2.Zero;
                if ((lai[1] += 1f) > 60f)
                {
                    proj.Kill();
                }
            }
            else
            {
                proj.rotation = Utils.ToRotation(proj.velocity) + (float)Math.PI / 2f;
                var vel = proj.velocity;
                if (proj.ai[0] == 0f)
                {
                    if (vel.Y < 10f)
                    {
                        vel.Y += 0.15f;
                    }
                    if (proj.extraUpdates >= 1)
                    {
                        vel.Y -= 0.05f;
                    }
                }
                else if (vel.Y > -12f)
                {
                    vel.Y -= 0.15f;
                }
                proj.velocity = vel;
                Dust obj = Dust.NewDustDirect(proj.position, 15, 15, 5, 0f, 0f, 0, default(Color), 1.3f);
                obj.noGravity = true;
                obj.velocity = -proj.velocity * 0.5f;
            }
            XiheHelper.WriteLocalAi(proj, lai[0], lai[1], lai[2], 0f);

            if (trail == null) return;
            for (int i = TrailCache - 1; i > 0; i--)
            {
                trail[i] = trail[i - 1];
            }
            trail[0] = proj.Center;
        }

        public override bool PreDraw(Player player, ref Color lightColor) => false;

        public static void PushVisual(Projectile p)
        {
            var proj = p;
            var trail = TrailOf(proj);
            var center = proj.Center;

            if (trail != null)
            {
                var counts = 0f;
                for (int i = 0; i < trail.Length; i++)
                {
                    if (trail[i] != Vector2.Zero)
                    {
                        counts += 1f;
                    }
                }
                var points = new List<TrailStripPoint>(TrailCache);
                points.Add(new TrailStripPoint
                {
                    Left = center,
                    Right = center,
                    UvX = 0f,
                    W = 2f,
                });
                for (int j = 0; j < trail.Length - 1 && !(trail[j + 1] == Vector2.Zero); j++)
                {
                    var normalDir = trail[j] - trail[j + 1];
                    normalDir = XiheHelper.Normalize2(new Vector2(-normalDir.Y, normalDir.X));
                    var factor = j / (counts - 1f);
                    var w = MathHelper.SmoothStep(1f, 0f, factor);
                    var width = 30f;
                    points.Add(new TrailStripPoint
                    {
                        Left = trail[j] + normalDir * width,
                        Right = trail[j] - normalDir * width,
                        UvX = (float)Math.Sqrt(factor),
                        W = w,
                    });
                }
                XiheFx.TrailStrip(points, Tex.Extra196, Tex.ColorBlood, Tex.Transparent,
                    proj.timeLeft * 0.04f, additive: false);
            }

            XiheFx.Sprite(center, Tex.ProjTex, new Color(255, 10, 10), proj.rotation,
                Vector2.One * proj.scale,
                new Vector2(XiheTextures.WidthOf(Tex.ProjTex), XiheTextures.HeightOf(Tex.ProjTex)) / 2f,
                additive: false);
        }
    }
}
