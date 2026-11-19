using System;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;

namespace XiheBoss
{
    public sealed class EoLLight : ModProjectile
    {
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
            proj.timeLeft = 70;
            proj.ignoreWater = true;
            proj.tileCollide = false;
            proj.penetrate = -1;
            proj.scale = 0.1f;
            proj.alpha = 255;
        }

        public override bool ShouldUpdatePosition() => false;

        public override void AI()
        {
            var proj = Projectile;
            var ai = proj.ai;

            var idx = (int)ai[0];
            if (idx < 0 || idx >= Main.npc.Length)
            {
                proj.Kill();
                return;
            }

            var npc = Main.npc[idx];
            if (npc.active) proj.Center = npc.Center;
            else
            {
                proj.Kill();
                return;
            }

            var local = proj.localAI;
            var mode = ai[1];

            if (mode == 1f || mode == -1f)
            {
                local[1] = proj.timeLeft > 10
                    ? MathHelper.Lerp(local[1], 320f, 0.1f)
                    : MathHelper.Lerp(local[1], 0f, 0.2f);
                proj.rotation += 0.1f * ai[1];
            }
            else if (mode == 2f || mode == -2f)
            {
                local[1] = proj.timeLeft > 10
                    ? MathHelper.Lerp(local[1], 200f, 0.1f)
                    : MathHelper.Lerp(local[1], 0f, 0.2f);
                proj.scale = MathHelper.Lerp(proj.scale, 1f, 0.05f);
                proj.rotation += ai[1] * 0.05f;
            }
            else if (mode == 3f || mode == -3f)
            {
                local[1] = proj.timeLeft > 10
                    ? MathHelper.Lerp(local[1], 650f, 0.1f)
                    : MathHelper.Lerp(local[1], 0f, 0.2f);
                proj.scale = MathHelper.Lerp(proj.scale, 1f, 0.05f);
                proj.rotation += ai[1] * -0.011f;
            }

            proj.localAI = local;
        }

        public override bool CanDamage() => Projectile.scale > 0.2f;

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            var proj = Projectile;
            var unit = XiheHelper.Vxr(proj.rotation);
            var point = 0f;
            return Collision.CheckAABBvLineCollision(Utils.TopLeft(targetHitbox), Utils.Size(targetHitbox),
                proj.Center, proj.Center + unit * (proj.localAI[1] - 10f), 15f * proj.scale, ref point);
        }

        public override bool PreDraw(Player player, ref Color lightColor) => false;

        public static void PushVisual(Projectile p)
        {
            var len = p.localAI[1];
            if (len <= 4f || p.scale <= 0.01f) return;

            var color = XiheHelper.HsvToRgb(p.localAI[0], 1f, 0.8f);

            var origin = new Vector2(XiheTextures.WidthOf(Tex.ProjTex) / 2f, XiheTextures.HeightOf(Tex.ProjTex));
            var h = XiheTextures.HeightOf(Tex.ProjTex);
            XiheFx.Sprite(p.Center, Tex.ProjTex, color, p.rotation + 1.57f,
                new Vector2(p.scale, len / h), origin, additive: true, colorize: true);
        }
    }
}
