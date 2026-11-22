using System;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Color = Il2CppMicrosoft.Xna.Framework.Graphics.Color;
using Pylon.ModLoader;
using Terraria;
using Terraria.ID;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;

namespace XiheBoss
{
    public sealed class EoLLaser1 : ModProjectile
    {
        public override string TexturePath => null;

        private int _maxTime;

        private bool _setTime;

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
            proj.timeLeft = 25;
            proj.ignoreWater = true;
            proj.tileCollide = false;
            proj.penetrate = -1;
            proj.scale = 0.02f;
            proj.alpha = 255;
            proj.extraUpdates = 1;
        }

        public override void OnSpawn(Terraria.DataStructures.IEntitySource source)
        {
            var proj = Projectile;
            _maxTime = proj.ai[2] > 0f ? (int)proj.ai[2] : proj.timeLeft;

            var mode = proj.ai[1];
            if (mode >= -1f && mode <= 2f)
                proj.rotation = (float)Math.Atan2(proj.velocity.Y, proj.velocity.X);

            if (mode == 4f)
            {
                proj.timeLeft = 145;
                _maxTime = 145;
                proj.rotation = (float)Math.Atan2(proj.velocity.Y, proj.velocity.X);
                proj.extraUpdates--;
            }
        }

        private int Timer(Projectile proj) => _maxTime - proj.timeLeft;

        public override void AI()
        {
            var proj = Projectile;

            if (proj.ai[2] > 0f) _maxTime = (int)proj.ai[2];
            else if (_maxTime <= 0) _maxTime = proj.timeLeft;

            proj.localAI[1] = 5000f;

            var mode = proj.ai[1];

            if (mode == 0f)
            {
                if (proj.timeLeft == 24)
                {
                    proj.rotation = (float)Math.Atan2(proj.velocity.Y, proj.velocity.X);
                    LaserDust(proj, Color.Lerp(XiheHelper.HsvToRgb(proj.ai[0] % 360f), Color.White, 0.5f));
                }
                if (proj.timeLeft > 16) proj.scale += 0.06f;
                else if (proj.timeLeft < 12) proj.scale -= 0.05f;
            }

            else if (mode == 1f || mode == -1f)
            {
                if (!_setTime)
                {
                    proj.extraUpdates = 0;
                    proj.timeLeft = _maxTime;
                    _setTime = true;
                }

                var ownerIndex = (int)proj.ai[0];
                if (ownerIndex < 0 || ownerIndex >= Main.npc.Length || !Main.npc[ownerIndex].active)
                {
                    proj.Kill();
                    return;
                }

                proj.Center = Main.npc[ownerIndex].Center;
                proj.rotation += proj.ai[1] * 0.00525f;

                var t = Timer(proj);
                if (t >= 20 && t < 30) proj.scale -= 0.002f;
                if (t >= 45 && t < 50) proj.scale += 0.07f;
                if (t > 50) proj.scale += 0.06f * (float)Math.Sin(proj.timeLeft * 0.8f);
                if (t > 150) proj.rotation += (t - 150) * 0.00022f * proj.ai[1];
                if (proj.timeLeft < 10) proj.scale -= 0.03f;
            }

            else if (mode == 2f)
            {
                if (!_setTime)
                {
                    proj.extraUpdates = 0;
                    proj.timeLeft = _maxTime;
                    _setTime = true;
                }

                var ownerIndex = (int)proj.ai[0];
                if (ownerIndex < 0 || ownerIndex >= Main.npc.Length || !Main.npc[ownerIndex].active)
                {
                    proj.Kill();
                    return;
                }

                proj.Center = Main.npc[ownerIndex].Center;
                proj.rotation += 0.009f * (float)Math.Sin(proj.timeLeft * 0.03f);

                var t = Timer(proj);
                if (t >= 54 && t < 60) proj.scale += 0.07f;
                if (proj.timeLeft < 10) proj.scale -= 0.04f;
            }

            else if (mode == 3f)
            {
                if (!_setTime)
                {
                    proj.extraUpdates = 0;
                    proj.timeLeft = _maxTime;
                    _setTime = true;
                    proj.rotation = (float)Math.Atan2(proj.velocity.Y, proj.velocity.X);
                    LaserDust(proj, XiheHelper.HsvToRgb(proj.ai[0] % 360f));
                }

                var t = Timer(proj);
                if (t < 8) proj.scale += 0.06f;
                else if (proj.timeLeft < 12) proj.scale -= 0.05f;
            }

            else if (mode == 4f)
            {
                var t = Timer(proj);
                var scale = proj.scale;

                if (t < 20) XiheHelper.LerpValue(ref scale, 0.03f, 0.1f);
                if (t > 50 && t < 100)
                {
                    XiheHelper.LerpValue(ref scale, 0f, 0.05f);
                    if (scale < 0.015f) scale = 0f;
                }
                if (t > 100 && t < 120) XiheHelper.LerpValue(ref scale, 0.65f, 0.2f);
                if (t > 135) XiheHelper.LerpValue(ref scale, 0f, 0.3f);

                proj.scale = scale;
            }
        }

