// 摆线（源：MEAC.Bosses.EoL.Projectiles.Cycloid 1:1 移植）。
//
// AI 逐行照抄：840 tick 生命周期、每 51.5 tick 的 Shoot（timeLeft 634/583/531/480）、
// timeLeft 370/385/410 的 10 向 EoLProj、410~80 段的环形弹幕、三段 scale / alpha / localAI 的
// Lerp（>428、>=20、<20）、25/10 tick 的收尾 Shoot、p2 在 210 tick 起的 localAI[1] 收缩，
// 以及 Colliding 的 3000px / 100*scale / localAI[1]*28 判定。全部数值与时序与源文件一致。
//
// ai 约定（PORTING.md）：
//   ai[0] = 宿主 NPC 下标（源 ownerIndex），ai[1] = 1 表示 p2（源 p2）。
// 源文件自己的 ai[0]（摆线半径 r1）挪到 ai[2]，源 ai[1]（角度偏移）挪到 localAI[2]
// （手机版 localAI 只有 3 格，没有 localAI[3]）；localAI[0] / localAI[1] 保持源含义。
// PushVisual 只读这些 proj 字段，drawEffTimer 只留在实例字段。
//
// 视觉近似点（原版 PreDraw 的抓屏/反色在 Pylon 里做不了）：
// - 源把 Cycloid 画进 Main.screenTargetSwap、再用 Hide shader 在屏幕上挖一个洞（抓屏 + 反色 +
//   顶点绘制）；Pylon 的手机版代理不能碰 screenTargetSwap，也没有原生 Effect。这里用
//   XiheFx.Flash 闪白 + RadialGlur 径向模糊 + ScreenShake 近似“屏幕被撕开”的冲击，
//   摆线本体用 Ring(WarpRing/MagicRing) + Orb(WarpBall) 按源文件的半径/时间重画。
// - 源的全屏黄色覆盖 (255,255,150)*(drawEffTimer/70) 用 Shoot 里的 XiheFx.Flash(0.6f) 近似。
// - 源的 30/100 段顶点摆线无法逐点提交，这里取 6 个曲线节点画 Orbs，主体靠两圈 Ring 表达。
using System;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria;
using Terraria.ID;

namespace XiheBoss
{
    public sealed class Cycloid : ModProjectile
    {
        // ---- 源文件的实例状态（每发弹幕一份；PushVisual 需要的那份同步进 ai/localAI）----
        private int _ownerIndex = -1;   // 源 ownerIndex（来自 ai[0]）
        private bool _p2;               // 源 p2（来自 ai[1]）
        private int _canDamageTimer;    // 源 canDamageTimer
        private int _drawEffTimer;      // 源 drawEffTimer
        private float _r1;              // 源 ai[0]：摆线内半径
        private float _angle;           // 源 ai[1]：角度偏移
        private float _local0;          // 源 localAI[0]：内层摆线半径参数
        private float _local1;          // 源 localAI[1]：内层摆线缩放

        // drawEffTimer 只影响画面，不进 ai；按 whoAmI 存一份给静态 PushVisual 读。
        private static readonly System.Collections.Generic.Dictionary<int, int> DrawEff = new();

        public override string TexturePath => null;

        public override void SetStaticDefaults()
        {
            Main.projFrames[Type] = 1;
        }

        public override void SetDefaults(Projectile proj)
        {
            proj.width = 1;
            proj.height = 1;
            proj.aiStyle = -1;
            proj.timeLeft = 840;
            proj.scale = 0f;
            proj.hostile = true;
            proj.friendly = false;
            proj.tileCollide = true;
            proj.penetrate = 2;
            proj.ignoreWater = false;
        }

        public override void OnSpawn(Projectile proj)
        {
            var ai = proj.ai;
            var l = proj.localAI;
            _ownerIndex = (int)ai[0];
            _p2 = ai[1] == 1f;
            _r1 = ai[2];
            _angle = l[2];
            _local0 = l[0];
            _local1 = l[1];
            DrawEff[proj.whoAmI] = 0;
        }

