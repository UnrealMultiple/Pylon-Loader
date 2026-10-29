using System;
using Il2CppMicrosoft.Xna.Framework;
using Color = Il2CppMicrosoft.Xna.Framework.Graphics.Color;
using Pylon.ModLoader;
using Terraria;
using Terraria.ID;

namespace XiheBoss
{
    public sealed class EoLLance : ModProjectile
    {
        public override string TexturePath => null;

        /// <summary>出生时的总寿命（MEAC 的 maxtime；ai[2] 可覆盖）。</summary>
        private int _maxTime = 600;

        public override void SetStaticDefaults()
        {
            Main.projFrames[Type] = 1;
        }

        public override void SetDefaults(Projectile proj)
        {
            proj.width = 100;
            proj.height = 1;
            proj.aiStyle = -1;
            proj.timeLeft = 600;
            proj.scale = 1f;
            proj.hostile = false;
            proj.friendly = false;
            proj.tileCollide = false;
            proj.ignoreWater = false;
            proj.alpha = 0;
            proj.penetrate = -1;
            proj.hide = true;
        }

        public override void OnSpawn(Projectile proj)
        {
            var ai = proj.ai;
            _maxTime = ai[2] > 1f ? (int)ai[2] : 600;
        }

        private int Timer(Projectile proj) => _maxTime - proj.timeLeft;

        public override void AI(Projectile proj)
        {
            var t = Timer(proj);
            var center = proj.Center;

            var color = XiheHelper.HsvToRgb(proj.ai[0] % 360f, 0.5f);
            Lighting.AddLight(center, color.R / 255f * 0.5f, color.G / 255f * 0.5f, color.B / 255f * 0.5f);

            var local = proj.localAI;
            if (local[0] < 1f)
            {
                local[0] += 0.1f;
                proj.localAI = local;
            }

            var ai = proj.ai;
            var mode = ai[1];

            if (mode == 0f)
            {
                if (t == 0 && proj.velocity.Length() != 1f) proj.velocity = Vector2.Normalize(proj.velocity);
                if (t < 60) proj.position -= proj.velocity;
                if (t < 15) proj.alpha += 15;
                if (t > 45 && t < 60)
                {
                    proj.alpha -= 15;
                    proj.position -= proj.velocity * MathHelper.Lerp(10f, 0f, (t - 45f) / 15f);
                }
                if (t == 60)
                {
                    proj.hostile = true;
                    proj.alpha = 0;
                    proj.velocity = Vector2.Normalize(proj.velocity) * 40f;
                    proj.extraUpdates++;
                }
            }
            else if (mode == 1f)
            {
                if (t == 0 && proj.velocity.Length() != 1f) proj.velocity = Vector2.Normalize(proj.velocity);
                if (t < 45) proj.position -= proj.velocity;
                if (t < 15) proj.alpha += 15;
                if (t > 15 && t < 30) proj.alpha -= 15;
                if (t > 30 && t < 45) proj.position -= proj.velocity * MathHelper.Lerp(10f, 0f, (t - 30f) / 15f);
                if (t == 45)
                {
                    proj.hostile = true;
                    proj.alpha = 0;
                    proj.Kill();
                    NewLaser(proj, proj.ai[0]);
                }
            }
            else if (mode == 2f)
            {
                if (t == 0 && proj.velocity.Length() != 1f) proj.velocity = Vector2.Normalize(proj.velocity);
                if (t < 60) proj.position -= proj.velocity;
                if (t < 15) proj.alpha += 15;
                if (t > 15 && t < 30) proj.alpha -= 15;
                if (t > 45 && t < 60) proj.position -= proj.velocity * MathHelper.Lerp(10f, 0f, (t - 30f) / 15f);
                if (t == 60)
                {
                    proj.hostile = true;
                    proj.alpha = 0;
                    proj.Kill();
                    NewLaser(proj, proj.ai[0]);
                }
            }
            else if (mode == 3f || mode == -3f)
            {
                var ownerIndex = (int)proj.localAI[1];
                if (ownerIndex < 0 || ownerIndex >= Main.npc.Length || !Main.npc[ownerIndex].active)
                {
                    proj.Kill();
                    return;
                }
                var owner = Main.npc[ownerIndex];
                proj.Center = owner.Center + proj.velocity;
                proj.position -= proj.velocity;

                if (t < 20) proj.alpha += 13;
                if (t > 30 && t < 60) proj.alpha -= 8;
                if (t < 40)
                {
                    var a = (40 - t) / 40f;
                    proj.velocity = XiheHelper.RotatedBy(proj.velocity, a * proj.ai[1] * 0.022f);
                }
                if (t < 65) proj.velocity *= 0.992f;
                if (t == 70)
                {
                    proj.Kill();
                    NewLaser(proj, proj.ai[0]);
                }
            }
            else if (mode == 4f && proj.timeLeft == 600)
            {
                proj.timeLeft = 200;
                _maxTime = 200;
                ai[1] = 0f;
                proj.ai = ai;
            }
            else if (mode == 5f)
            {
                if (t == 0 && proj.velocity.Length() != 1f) proj.velocity = Vector2.Normalize(proj.velocity);
                if (t < 60) proj.position -= proj.velocity;
                if (t < 15) proj.alpha += 15;
                if (t > 15 && t < 30) proj.alpha -= 15;
                if (t == 60)
                {
                    proj.hostile = true;
                    proj.alpha = 0;
                    proj.Kill();
                    var p = NewProjectile(proj, ModContent.ProjectileType<EoLLaser1>(), 50f, 3f);
                    if (p != null)
                    {
                        var lai = p.ai;
                        lai[2] = 50f;
                        p.ai = lai;
                    }
                }
            }
            else if (mode == 6f)
            {
                if (proj.timeLeft > 315)
                {
                    local = proj.localAI;
                    local[0] -= 0.1f;
                    proj.localAI = local;
                }
                if (proj.timeLeft < 315 && proj.timeLeft > 300) proj.alpha += 15;
                if (proj.timeLeft < 285 && proj.timeLeft > 270) proj.alpha -= 15;
                if (t < proj.ai[0] - 15f) proj.position -= proj.velocity;
                if (t > proj.ai[0] - 15f && t < proj.ai[0])
                    proj.position -= proj.velocity * MathHelper.Lerp(10f, 0f, (t - proj.ai[0] + 15f) / 15f);
                if (t == proj.ai[0])
                {
                    proj.hostile = true;
                    proj.alpha = 0;
                    proj.velocity = Vector2.Normalize(proj.velocity) * 40f;
                    proj.extraUpdates++;
                }
            }
        }

