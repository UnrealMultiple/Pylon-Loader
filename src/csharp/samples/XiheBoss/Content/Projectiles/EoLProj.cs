using System;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria;
using Terraria.GameContent;

namespace XiheBoss
{
    public sealed class EoLProj : ModProjectile
    {
        private const float TwoPi = (float)(Math.PI * 2.0);

        /// <summary>MEAC 的 speedMuti（每发独立）。</summary>
        private float _speedMuti = 1f;

        /// <summary>源文件的 public int endTime（MEAC 里没人写它，恒 0；保留以便逐行对应）。</summary>
        public int EndTime;

        // PushVisual is static, so retain source oldPos buffers by projectile slot.
        // Keep both source lengths: alternating 5/7 and other modes must not allocate
        // a new array every time the same Terraria slot is reused.
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

        public override void SetDefaults(Projectile proj)
        {
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

        public override void OnSpawn(Projectile proj)
        {
            var ai = proj.ai;
            var mode = ai[1];

            _speedMuti = 1f;

            // 源：ai[1] == 4 且在屏幕内 → LightEff0（黄星星 + 环）。
            if (mode == 4f && IsInScreen(proj.Center))
            {
                XiheVisuals.Create(VfxKind.LightDust, proj.Center, Vector2.Zero).DrawColor = Color.Yellow;
            }

            // MEAC：mode 5/7 使用 15 个 oldPos，其余模式使用 30 个；完整保留拖尾，
            // 不用迁移版的 16/10 段 LOD，避免光弹长度和密度与原版不同。
            SetTrail(proj, mode == 5f || mode == 7f ? 15 : 30);

            if (mode == 8f)
            {
                proj.timeLeft = 400;
            }
        }

        public override void AI(Projectile proj)
        {
            proj.rotation += 0.05f;

            var ai = proj.ai;
            var mode = ai[1];

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
                // 源：Main.player[(int)ai[0]]（Boss 传的是 npc.target）。越界时源会抛异常，这里直接收掉。
                var idx = (int)ai[0];
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
                // 源：Main.npc[(int)ai[0]]（Boss 传的是 whoAmI）。
                var idx = (int)ai[0];
                if (idx < 0 || idx >= Main.npc.Length)
                {
                    proj.Kill();
                    return;
                }
                var npc = Main.npc[idx];

                // 源：velocity = Utils.ToRotationVector2(Utils.AngleLerp(ToRotation(velocity), ToRotation(dirTo), 0.015f))
                //             * (velocity.Length() + 0.02f)
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
                var idx = (int)ai[0];
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
                proj.velocity = XiheHelper.RotatedBy(proj.velocity, 0.02094395f);   // 1.2°
            }
            else if (mode == 7f)
            {
                var idx = (int)ai[0];
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
                if (proj.timeLeft > 340) proj.velocity = XiheHelper.RotatedBy(proj.velocity, 0.05f * ai[0]);
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
                // 源文件没有这条分支（Boss 的 Leave 阶段传 114514）：行为 = 通用尾段，什么都不做。
            }

            // 源：position += velocity * speedMuti（ShouldUpdatePosition 恒 false）。
            proj.position += proj.velocity * _speedMuti;

            // 源：屏幕内 + 1/8 概率 + 速度够快 → LightDust。
            if (IsInScreen(proj.Center) && XiheHelper.RandBool(8)
                && proj.velocity.Length() * Math.Abs(_speedMuti) > 10f)
            {
                var pos = proj.Center + XiheHelper.NextVector2Unit() * 10f;
                var vel = XiheHelper.RotatedBy(proj.velocity, XiheHelper.NextFloatDirection() * 0.2f) * 0.3f * _speedMuti;
                XiheVisuals.Create(VfxKind.LightDust, pos, vel).DrawColor = new Color(1f, 1f, 0.4f);
            }

            // 源：oldPos 整体后移一格，头部写当前 Center。
            var oldPos = TrailOf(proj);
            if (oldPos != null && oldPos.Length > 0)
            {
                for (var i = oldPos.Length - 1; i > 0; i--) oldPos[i] = oldPos[i - 1];
                oldPos[0] = proj.Center;
            }
        }

