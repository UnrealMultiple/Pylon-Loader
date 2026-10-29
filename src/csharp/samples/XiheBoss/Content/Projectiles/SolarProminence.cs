using System;
using System.Collections.Generic;
using Il2CppMicrosoft.Xna.Framework;
using Color = Il2CppMicrosoft.Xna.Framework.Graphics.Color;
using Pylon.ModLoader;
using Terraria;

namespace XiheBoss
{
    public sealed class SolarProminence : ModProjectile
    {
        private int _maxTime = 120;
        private List<Vector2> _pos = new();

        private static readonly Dictionary<int, Vector2> Centers = new();
        private static readonly Dictionary<int, float> Radii = new();
        private static readonly Dictionary<int, List<Vector2>> TrailPos = new();

        public override string TexturePath => null;

        private static int Timer(Projectile p, int maxTime) => 100 - p.timeLeft;

        public override void SetDefaults(Projectile proj)
        {
            proj.width = 15;
            proj.height = 15;
            proj.aiStyle = -1;
            proj.hostile = true;
            proj.friendly = false;
            proj.timeLeft = _maxTime = 120;
            proj.ignoreWater = true;
            proj.tileCollide = false;
            proj.penetrate = -1;
            proj.scale = 0f;
        }

        public override void OnSpawn(Projectile proj)
        {
            TrailPos[proj.whoAmI] = new List<Vector2>();
        }

        public override void OnKill(Projectile proj, int timeLeft)
        {
            Centers.Remove(proj.whoAmI);
            Radii.Remove(proj.whoAmI);
            TrailPos.Remove(proj.whoAmI);
        }

        /// <summary>源 SunProj 生成后直接写 solarCenter / solarRadius，再 SetTrail()。</summary>
        internal static void Configure(Projectile proj, Vector2 center, float radius, bool setTrail = true)
        {
            if (proj == null) return;
            Centers[proj.whoAmI] = center;
            Radii[proj.whoAmI] = radius;
            if (!TrailPos.TryGetValue(proj.whoAmI, out var list))
                TrailPos[proj.whoAmI] = list = new List<Vector2>();
            list.Clear();
            if (setTrail) SetTrail(proj, list, center, radius);
        }

        private static void SetTrail(Projectile proj, List<Vector2> pos, Vector2 solarCenter, float solarRadius)
        {
            pos.Clear();
            // 源 SetupTrail：固定最多 100 点（之前写成 40，火舌比 MEAC 短了一半）。
            for (var i = 0; i < 100; i++)
            {
                pos.Add(proj.Center);
                proj.Center += proj.velocity;
                proj.velocity = XiheHelper.RotatedBy(proj.velocity, 0.10000000149011612f);
                if (Vector2.Distance(solarCenter, proj.Center) < solarRadius) break;
            }

            for (var j = 0; j < pos.Count; j++)
            {
                var v = solarCenter - pos[j];
                pos[j] += XiheHelper.Normalize2(v) * (float)Math.Pow(v.Length(), 1.5) * 0.01f;
            }
        }

        public override void AI(Projectile proj)
        {
            var timer = Timer(proj, _maxTime);
            if (timer == 0)
            {
                if (Centers.TryGetValue(proj.whoAmI, out var center)
                    && Radii.TryGetValue(proj.whoAmI, out var radius)
                    && TrailPos.TryGetValue(proj.whoAmI, out var list))
                {
                    SetTrail(proj, list, center, radius);
                }
            }

            var scale = proj.scale;
            if (timer < 15) XiheHelper.LerpValue(ref scale, 1f, 0.02f);
            else if (timer < 45) XiheHelper.LerpValue(ref scale, 0f, 0.11f);
            if (timer > 65 && timer < 80) XiheHelper.LerpValue(ref scale, 1f, 0.2f);
            if (proj.timeLeft < 10) XiheHelper.LerpValue(ref scale, 0f, 0.2f);
            proj.scale = scale;
        }

        public override bool ShouldUpdatePosition(Projectile proj) => false;

        public override bool CanDamage(Projectile proj) => proj.scale > 0.1f && Timer(proj, _maxTime) > 65;

        public override bool PreDraw(Projectile proj, Color lightColor) => false;

        public override bool? Colliding(Projectile proj, Rectangle projHitbox, Rectangle targetHitbox)
        {
            if (!TrailPos.TryGetValue(proj.whoAmI, out var pos)) return null;
            var point = 0f;
            for (var i = 0; i < pos.Count - 2; i += 2)
            {
                if (Collision.CheckAABBvLineCollision(Utils.TopLeft(targetHitbox), Utils.Size(targetHitbox),
                        pos[i], pos[i + 2], 60f * proj.scale, ref point))
                    return true;
            }
            return null;
        }

        public static void PushVisual(Projectile p)
        {
            if (!TrailPos.TryGetValue(p.whoAmI, out var pos) || pos.Count < 3) return;
            var timer = Timer(p, 120);
            var mode = timer < 60 ? 0 : 1;
            var points = new List<TrailStripPoint>(pos.Count);
            for (var i = 1; i < pos.Count && pos[i] != Vector2.Zero; i++)
            {
                var normalDir = pos[i - 1] - pos[i];
                normalDir = XiheHelper.Normalize2(new Vector2(-normalDir.Y, normalDir.X));
                var factor = 1f - (float)i / pos.Count;
                var width = 120f * p.scale * (0.4f + p.velocity.Length() / 150f);
                var w = mode == 0 ? 0.3f : 1.5f;
                w = factor < 0.5f ? w * (factor * 2f) : w * ((1f - factor) * 2f);
                w = w * 1.5f + 0.1f;
                points.Add(new TrailStripPoint
                {
                    Left = pos[i] + normalDir * width,
                    Right = pos[i] - normalDir * width,
                    UvX = (float)Math.Sqrt(factor),
                    W = w,
                });
            }

            if (points.Count < 2) return;

            // 源 Draw：mode 0 = tex3、mode 1 = tex4，img_color 取色，Projectile_0 叠加，Additive。
            // 走 Trail.fx 的精确逐像素通道（硬件几何），不再在 CPU 上采样颜色图。
            var tex = mode == 0 ? Tex.Tex3 : Tex.Tex4;
            XiheFx.TrailStrip(points, tex, Tex.ImgColor, Tex.Transparent,
                p.timeLeft * 0.03f, additive: true);
        }
    }
}