        private static void NewLaser(Projectile proj, float hue)
        {
            Projectile.NewProjectile(null, proj.Center, proj.velocity, ModContent.ProjectileType<EoLLaser1>(),
                proj.damage, 0f, Main.myPlayer, hue, 0f, 0f);
        }

        private static Projectile NewProjectile(Projectile proj, int type, float ai0, float ai1)
        {
            var idx = Projectile.NewProjectile(null, proj.Center, proj.velocity, type, proj.damage, 0f,
                Main.myPlayer, ai0, ai1, 0f);
            return idx >= 0 && idx < Main.projectile.Length ? Main.projectile[idx] : null;
        }

        public override bool ShouldUpdatePosition(Projectile proj) => true;

        public override bool? Colliding(Projectile proj, Rectangle projHitbox, Rectangle targetHitbox)
        {
            float point = 0f;
            var vec = XiheHelper.Normalize2(proj.velocity) * 120f / 2f;
            return Collision.CheckAABBvLineCollision(Utils.TopLeft(targetHitbox), Utils.Size(targetHitbox),
                proj.Center - vec, proj.Center + vec, 20f, ref point);
        }

        public override bool PreDraw(Projectile proj, Color lightColor) => false;

        /// <summary>源 PreDraw：2500px 的顶点光矛 + 8 层残影 + 枪身，全部加色。</summary>
        public static void PushVisual(Projectile p)
        {
            var ai = p.ai;
            // 源 PreDraw 用的是 MEAC.Colorize（把贴图亮度映射到 HSV 色），不是普通 Color 乘法；
            // 这里让 Sprite 走 colorize 通道，颜色才和 MEAC 一样亮。
            var color = XiheHelper.HsvToRgb(ai[0] % 360f);
            var alpha = p.localAI[0] * (1f - p.alpha / 255f);
            if (alpha <= 0.01f) return;

            var rot = (float)Math.Atan2(p.velocity.Y, p.velocity.X);
            var tex = Tex.Proj919;
            var origin = new Vector2(XiheTextures.WidthOf(tex), XiheTextures.HeightOf(tex)) / 2f;
            var scale = new Vector2(1f, p.hostile ? 1.2f : 0.5f) * p.scale;

            // 源第一段：一条 3 顶点光矛，纹理 MagicPixel，顶点色 alpha/200、alpha/205，Additive。
            var l = XiheHelper.Normalize2(p.velocity);
            var pos = p.Center;
            var color2 = XiheHelper.HsvToRgb(ai[0] % 360f);
            var a200 = color2 * (p.alpha / 200f);
            var a205 = color2 * (p.alpha / 205f);
            a200.A = (byte)MathHelper.Clamp(a200.A, 0f, 255f);
            a205.A = (byte)MathHelper.Clamp(a205.A, 0f, 255f);
            var verts = new[]
            {
                new TriVertex { World = pos + XiheHelper.VerticalVec(l) * 2.5f, Color = a200 },
                new TriVertex { World = pos - XiheHelper.VerticalVec(l) * 2.5f, Color = a205 },
                new TriVertex { World = pos + l * 2500f + XiheHelper.VerticalVec(l), Color = new Color(0, 0, 0, 0) },
            };
            XiheFx.Tri(Tex.MagicPixel, false, true, false, false, verts);

            if (p.velocity.Length() > 20f && ai[1] != 3f && ai[1] != -3f)
            {
                // 源是 8 层残影（i=0..7，亮度 0.6 - i/15）。之前为了省图元只画 4 层（i*2），
                // 长枪看起来比 MEAC 细一圈——这里按源恢复 8 层。
                for (var i = 0; i < 8; i++)
                {
                    XiheFx.Sprite(p.Center - p.velocity * 0.8f * i, tex,
                        color * (p.localAI[0] * (0.6f - i / 15f)), rot, scale, origin,
                        additive: true, colorize: true);
                }
            }

            XiheFx.Sprite(p.Center, tex, color * p.localAI[0], rot,
                new Vector2(1f, p.hostile ? 1f : 0.5f) * p.scale, origin, additive: true, colorize: true);
        }
    }
}




