using System;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria;
using Terraria.GameContent;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;

namespace XiheBoss
{
    public sealed class EoLProj : ModProjectile
    {
        private const float TwoPi = (float)(Math.PI * 2.0);

        private float _speedMuti = 1f;

        public int EndTime;

        private sealed class TrailBuffers
        {
            public Vector2[] Short;
            public Vector2[] Long;
            public Vector2[] Active;
        }

        private static readonly TrailBuffers[] Trails = new TrailBuffers[4096];

        public override string TexturePath => null;

        public override void SetStaticDefaults()
        {
            Main.projFrames[Type] = 1;
        }

        public override void SetDefaults()
        {
            var proj = Projectile;
            proj.width = 5;
            proj.height = 5;
            proj.aiStyle = -1;
            proj.hostile = true;
            proj.friendly = false;
            proj.timeLeft = 300;
            proj.ignoreWater = true;
            proj.tileCollide = false;
            proj.penetrate = -1;
            proj.scale = 1f;
            proj.extraUpdates = 1;
        }

        public override void OnSpawn(Terraria.DataStructures.IEntitySource source)
        {
            var proj = Projectile;

            var mode = proj.ai[1];

            _speedMuti = 1f;

            if (mode == 4f && IsInScreen(proj.Center))
            {
                XiheVisuals.Create(VfxKind.LightDust, proj.Center, Vector2.Zero).DrawColor = Color.Yellow;
            }

            SetTrail(proj, mode == 5f || mode == 7f ? 15 : 30);

            if (mode == 8f)
            {
                proj.timeLeft = 400;
            }
        }

        public override void AI()
        {
            var proj = Projectile;
            proj.rotation += 0.05f;

            var mode = proj.ai[1];

            if (mode == 0f)
            {
                if (proj.timeLeft > 240) _speedMuti -= 0.0166f;
                else _speedMuti += 0.02f;
            }
            else if (mode == 1f)
            {
                if (proj.timeLeft > 275) _speedMuti -= 0.016f;
                else if (proj.velocity.Length() * _speedMuti < 16f) _speedMuti += 0.025f;
                proj.velocity = XiheHelper.RotatedBy(proj.velocity, 0.005f);
            }
            else if (mode == 2f)
            {
                if (proj.timeLeft > 275) _speedMuti -= 0.016f;
                else if (proj.velocity.Length() * _speedMuti < 16f) _speedMuti += 0.025f;
                proj.velocity = XiheHelper.RotatedBy(proj.velocity, -0.005f);
            }
            else if (mode == 3f)
            {

                var idx = (int)proj.ai[0];
                if (idx < 0 || idx >= Main.player.Length)
                {
                    proj.Kill();
                    return;
                }
                var target = Main.player[idx];

                if (proj.timeLeft > 240)
                {
                    _speedMuti -= 0.015f;
                    proj.velocity = Vector2.Lerp(proj.velocity,
                        XiheHelper.DirectionTo(proj.Center, target.Center) * 5f, 0.01f);
                }
                else if (proj.timeLeft > 120)
                {
                    proj.velocity = Vector2.Lerp(proj.velocity,
                        XiheHelper.DirectionTo(proj.Center, target.Center) * 20f, 0.012f);
                    _speedMuti += 0.01f;
                }

                if (proj.timeLeft < EndTime && proj.timeLeft > 1) proj.timeLeft = 1;
            }
            else if (mode == 4f)
            {

                var idx = (int)proj.ai[0];
                if (idx < 0 || idx >= Main.npc.Length)
                {
                    proj.Kill();
                    return;
                }
                var npc = Main.npc[idx];

                var cur = (float)Math.Atan2(proj.velocity.Y, proj.velocity.X);
                var want = (float)Math.Atan2(npc.Center.Y - proj.Center.Y, npc.Center.X - proj.Center.X);
                var diff = want - cur;
                if (diff > Math.PI) diff -= TwoPi;
                if (diff < -Math.PI) diff += TwoPi;
                proj.velocity = XiheHelper.Vxr(cur + diff * 0.015f) * (proj.velocity.Length() + 0.02f);

                if (Vector2.Distance(npc.Center, proj.Center) < 70f) proj.Kill();
            }
            else if (mode == 5f)
            {
                var idx = (int)proj.ai[0];
                if (idx < 0 || idx >= Main.npc.Length)
                {
                    proj.Kill();
                    return;
                }
                var npc = Main.npc[idx];

                if (proj.timeLeft > 160) _speedMuti += 0.02f;
                else _speedMuti -= 0.027f;
                if (Vector2.Distance(npc.Center, proj.Center) < 30f) proj.Kill();
            }
            else if (mode == 6f)
            {
                proj.extraUpdates = 0;
                proj.velocity = XiheHelper.RotatedBy(proj.velocity, 0.02094395f);
            }
            else if (mode == 7f)
            {
                var idx = (int)proj.ai[0];
                if (idx < 0 || idx >= Main.npc.Length)
                {
                    proj.Kill();
                    return;
                }
                var npc = Main.npc[idx];

                _speedMuti += 0.08f;
                if (Vector2.Distance(npc.Center, proj.Center) < 30f) proj.Kill();
            }
            else if (mode == 8f)
            {
                if (proj.timeLeft > 340) proj.velocity = XiheHelper.RotatedBy(proj.velocity, 0.05f * proj.ai[0]);
            }
            else if (mode == 9f)
            {
                var r = XiheHelper.Vxr(proj.timeLeft * 0.05f);
                if (proj.timeLeft % 7 == 0)
                {
                    Projectile.NewProjectile(null, proj.Center + r * 20f, r, ModContent.ProjectileType<EoLLance>(),
                        proj.damage, 0f, Main.myPlayer, 50f, 2f, 0f);
                }
            }
            else if (mode == 114514f)
            {

            }

            proj.position += proj.velocity * _speedMuti;

            if (IsInScreen(proj.Center) && XiheHelper.RandBool(8)
                && proj.velocity.Length() * Math.Abs(_speedMuti) > 10f)
            {
                var pos = proj.Center + XiheHelper.NextVector2Unit() * 10f;
                var vel = XiheHelper.RotatedBy(proj.velocity, XiheHelper.NextFloatDirection() * 0.2f) * 0.3f * _speedMuti;
                XiheVisuals.Create(VfxKind.LightDust, pos, vel).DrawColor = new Color(1f, 1f, 0.4f);
            }

            var oldPos = TrailOf(proj);
            if (oldPos != null && oldPos.Length > 0)
            {
                for (var i = oldPos.Length - 1; i > 0; i--) oldPos[i] = oldPos[i - 1];
                oldPos[0] = proj.Center;
            }
        }

