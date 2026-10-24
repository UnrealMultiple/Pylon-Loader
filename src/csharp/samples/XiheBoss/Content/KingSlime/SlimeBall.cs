using System;
using Il2CppMicrosoft.Xna.Framework;
using Color = Il2CppMicrosoft.Xna.Framework.Graphics.Color;
using Pylon.ModLoader;
using Pylon.ModLoader.Content;
using Terraria;

namespace XiheBoss.KingSlime
{
    public sealed class SlimeBall : ModProjectile
    {
        public override string TexturePath => null;

        public override void SetStaticDefaults()
        {
            Main.projFrames[Type] = 1;
        }

        public override void SetDefaults()
        {
            var proj = Projectile;
            proj.width = 25;
            proj.height = 25;
            proj.aiStyle = -1;
            proj.hostile = true;
            proj.friendly = false;
            proj.timeLeft = 300;
            proj.ignoreWater = true;
            proj.tileCollide = true;
            proj.penetrate = -1;
            proj.scale = 0.8f;
            proj.alpha = 100;
        }

        public override bool OnTileCollide(Vector2 oldVelocity)
        {
            var proj = Projectile;
            var ai = proj.ai;
            for (int i = 0; i < 10; i++)
            {
                var v = Utils.NextVector2Unit(Main.rand, 0f, (float)Math.PI * 2f) * XiheHelper.NextFloat(1f, 5f);
                var obj = Dust.NewDustPerfect(proj.Center, 116, null, 0, default(Color), XiheHelper.NextFloat(1f, 2f));
                obj.velocity = v;
                obj.noGravity = true;
            }
            ai[0] += 1f;
            proj.ai = ai;
            if (ai[0] < 2f)
            {
                if (proj.velocity.X != oldVelocity.X)
                {
                    var v = proj.velocity;
                    v.X = -oldVelocity.X * 0.4f;
                    proj.velocity = v;
                }
                if (proj.velocity.Y != oldVelocity.Y)
                {
                    var v = proj.velocity;
                    v.Y = -oldVelocity.Y * 0.8f;
                    v.X = oldVelocity.X * 0.8f;
                    proj.velocity = v;
                }
                return false;
            }
            for (int j = 0; j < 15; j++)
            {
                var v2 = Utils.NextVector2Unit(Main.rand, 0f, (float)Math.PI * 2f) * XiheHelper.NextFloat(1f, 15f);
                var obj2 = Dust.NewDustPerfect(proj.Center, 116, null, 0, default(Color), XiheHelper.NextFloat(1f, 2f));
                obj2.velocity = v2;
                obj2.noGravity = true;
            }
            return true;
        }

        public override void AI()
        {
            var proj = Projectile;
            var vel = proj.velocity;
            vel.Y += 0.15f;
            proj.velocity = vel;
            var ai = proj.ai;
            ai[1] = 1.5f + (float)Math.Sin((float)proj.timeLeft * 0.3f) * 0.2f;
            proj.ai = ai;
            proj.rotation = Utils.ToRotation(proj.velocity);
        }

        public override bool PreDraw(Player player, ref Color lightColor) => false;

        public static void PushVisual(Projectile p)
        {
            var ai = p.ai;
            var light = Lighting.GetColor((int)(p.Center.X / 16f), (int)(p.Center.Y / 16f));
            var color = XiheHelper.MulClamp(light, (255 - p.alpha) / 255f);
            var w = XiheTextures.WidthOf(Tex.SlimeBall);
            var h = XiheTextures.HeightOf(Tex.SlimeBall);
            XiheFx.Sprite(p.Center, Tex.SlimeBall, color, p.rotation,
                new Vector2(ai[1], 1f) * p.scale, new Vector2(w, h) / 2f, additive: false);
        }
    }
}