        private static void LaserDust(Projectile proj, Color color)
        {
            var dir = XiheHelper.Normalize2(proj.velocity);
            for (var i = 0; i < 30; i++)
            {
                var pos = proj.Center
                          + dir * i * (80 + Main.rand.Next(100))
                          + XiheHelper.NextVector2Unit() * 5f;
                var vfx = XiheVisuals.Create(VfxKind.LightDust, pos,
                    XiheHelper.NextVector2Unit() * 1f + proj.velocity * 10f);
                vfx.DrawColor = color;
            }
        }

        public override bool ShouldUpdatePosition() => false;

        public override bool CanDamage() => Projectile.scale > 0.2f;

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            var proj = Projectile;

            var unit = XiheHelper.Vxr(proj.rotation);
            float point = 0f;
            return Collision.CheckAABBvLineCollision(
                Utils.TopLeft(targetHitbox), Utils.Size(targetHitbox),
                proj.Center, proj.Center + unit * proj.localAI[1],
                18f * proj.scale, ref point);
        }

        public override bool PreDraw(Player player, ref Color lightColor) => false;

        public static void PushVisual(Projectile p)
        {
            if (p.scale <= 0.001f) return;

            var color = XiheHelper.HsvToRgb(45f, 1f, 0.5f);
            var length = p.localAI[1];
            if (length <= 1f) return;
            var dir = XiheHelper.Vxr(p.rotation);
            var s = p.scale;

            XiheFx.SpriteRect(p.Center, Tex.Tex4, new Rectangle(0, 0, 256 * 5, 256),
                color, p.rotation, new Vector2(length / 256f / 5f, s), new Vector2(0f, 128f),
                additive: true, colorize: true, wrapU: true, srcRepeat: 5f,
                wrapShift: p.timeLeft * 8f / 256f);

            var end = p.Center + dir * length;

            var originLaser = new Vector2(72f, 72f) / 2f;
            XiheFx.Sprite(p.Center, Tex.Laser0, color, 0f, Vector2.One * (s * 2f), originLaser,
                additive: true, colorize: true);
            XiheFx.Sprite(end, Tex.Laser0, color, 0f, Vector2.One * (s * 2f), originLaser,
                additive: true, colorize: true);

            var originStar = new Vector2(72f, 72f) / 2f;
            XiheFx.Sprite(p.Center, Tex.Extra89, color, p.timeLeft * 0.08f,
                new Vector2(0.5f, 3f) * s * 3f, originStar, additive: true, colorize: true);
            XiheFx.Sprite(p.Center, Tex.Extra89, color, -p.timeLeft * 0.1f,
                new Vector2(0.5f, 2f) * s * 3f, originStar, additive: true, colorize: true);
            XiheFx.Sprite(p.Center, Tex.Extra89, color, 0f,
                new Vector2(0.6f, 4f) * s * 2f, originStar, additive: true, colorize: true);
            XiheFx.Sprite(p.Center, Tex.Extra89, color, 1.57f,
                new Vector2(0.6f, 6f) * s * 2f, originStar, additive: true, colorize: true);
        }
    }
}
