using System;
using System.Collections.Generic;
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

            // 源：RadialGlur(20)，Pos = Center。注意源用的是**非加色**的 RadialGlur（不是 RadialGlur2），
            // 这里之前传了 true，整个屏幕被 9 层加色叠成纯白——就是“法阵打伤害全屏白”的原因。
            XiheFx.RadialGlur(proj.Center, 20, false);

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

        /// <summary>画面：源 DrawCycloid 的三条 TriangleStrip（摆线 + 100/110 两圈），全部 MagicPixel + Additive。</summary>
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

            // 源 PreDraw：screenTargetSwap 上的黄色全屏闪光，再经 Hide.fx 在 exPos 挖一个洞。
            if (DrawEff.TryGetValue(p.whoAmI, out var drawEff) && drawEff > 0)
            {
                var hole = 31f * l[1] / Math.Max(1, Main.screenHeight);
                XiheFx.CycloidOverlay(exPos, drawEff / 70f, hole);
            }

            // 源 DrawCycloid：三条三角形条带合进一次硬件几何提交（加色，顺序无关）。
            var verts = new List<TriVertex>(700 * 6);
            AddCycloidStrips(verts, center, r1 + 0.001f, 100f, scale, rot, color);
            if (local1 > 0.01f)
                AddCycloidStrips(verts, exPos, local0 + 0.001f, 100f, local1 * 0.28f, -1.5f * rot, color2);

            if (verts.Count >= 3)
                XiheFx.Tri(Tex.MagicPixel, false, true, false, false, verts.ToArray());
        }

        /// <summary>源 DrawCycloid：摆线曲线 + 半径 100/110 两圈，MagicPixel 顶点色。</summary>
        private static void AddCycloidStrips(List<TriVertex> verts, Vector2 pos, float r1, float r2,
            float scale, float rot, Color color)
        {
            // 1) 摆线：Create(r1 + 0.001, r2, 0.15, 70) 的每点两个顶点。
            var curve = Create(r1, r2, 0.15f, 70f);
            var inner = new Vector2[curve.Length];
            var outer = new Vector2[curve.Length];
            for (var i = 0; i < curve.Length; i++)
            {
                var c = XiheHelper.RotatedBy(curve[i], rot);
                inner[i] = pos + c * scale;
                outer[i] = pos + c * (scale * 1.02f + 0.05f);
            }
            AddStrip(verts, inner, outer, color);

            // 2) 100 圈、3) 110 圈：每圈 101 个点，外径 = 内径 * 1.005 + 2。
            AddRingStrip(verts, pos, 100f * scale, 100.5f * scale + 2f, 100, color);
            AddRingStrip(verts, pos, 110f * scale, 110.5f * scale + 2f, 100, color);
        }

        private static void AddRingStrip(List<TriVertex> verts, Vector2 pos, float radius, float outerRadius,
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
                var a0 = new TriVertex { World = inner[i], Color = color };
                var b0 = new TriVertex { World = outer[i], Color = color };
                var a1 = new TriVertex { World = inner[i + 1], Color = color };
                var b1 = new TriVertex { World = outer[i + 1], Color = color };

                verts.Add(a0); verts.Add(b0); verts.Add(a1);
                verts.Add(b0); verts.Add(b1); verts.Add(a1);
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