        public override void OnKill(Projectile proj, int timeLeft)
        {
            ClearTrail(proj);

            if (!IsInScreen(proj.Center)) return;

            // 源：Explosion（velocity 拖着走）+ 若干 LightDust（color 1,1,0.6 / 1,1,0.5）。
            var boom = XiheVisuals.Create(VfxKind.Explosion, proj.Center, proj.velocity);
            boom.DrawColor = new Color(1f, 1f, 0.6f);

            // MEAC Kill：默认 20 颗，模式 4/5/7 分别为 7/5/5 颗。
            var counts = 20;
            if (proj.ai[1] == 4f) counts = 7;
            if (proj.ai[1] == 5f) counts = 5;
            if (proj.ai[1] == 7f) counts = 5;

            for (var i = 0; i < counts; i++)
            {
                var speed = (float)(2.0 + Main.rand.NextDouble() * 8.0);   // Utils.NextFloat(rand, 2f, 10f)
                var dust = XiheVisuals.Create(VfxKind.LightDust, proj.Center,
                    XiheHelper.NextVector2Unit() * speed);
                dust.DrawColor = new Color(1f, 1f, 0.5f);
            }
        }

        public override bool ShouldUpdatePosition(Projectile proj) => false;

        public override bool PreDraw(Projectile proj, Color lightColor) => false;

        public override void PostDraw(Projectile proj, Color lightColor)
        {
            // MEAC PostDraw：预乘 AlphaBlend 下 RGB 非零、alpha=0 的三笔 Ball 仍可见。
            // 它们是光弹的旋转十字，不能按“全透明”删掉；太阳与十字共同使用 scale*0.7。
            try
            {
                _sun ??= TextureAssets.Sun?.Value;
                var scale = proj.scale * 0.7f;
                var position = proj.Center - Main.screenPosition;
                // The three alpha-zero Ball sprites are collected by PushVisual into
                // a single premultiplied mesh batch rather than three IL2CPP calls per bullet.
                if (_sun != null && scale > 0.01f)
                {
                    XiheDraw.Sprite(Main.spriteBatch, _sun, position,
                        new Rectangle(0, 0, _sun.Width, _sun.Height), new Color(1f, 1f, 1f, 0.75f), proj.rotation,
                        Utils.Size(_sun) / 2f, scale);
                }
            }
            catch (Exception e)
            {
                if (!_drawLogged) { _drawLogged = true; XiheBossMod.Log?.Warn("[eolproj] PreDraw 异常：" + e.Message); }
            }
        }

        private static Texture2D _sun;
        private static bool _drawLogged;

        // ================= 拖尾的静态表（PushVisual 拿不到实例） =================

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
            // Deactivate without discarding either reusable buffer. OnSpawn clears
            // the selected one before it can be observed by AI or PushVisual.
            Trails[i].Active = null;
        }

        private static bool IsInScreen(Vector2 world)
        {
            var sp = Main.screenPosition;
            return world.X >= sp.X && world.X <= sp.X + Main.screenWidth
                && world.Y >= sp.Y && world.Y <= sp.Y + Main.screenHeight;
        }

        // ================= 画面 =================

        /// <summary>MEAC PostDraw 的完整15/30点Trail.fx三角带；直接写入同材质顶点批。</summary>
        public static void PushVisual(Projectile p)
        {
            var trail = TrailOf(p);
            var center = p.Center;
            if (trail != null) XiheMesh.AddEolTrail(center, trail, p.timeLeft * 0.04f);
            XiheMesh.AddEolHead(center, p.rotation, p.scale * 0.7f);
        }
    }
}










