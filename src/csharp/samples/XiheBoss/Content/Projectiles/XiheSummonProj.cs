using System;
using System.Collections.Generic;
using Il2CppMicrosoft.Xna.Framework;
using Color = Il2CppMicrosoft.Xna.Framework.Graphics.Color;
using Pylon.ModLoader;
using Terraria;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;

namespace XiheBoss
{

    public sealed class XiheSummonProj : ModProjectile
    {
        public override string TexturePath => null;

        public override void SetStaticDefaults()
        {
            Main.projFrames[Type] = 1;
        }

        public override void SetDefaults()
        {
            var proj = Projectile;
            proj.width = 10;
            proj.height = 10;
            proj.aiStyle = -1;
            proj.hostile = false;
            proj.friendly = true;
            proj.tileCollide = false;
            proj.timeLeft = 100;
        }

        public override bool CanDamage() => false;

        public override bool ShouldUpdatePosition() => false;

        public override void AI()
        {
            var proj = Projectile;
            var player = Main.player[proj.owner];
            if (player == null || !player.active)
            {
                proj.Kill();
                return;
            }

            proj.Center = player.Center;
            var i = (100 - proj.timeLeft) * 0.1f;
            XiheFx.ScreenShake(i, proj.Center);

            if (proj.timeLeft > 20)
            {
                proj.ai[0] = MathHelper.Lerp(proj.ai[0], 63.636364f, 0.1f);
                proj.scale = MathHelper.Lerp(proj.scale, 4f, 0.05f);
            }
            proj.rotation += 0.01f;

            if (proj.timeLeft < 10)
            {
                proj.alpha += 25;
                proj.scale += 0.5f;
            }
            if (proj.timeLeft == 10)
            {

                XiheFx.RadialGlur(proj.Center, 20, true);
                XiheBossNpc.SpawnCountdown = 15;
            }
        }

        public override bool PreDraw(Player player, ref Color lightColor) => false;

        public static void PushVisual(Projectile p)
        {
            var alpha = 1f - p.alpha / 255f;
            if (alpha <= 0f) return;

            var color = new Color(255, 255, 130) * alpha;

            var verts = new List<TriVertex>(512);

            var curve = Create(p.ai[0] + 0.001f, 100f, 0.15f, 70);
            var inner = new Vector2[curve.Length];
            var outer = new Vector2[curve.Length];
            for (var i = 0; i < curve.Length; i++)
            {
                var c = XiheHelper.RotatedBy(curve[i], p.rotation);
                inner[i] = p.Center + c * p.scale;
                outer[i] = p.Center + c * (p.scale * 1.02f + 0.05f);
            }
            AddStrip(verts, inner, outer, color);

            AddRing(verts, p.Center, 100f * p.scale, 100.5f * p.scale + 2f, 100, color);
            AddRing(verts, p.Center, 110f * p.scale, 110.5f * p.scale + 2f, 100, color);

            if (verts.Count >= 3)
                XiheFx.Tri(Tex.MagicPixel, false, true, false, false, verts.ToArray());
        }

        private static Vector2[] Create(float r1, float r2, float v, int max)
        {
            var list = new List<Vector2>();
            for (var t = 0f; t < max; t += v)
            {
                var d = r2 - r1;
                list.Add(new Vector2(
                    d * (float)Math.Cos(t) + r1 * (float)Math.Cos(d / r1 * t),
                    d * (float)Math.Sin(t) - r1 * (float)Math.Sin(d / r1 * t)));
            }
            return list.ToArray();
        }

        private static void AddRing(List<TriVertex> verts, Vector2 pos, float radius, float outerRadius,
            int counts, Color color)
        {
            var inner = new Vector2[counts + 1];
            var outer = new Vector2[counts + 1];
            for (var j = 0; j <= counts; j++)
            {
                var dir = XiheHelper.Vxr(j * 6.283f / counts);
                inner[j] = pos + dir * radius;
                outer[j] = pos + dir * outerRadius;
            }
            AddStrip(verts, inner, outer, color);
        }

        private static void AddStrip(List<TriVertex> verts, Vector2[] inner, Vector2[] outer, Color color)
        {
            for (var i = 0; i + 1 < inner.Length; i++)
            {
                verts.Add(new TriVertex { World = inner[i], Color = color });
                verts.Add(new TriVertex { World = outer[i], Color = color });
                verts.Add(new TriVertex { World = inner[i + 1], Color = color });
                verts.Add(new TriVertex { World = outer[i], Color = color });
                verts.Add(new TriVertex { World = outer[i + 1], Color = color });
                verts.Add(new TriVertex { World = inner[i + 1], Color = color });
            }
        }
    }
}