        public override void OnKill(int timeLeft)
        {
            var proj = Projectile;
            ClearTrail(proj);

            if (!IsInScreen(proj.Center)) return;

            var boom = XiheVisuals.Create(VfxKind.Explosion, proj.Center, proj.velocity);
            boom.DrawColor = new Color(1f, 1f, 0.6f);

            var counts = 20;
            if (proj.ai[1] == 4f) counts = 7;
            if (proj.ai[1] == 5f) counts = 5;
            if (proj.ai[1] == 7f) counts = 5;

            for (var i = 0; i < counts; i++)
            {
                var speed = (float)(2.0 + Main.rand.NextDouble() * 8.0);
                var dust = XiheVisuals.Create(VfxKind.LightDust, proj.Center,
                    XiheHelper.NextVector2Unit() * speed);
                dust.DrawColor = new Color(1f, 1f, 0.5f);
            }
        }

        public override bool ShouldUpdatePosition() => false;

        public override bool PreDraw(Player player, ref Color lightColor) => false;

        public override void PostDraw(Player player, Color lightColor)
        {
            var proj = Projectile;

            try
            {
                _sun ??= TextureAssets.Sun?.Value;
                var scale = proj.scale * 0.7f;
                var position = proj.Center - Main.screenPosition;

                if (_sun != null && scale > 0.01f)
                {
                    XiheDraw.Sprite(Main.spriteBatch, _sun, position,
                        new Rectangle(0, 0, _sun.Width, _sun.Height), new Color(1f, 1f, 1f, 0.75f), proj.rotation,
                        Utils.Size(_sun) / 2f, scale);
                }
            }
            catch (Exception e)
            {
                if (!_drawLogged) { _drawLogged = true; XiheBossMod.Log?.Warn("[eolproj] PreDraw 异常：" + e); }
            }
        }

        private static Texture2D _sun;
        private static bool _drawLogged;

        private static Vector2[] TrailOf(Projectile proj)
        {
            var i = proj.whoAmI;
            return i >= 0 && i < Trails.Length ? Trails[i]?.Active : null;
        }

        private static void SetTrail(Projectile proj, int length)
        {
            var i = proj.whoAmI;
            if (i < 0 || i >= Trails.Length) return;
            var buffers = Trails[i] ??= new TrailBuffers();
            var trail = length == 15
                ? (buffers.Short ??= new Vector2[15])
                : (buffers.Long ??= new Vector2[30]);
            Array.Clear(trail, 0, trail.Length);
            buffers.Active = trail;
        }

        private static void ClearTrail(Projectile proj)
        {
            var i = proj.whoAmI;
            if (i < 0 || i >= Trails.Length || Trails[i] == null) return;

            Trails[i].Active = null;
        }

        private static bool IsInScreen(Vector2 world)
        {
            var sp = Main.screenPosition;
            return world.X >= sp.X && world.X <= sp.X + Main.screenWidth
                && world.Y >= sp.Y && world.Y <= sp.Y + Main.screenHeight;
        }

        public static void PushVisual(Projectile p)
        {
            var trail = TrailOf(p);
            var center = p.Center;
            if (trail != null) XiheMesh.AddEolTrail(center, trail, p.timeLeft * 0.04f);
            XiheMesh.AddEolHead(center, p.rotation, p.scale * 0.7f);
        }
    }
}