        public override void OnKill(Projectile proj, int timeLeft) => DrawEff.Remove(proj.whoAmI);

        public override void AI(Projectile proj)
        {
            if (_canDamageTimer > 0) _canDamageTimer--;
            if (_drawEffTimer > 0) _drawEffTimer--;
            DrawEff[proj.whoAmI] = _drawEffTimer;

            if (_ownerIndex < 0 || _ownerIndex >= Main.npc.Length)
            {
                proj.Kill();
                return;
            }
            var owner = Main.npc[_ownerIndex];
            if (owner == null || !owner.active || owner.type != ModContent.NpcType<XiheBossNpc>())
            {
                proj.Kill();
                return;
            }

            proj.Center = owner.Center;

            if (proj.timeLeft == 840)
            {
                _r1 = 33.333332f;
                _angle = (float)Main.rand.NextDouble() * 10f;
            }

            if (proj.timeLeft == 704)
            {
                XiheVisuals.Create(VfxKind.Circle, proj.Center, Vector2.Zero);
            }

            // 源：for i = 4..7，每 51.5 tick 一次 Shoot
            for (var i = 4; i <= 7; i++)
            {
                if (proj.timeLeft == 840 - (int)(51.5f * i)) Shoot(proj);
            }

            if (proj.timeLeft <= 410 && proj.timeLeft > 80)
            {
                if (proj.timeLeft == 370 || proj.timeLeft == 385 || proj.timeLeft == 410)
                {
                    var vec = Utils.ToRotationVector2((float)Main.rand.NextDouble() * 8f);
                    for (var j = 0; j < 10; j++)
                    {
                        var vel = XiheHelper.RotatedBy(vec, j * (float)Math.PI * 2f / 10f) * 8f;
                        var idx = Projectile.NewProjectile(null, proj.Center, vel,
                            ModContent.ProjectileType<EoLProj>(), proj.damage / 2, 0f, Main.myPlayer,
                            0f, 6f, 0f);
                        if (idx >= 0 && idx < Main.projectile.Length) Main.projectile[idx].timeLeft = 300;
                    }
                }
            }

            if (proj.timeLeft > 428)
            {
                _r1 = MathHelper.Lerp(_r1, 69.23077f, 0.1f);
                proj.scale = MathHelper.Lerp(proj.scale, 4f, 0.05f);
                if (proj.timeLeft < 740)
                {
                    _local1 = MathHelper.Lerp(_local1, proj.scale, 0.1f);
                    _local0 = MathHelper.Lerp(_local0, 77.77778f, 0.1f);
                }
                proj.alpha = proj.timeLeft > 820
                    ? 80 + (int)(Math.Sin(proj.timeLeft * 1.8f) * 80.0)
                    : (int)MathHelper.Lerp(proj.alpha, 0f, 0.05f);
            }
            else if (proj.timeLeft >= 20)
            {
                _r1 = MathHelper.Lerp(_r1, 64.70588f, 0.1f);
                _local0 = MathHelper.Lerp(_local0, 63.636364f, 0.1f);
                proj.scale = MathHelper.Lerp(proj.scale, 6.5f, 0.05f);
                _angle += 0.005f;

                if (_p2 && proj.timeLeft < 210)
                {
                    _local1 -= 0.07f;
                    if (_local1 < 0.2f) _local1 = 0f;
                }
                else
                {
                    _local1 = MathHelper.Lerp(_local1, proj.scale, 0.1f);
                }

                proj.alpha = proj.timeLeft < 210
                    ? (int)MathHelper.Lerp(proj.alpha, 0f, 0.02f)
                    : (int)MathHelper.Lerp(proj.alpha, 120f, 0.05f);
            }
            else
            {
                proj.scale = MathHelper.Lerp(proj.scale, 15f, 0.1f);
                proj.alpha = (int)MathHelper.Lerp(proj.alpha, 255f, 0.2f);
                if (!_p2) _local1 = proj.scale;
            }

            if (proj.timeLeft > 20)
            {
                proj.rotation += 0.01f;
                _angle += 0.02f;
            }

            if (proj.timeLeft == 25 && !_p2)
            {
                Shoot(proj);
                _drawEffTimer = 10;
            }
            if (proj.timeLeft == 10 && _p2)
            {
                Shoot(proj);
                _drawEffTimer = 10;
            }

            Sync(proj);
        }

