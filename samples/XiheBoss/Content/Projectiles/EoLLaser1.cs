using System;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Color = Il2CppMicrosoft.Xna.Framework.Graphics.Color;
using Pylon.ModLoader;
using Terraria;
using Terraria.ID;

namespace XiheBoss
{
    public sealed class EoLLaser1 : ModProjectile
    {
        public override string TexturePath => null;

        /// <summary>MEAC 的 maxTime 字段；来自 ai[2]。</summary>
        private int _maxTime;

        /// <summary>MEAC 的 setTime（每种模式只初始化一次寿命）。</summary>
        private bool _setTime;

        public override void SetStaticDefaults()
        {
            Main.projFrames[Type] = 1;
        }

        public override void SetDefaults(Projectile proj)
        {
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

        public override void OnSpawn(Projectile proj)
        {
            var ai = proj.ai;
            _maxTime = ai[2] > 0f ? (int)ai[2] : proj.timeLeft;

            var mode = ai[1];
            if (mode >= -1f && mode <= 2f)
                proj.rotation = (float)Math.Atan2(proj.velocity.Y, proj.velocity.X);

            // 源文件 OnSpawn 的 ai[1]==4 分支：固定 145 寿命、朝向速度方向、少一次 extraUpdate。
            if (mode == 4f)
            {
                proj.timeLeft = 145;
                _maxTime = 145;
                proj.rotation = (float)Math.Atan2(proj.velocity.Y, proj.velocity.X);
                proj.extraUpdates--;
            }
        }

        private int Timer(Projectile proj) => _maxTime - proj.timeLeft;

        public override void AI(Projectile proj)
        {
            var ai = proj.ai;
            // Boss 在生成后才写 ai[2]（maxTime），所以每 tick 都以 ai[2] 为准刷新一次。
            if (ai[2] > 0f) _maxTime = (int)ai[2];
            else if (_maxTime <= 0) _maxTime = proj.timeLeft;

            // 源文件 AI 第一行：projectile.localAI[1] = 5000f（光束长度基数）。
            var local = proj.localAI;
            local[1] = 5000f;
            proj.localAI = local;

            var mode = ai[1];

            // ---------------- 模式 0 ----------------
            if (mode == 0f)
            {
                if (proj.timeLeft == 24)
                {
                    proj.rotation = (float)Math.Atan2(proj.velocity.Y, proj.velocity.X);
                    LaserDust(proj, Color.Lerp(XiheHelper.HsvToRgb(ai[0] % 360f), Color.White, 0.5f));
                }
                if (proj.timeLeft > 16) proj.scale += 0.06f;
                else if (proj.timeLeft < 12) proj.scale -= 0.05f;
            }

            // ---------------- 模式 -1 / 1（跟随宿主 NPC） ----------------
            else if (mode == 1f || mode == -1f)
            {
                if (!_setTime)
                {
                    proj.extraUpdates = 0;
                    proj.timeLeft = _maxTime;
                    _setTime = true;
                }

                var ownerIndex = (int)ai[0];
                if (ownerIndex < 0 || ownerIndex >= Main.npc.Length || !Main.npc[ownerIndex].active)
                {
                    proj.Kill();
                    return;
                }

                proj.Center = Main.npc[ownerIndex].Center;
                proj.rotation += ai[1] * 0.00525f;

                var t = Timer(proj);
                if (t >= 20 && t < 30) proj.scale -= 0.002f;
                if (t >= 45 && t < 50) proj.scale += 0.07f;
                if (t > 50) proj.scale += 0.06f * (float)Math.Sin(proj.timeLeft * 0.8f);
                if (t > 150) proj.rotation += (t - 150) * 0.00022f * ai[1];
                if (proj.timeLeft < 10) proj.scale -= 0.03f;
            }

            // ---------------- 模式 2（跟随宿主 NPC + 摆动） ----------------
            else if (mode == 2f)
            {
                if (!_setTime)
                {
                    proj.extraUpdates = 0;
                    proj.timeLeft = _maxTime;
                    _setTime = true;
                }

                var ownerIndex = (int)ai[0];
                if (ownerIndex < 0 || ownerIndex >= Main.npc.Length || !Main.npc[ownerIndex].active)
                {
                    proj.Kill();
                    return;
                }

                proj.Center = Main.npc[ownerIndex].Center;
                proj.rotation += 0.009f * (float)Math.Sin(proj.timeLeft * 0.03f);

                // 源文件这里有一个 `if (timer >= 40) { _ = 50; }`，只是空操作，不留。
                var t = Timer(proj);
                if (t >= 54 && t < 60) proj.scale += 0.07f;
                if (proj.timeLeft < 10) proj.scale -= 0.04f;
            }

            // ---------------- 模式 3（定角柱） ----------------
            else if (mode == 3f)
            {
                if (!_setTime)
                {
                    proj.extraUpdates = 0;
                    proj.timeLeft = _maxTime;
                    _setTime = true;
                    proj.rotation = (float)Math.Atan2(proj.velocity.Y, proj.velocity.X);
                    LaserDust(proj, XiheHelper.HsvToRgb(ai[0] % 360f));
                }

                var t = Timer(proj);
                if (t < 8) proj.scale += 0.06f;
                else if (proj.timeLeft < 12) proj.scale -= 0.05f;
            }

            // ---------------- 模式 4（大激光的四根角柱） ----------------
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

        /// <summary>源文件的 LightDust 起手尘（30 颗，沿速度方向铺开）。</summary>
        private static void LaserDust(Projectile proj, Color color)
        {
            var dir = XiheHelper.Normalize2(proj.velocity);
            for (var i = 0; i < 10; i++)
            {
                var pos = proj.Center
                          + dir * i * (80 + Main.rand.Next(100))
                          + XiheHelper.NextVector2Unit() * 5f;
                var vfx = XiheVisuals.Create(VfxKind.LightDust, pos,
                    XiheHelper.NextVector2Unit() * 1f + proj.velocity * 10f);
                vfx.DrawColor = color;
            }
        }

        public override bool ShouldUpdatePosition(Projectile proj) => false;

        public override bool CanDamage(Projectile proj) => proj.scale > 0.2f;

        public override bool? Colliding(Projectile proj, Rectangle projHitbox, Rectangle targetHitbox)
        {
            // 源文件：Center → Center + Vxr(rotation) * localAI[1]，宽 18*scale 的线段。
            var unit = XiheHelper.Vxr(proj.rotation);
            float point = 0f;
            return Collision.CheckAABBvLineCollision(
                Utils.TopLeft(targetHitbox), Utils.Size(targetHitbox),
                proj.Center, proj.Center + unit * proj.localAI[1],
                18f * proj.scale, ref point);
        }

        public override bool PreDraw(Projectile proj, Color lightColor) => false;

        /// <summary>源 PreDraw：tex4 的 5 倍宽 wrap 长条 + Laser0 两端 + Extra[89] 四层十字，全部 Colorize+Additive。</summary>
        public static void PushVisual(Projectile p)
        {
            if (p.scale <= 0.001f) return;

            // 源 PreDraw 固定用 HsvToRgb(45,1,0.5)，不读 ai[0]。
            var color = XiheHelper.HsvToRgb(45f, 1f, 0.5f);
            var length = p.localAI[1];
            if (length <= 1f) return;
            var dir = XiheHelper.Vxr(p.rotation);
            var s = p.scale;

            // tex4：源 Rectangle(timeLeft*8, 0, 256*5, 256) 是 PointWrap 滚动；
            // 这里把源矩形 x 折成 u 相位（WrapShift），长度/颜色/两端与源文件一致。
            XiheFx.SpriteRect(p.Center, Tex.Tex4, new Rectangle(0, 0, 256 * 5, 256),
                color, p.rotation, new Vector2(length / 256f / 5f, s), new Vector2(0f, 128f),
                additive: true, colorize: true, wrapU: true, srcRepeat: 5f,
                wrapShift: p.timeLeft * 8f / 256f);

            var end = p.Center + dir * length;

            // Laser0：两次，rotation=0，scale=scale*2，origin=中心。
            var originLaser = new Vector2(72f, 72f) / 2f;
            XiheFx.Sprite(p.Center, Tex.Laser0, color, 0f, Vector2.One * (s * 2f), originLaser,
                additive: true, colorize: true);
            XiheFx.Sprite(end, Tex.Laser0, color, 0f, Vector2.One * (s * 2f), originLaser,
                additive: true, colorize: true);

            // Extra[89]：4 次，scale/rotation 照抄。
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



