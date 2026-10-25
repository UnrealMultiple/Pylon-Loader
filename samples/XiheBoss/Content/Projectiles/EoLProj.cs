// EoLProj（MEAC：MEAC.Bosses.EoL.Projectiles.EoLProj）—— 羲和最基础的弹幕。
//
// 源文件：.work\meac\MEAC.Bosses.EoL.Projectiles\EoLProj.cs
//   AI 逐行照抄，ai[1] 的分支 0/1/2/3/4/5/6/7/8/9/114514 全部在（114514 在源里没有对应块，
//   走的是通用尾段：自转 + 位移 + 尾迹；这里显式留一个空分支并注明）。
// ai 约定（PORTING.md）：ai[0] = 源 ai[0]（玩家下标 / NPC 下标 / 旋转方向），ai[1] = 源 ai[1]（行为）。
//
// 视觉（源 PostDraw）：
//   * 30 点轨迹条带：TrailShape 贴图、半宽 15px、滚动 t = timeLeft * 0.04；
//   * 头部 4 层精灵：Ball 拉伸成 4x0.5 与 5x0.3 两个交叉椭圆 + Sun 圆盘。
//   这里用 1 条 beam（整条轨迹的弦）+ 2 个 orb（EolProj 光球 + 白心）近似。
// 近似点：
//   1) 源 oldPos 的采样点按 whoAmI 存在静态表里 —— PushVisual 是 static，按约定不能强转 ModProjectile；
//   2) 两个交叉拉伸的 Ball 椭圆 + Sun 圆盘合成「1 个 EolProj 光球 + 1 个白心」，避免同屏几十发时抢 beam 预算；
//   3) 源 Kill 里的 Explosion（7 帧贴图）没有等价的 VfxKind，用一发放大的 LightDust 近似；
//   4) 源 OnSpawn 的 LightEff0（星形 + 环）/ AI 与 Kill 的 LightDust 用 XiheVisuals 的 LightDust 近似。
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

        /// <summary>源文件的 oldPos：按 whoAmI 索引的静态表（见文件头近似点 1）。</summary>
        private static Vector2[][] _trails;
        private const int TrailSlots = 4096;

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

            // 源：mode 5/7 的 oldPos 只有 15 个点，其余 30 个。手机端保留 16/10 段缓存，
            // 同屏图元过多时 PushVisual 再按 LOD 只提交前 8 段。
            SetTrail(proj, mode == 5f || mode == 7f ? 10 : 16);

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

            var counts = 8;
            if (proj.ai[1] == 4f) counts = 5;
            if (proj.ai[1] == 5f) counts = 4;
            if (proj.ai[1] == 7f) counts = 4;

            for (var i = 0; i < counts; i++)
            {
                var speed = (float)(2.0 + Main.rand.NextDouble() * 8.0);   // Utils.NextFloat(rand, 2f, 10f)
                var dust = XiheVisuals.Create(VfxKind.LightDust, proj.Center,
                    XiheHelper.NextVector2Unit() * speed);
                dust.DrawColor = new Color(1f, 1f, 0.5f);
            }
        }

        public override bool ShouldUpdatePosition(Projectile proj) => false;

        public override bool PreDraw(Projectile proj, Color lightColor)
        {
            // 源 PreDraw：头部是 TextureAssets.Sun（原版太阳），Color(1,1,1,0.75)，scale = projectile.scale。
            // 那三笔 Ball 十字在源里 alpha=0（AlphaBlend 下不可见），不要画。
            try
            {
                _sun ??= TextureAssets.Sun?.Value;
                if (_sun != null && proj.scale > 0.01f)
                {
                    var a = MathHelper.Clamp(1f - proj.alpha / 255f, 0f, 1f);
                    var color = new Color(1f, 1f, 1f, 0.75f) * a;
                    XiheDraw.Sprite(Main.spriteBatch, _sun, proj.Center - Main.screenPosition,
                        new Rectangle(0, 0, _sun.Width, _sun.Height), color, proj.rotation,
                        Utils.Size(_sun) / 2f, proj.scale);
                }
            }
            catch (Exception e)
            {
                if (!_drawLogged) { _drawLogged = true; XiheBossMod.Log?.Warn("[eolproj] PreDraw 异常：" + e.Message); }
            }
            return false;
        }

        private static Texture2D _sun;
        private static bool _drawLogged;

        // ================= 拖尾的静态表（PushVisual 拿不到实例） =================

        private static Vector2[] TrailOf(Projectile proj)
        {
            if (_trails == null) return null;
            var i = proj.whoAmI;
            if (i < 0 || i >= _trails.Length) return null;
            return _trails[i];
        }

        private static void SetTrail(Projectile proj, int length)
        {
            if (_trails == null) _trails = new Vector2[TrailSlots][];
            var i = proj.whoAmI;
            if (i < 0 || i >= _trails.Length) return;
            _trails[i] = new Vector2[length];
        }

        private static void ClearTrail(Projectile proj)
        {
            if (_trails == null) return;
            var i = proj.whoAmI;
            if (i < 0 || i >= _trails.Length) return;
            _trails[i] = null;
        }

        private static bool IsInScreen(Vector2 world)
        {
            var sp = Main.screenPosition;
            return world.X >= sp.X && world.X <= sp.X + Main.screenWidth
                && world.Y >= sp.Y && world.Y <= sp.Y + Main.screenHeight;
        }

        // ================= 画面 =================

        /// <summary>源 PostDraw：Trail.fx pass 0 的三角带（每段左右两个顶点）；头部 Sun 在 PreDraw 里画。</summary>
        public static void PushVisual(Projectile p)
        {
            // 源头部由 PreDraw 的 Sun 负责；这里只画 Trail.fx 的三角带。
            var trail = TrailOf(p);
            if (trail == null || trail.Length < 2) return;

            var counts = 0;
            for (var i = 0; i < trail.Length; i++)
                if (trail[i] != Vector2.Zero) counts++;

            // 源 PostDraw：j 每次 +1（每一对相邻 oldPos 一个 quad），不是隔点取段。
            // 之前隔点取段让尾巴只有一半长度、UV factor 只到 0.5，和 MEAC 不一样。
            var points = new System.Collections.Generic.List<TrailStripPoint>(trail.Length);
            for (var j = 0; j < trail.Length - 1 && trail[j + 1] != Vector2.Zero; j++)
            {
                var normalDir = trail[j] - trail[j + 1];
                normalDir = XiheHelper.Normalize2(new Vector2(-normalDir.Y, normalDir.X));
                var factor = counts <= 1 ? 0f : j / (counts - 1f);
                var mid = (trail[j] + trail[j + 1]) / 2f;
                const float width = 15f;
                points.Add(new TrailStripPoint
                {
                    Left = mid + normalDir * width,
                    Right = mid - normalDir * width,
                    UvX = (float)Math.Sqrt(factor),
                    W = MathHelper.Lerp(1.2f, 0f, (float)Math.Pow(factor, 0.6f)),
                });
            }

            if (points.Count < 2) return;

            // 源：Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.PointWrap, ...)
            // → Trail.fx pass 0，t = timeLeft * 0.04。之前这里误用了 Additive（画面偏亮）。
            XiheFx.TrailStrip(points, Tex.TrailShape, Tex.EolProjColor, Tex.Transparent,
                p.timeLeft * 0.04f, additive: false);
        }
    }
}