        /// <summary>源 Shoot()：光尘、震屏、Wave、RadialGlur、150 颗 LightDust，p2 再补 6 发 EoLProj。</summary>
        private void Shoot(Projectile proj)
        {
            for (var i = 0; i < 8; i++)
            {
                var r = XiheHelper.NextVector2Unit();
                var dust = XiheVisuals.Create(VfxKind.LightDustCn,
                    proj.Center + r * Main.rand.Next(20, 60), r * Main.rand.Next(3, 8));
                dust.BloomColor = Color.Yellow;
            }

            _canDamageTimer = 5;
            _drawEffTimer = 20;

            XiheFx.ScreenShake(20f, proj.Center);

            // 源：VisualEffect.Create<Wave>(...).scale *= 1.5f
            var wave = XiheVisuals.Create(VfxKind.Wave, proj.Center, Vector2.Zero);
            wave.Scale *= 1.5f;

            // 源：RadialGlur(20)，Pos = Center
            XiheFx.RadialGlur(proj.Center, 20, true);

            for (var j = 0; j < 10; j++)
            {
                for (var k = 0; k < 4; k++)
                {
                    var vel = XiheHelper.Vxr(j * (float)Math.PI * 2f / 10f
                                            + XiheHelper.NextFloatDirection() * 0.2f);
                    var pos = proj.Center + k * vel * (60 + Main.rand.Next(100))
                              + XiheHelper.NextVector2Unit() * 5f;
                    var dust = XiheVisuals.Create(VfxKind.LightDust, pos,
                        XiheHelper.NextVector2Unit() + vel * 10f);
                    dust.DrawColor = new Color(255, 255, 130);
                }
            }

            if (!_p2) return;
            if (Main.netMode == 1) return;
            for (var l = 0; l < 6; l++)
            {
                Projectile.NewProjectile(null, proj.Center,
                    Utils.ToRotationVector2(l * (float)Math.PI * 2f / 6f + _angle) * 5f,
                    ModContent.ProjectileType<EoLProj>(), proj.damage / 2, 0f, Main.myPlayer, 0f, 0f, 0f);
            }
        }

        /// <summary>把实例状态写回 proj.ai[2] / localAI，供静态 PushVisual 读取。</summary>
        private void Sync(Projectile proj)
        {
            var ai = proj.ai;
            ai[2] = _r1;
            proj.ai = ai;

            var l = proj.localAI;
            l[0] = _local0;
            l[1] = _local1;
            l[2] = _angle;
            proj.localAI = l;
        }

        public override bool? Colliding(Projectile proj, Rectangle projHitbox, Rectangle targetHitbox)
        {
            var target = Utils.TopLeft(targetHitbox) + Utils.Size(targetHitbox) / 2f;
            if (Vector2.Distance(proj.Center, target) > 3000f) return false;

            var l = proj.localAI;
            return Vector2.Distance(
                       proj.Center + Utils.ToRotationVector2(_angle) * 100f * proj.scale, target)
                   > l[1] * 28f && _canDamageTimer > 0;
        }

        public override bool PreDraw(Projectile proj, Color lightColor) => false;

