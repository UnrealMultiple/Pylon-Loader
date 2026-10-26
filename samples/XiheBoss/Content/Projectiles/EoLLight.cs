using System;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria;

namespace XiheBoss
{
    public sealed class EoLLight : ModProjectile
    {
        public override string TexturePath => null;

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
            proj.timeLeft = 70;
            proj.ignoreWater = true;
            proj.tileCollide = false;
            proj.penetrate = -1;
            proj.scale = 0.1f;
            proj.alpha = 255;
        }

        // 源：位置完全由 AI 钉在宿主身上。
        public override bool ShouldUpdatePosition(Projectile proj) => false;

        public override void AI(Projectile proj)
        {
            var ai = proj.ai;

            var idx = (int)ai[0];
            if (idx < 0 || idx >= Main.npc.Length)
            {
                proj.Kill();
                return;
            }

            var npc = Main.npc[idx];
            if (npc.active) proj.Center = npc.Center;
            else
            {
                proj.Kill();
                return;
            }

            var local = proj.localAI;
            var mode = ai[1];

            if (mode == 1f || mode == -1f)
            {
                local[1] = proj.timeLeft > 10
                    ? MathHelper.Lerp(local[1], 320f, 0.1f)
                    : MathHelper.Lerp(local[1], 0f, 0.2f);
                proj.rotation += 0.1f * ai[1];
            }
            else if (mode == 2f || mode == -2f)
            {
                local[1] = proj.timeLeft > 10
                    ? MathHelper.Lerp(local[1], 200f, 0.1f)
                    : MathHelper.Lerp(local[1], 0f, 0.2f);
                proj.scale = MathHelper.Lerp(proj.scale, 1f, 0.05f);
                proj.rotation += ai[1] * 0.05f;
            }
            else if (mode == 3f || mode == -3f)
            {
                local[1] = proj.timeLeft > 10
                    ? MathHelper.Lerp(local[1], 650f, 0.1f)
                    : MathHelper.Lerp(local[1], 0f, 0.2f);
                proj.scale = MathHelper.Lerp(proj.scale, 1f, 0.05f);
                proj.rotation += ai[1] * -0.011f;
            }

            proj.localAI = local;
        }

        // 源：scale > 0.2 才造成伤害（第一圈「洗地」光棱只有 0.1，纯预警）。
        public override bool CanDamage(Projectile proj) => proj.scale > 0.2f;

        // 源：从 Center 沿 rotation 的一条长 localAI[1] 的线段，半宽 15*scale。
        public override bool? Colliding(Projectile proj, Rectangle projHitbox, Rectangle targetHitbox)
        {
            var unit = XiheHelper.Vxr(proj.rotation);
            var point = 0f;
            return Collision.CheckAABBvLineCollision(Utils.TopLeft(targetHitbox), Utils.Size(targetHitbox),
                proj.Center, proj.Center + unit * (proj.localAI[1] - 10f), 15f * proj.scale, ref point);
        }

        public override bool PreDraw(Projectile proj, Color lightColor) => false;

        /// <summary>源 PreDraw：projTex 沿 rotation 拉长，Colorize + 加色。</summary>
        public static void PushVisual(Projectile p)
        {
            var len = p.localAI[1];
            if (len <= 4f || p.scale <= 0.01f) return;

            var color = XiheHelper.HsvToRgb(p.localAI[0], 1f, 0.8f);
            // 需求：预警线（第一圈只造成 0.1 scale 的“洗地光棱”）再亮一点、宽一点。
            // 几何/混合/Colorize 仍按 MEAC，颜色提亮 + 外面补一层低透明宽辉光。
            color *= 1.8f;
            var origin = new Vector2(XiheTextures.WidthOf(Tex.ProjTex) / 2f, XiheTextures.HeightOf(Tex.ProjTex));
            var h = XiheTextures.HeightOf(Tex.ProjTex);
            var width = p.scale * 1.7f;
            XiheFx.Sprite(p.Center, Tex.ProjTex, color * 0.35f, p.rotation + 1.57f,
                new Vector2(width * 2.2f, len / h), origin, additive: true, colorize: true);
            XiheFx.Sprite(p.Center, Tex.ProjTex, color, p.rotation + 1.57f,
                new Vector2(width, len / h), origin, additive: true, colorize: true);
        }
    }
}