        /// <summary>画面：外圈摆线（Ring + 6 个曲线节点）与内圈摆线（exPos 上的 WarpBall/Ring）。</summary>
        public static void PushVisual(Projectile p)
        {
            var ai = p.ai;
            var l = p.localAI;

            var alpha = 1f - p.alpha / 255f;
            if (alpha <= 0.01f) return;

            var scale = p.scale;
            var center = p.Center;
            var rot = p.rotation;
            var r1 = ai[2];
            var angle = l[2];
            var local0 = l[0];
            var local1 = l[1];

            var color = new Color(255, 255, 130) * alpha;
            var color2 = new Color(255, 200, 100) * alpha;

            var exPos = center + Utils.ToRotationVector2(angle) * 100f * scale;

            // 源 PreDraw：screenTargetSwap 上的黄色全屏闪光，再经 Hide.fx 在 exPos 挖一个
            // 半径 31*localAI[1]*zoom/screenHeight 的圆洞。这里用后处理模式 7 复现。
            if (DrawEff.TryGetValue(p.whoAmI, out var drawEff) && drawEff > 0)
            {
                var hole = 31f * l[1] / Math.Max(1, Main.screenHeight);
                XiheFx.CycloidOverlay(exPos, drawEff / 70f, hole);
            }

            // 源 DrawCycloid：外圆 100*scale / 110*scale，宽约 0.5*scale+2，Additive。
            XiheFx.Ring2(center, 100f * scale, 0.5f * scale + 2f, Tex.MagicPixel, color, true);
            XiheFx.Ring2(center, 110f * scale, 0.5f * scale + 2f, Tex.MagicPixel, color, true);

            // 源 Create(r1 + 0.001, 100, 0.15, 2, 70) 的摆线，逐点画成加色折线。
            // 源顶点对是 curve*scale 与 curve*(scale*1.02+0.05)，径向宽度约 (0.02*scale+0.05)*|curve|。
            if (r1 > 0.01f)
            {
                var curve = Create(r1 + 0.001f, 100f, 0.15f, 70f);
                var pts = new Vector2[curve.Length];
                for (var i = 0; i < curve.Length; i++)
                    pts[i] = center + XiheHelper.RotatedBy(curve[i], rot) * scale;
                var halfWidth = (0.02f * scale + 0.05f) * 100f * 0.5f;
                XiheFx.Line(pts, halfWidth, color, true);
            }

            // 源：localAI[1] != 0 时在 exPos 再画一层（localAI[0], localAI[1]*0.28, -1.5*rotation）。
            if (local1 > 0.01f)
            {
                var inner = local1 * 0.28f;
                var innerRot = -1.5f * rot;
                var w = 0.5f * local1 + 2f;
                XiheFx.Ring2(exPos, 100f * inner, w, Tex.MagicPixel, color2, true);
                XiheFx.Ring2(exPos, 110f * inner, w, Tex.MagicPixel, color2, true);

                if (local0 > 0.01f)
                {
                    var curve = Create(local0 + 0.001f, 100f, 0.15f, 70f);
                    var pts = new Vector2[curve.Length];
                    for (var i = 0; i < curve.Length; i++)
                        pts[i] = exPos + XiheHelper.RotatedBy(curve[i], innerRot) * inner;
                    var halfWidth = (0.02f * inner + 0.05f) * 100f * 0.5f;
                    XiheFx.Line(pts, halfWidth, color2, true);
                }
            }
        }

        /// <summary>MEAC Cycloid.Create：trochoid 参数方程（R 在源里未使用）。</summary>
        private static Vector2[] Create(float r1, float r2, float v, float max)
        {
            var list = new System.Collections.Generic.List<Vector2>();
            for (var t = 0f; t < max; t += v)
            {
                var d = r2 - r1;
                list.Add(new Vector2(
                    d * (float)Math.Cos(t) + r1 * (float)Math.Cos(d / r1 * t),
                    d * (float)Math.Sin(t) - r1 * (float)Math.Sin(d / r1 * t)));
            }
            return list.ToArray();
        }
    }
}


