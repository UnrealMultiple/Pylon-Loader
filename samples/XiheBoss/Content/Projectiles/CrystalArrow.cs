// 水晶箭（源：MEAC.Bosses.EoL.Projectiles.CrystalArrow 1:1 移植）。
//
// AI 逐行照抄：alpha 的淡入/淡出、ai[0] = -1/0/1/2/3/4 六种模式、30 段 oldPos/oldRot 拖尾维护、
// 每 5 tick 一颗尘土、timeLeft == 1 的粒子爆。数值、顺序、分支都与源文件一致。
//
// ai 约定（PORTING.md）：ai[0] = 源 ai[0]（模式），ai[1] = 源 ai[1]（色相）。
//
// 视觉近似点：
// - 源 PreDraw 用一张 projTex 画箭体、用 Ball 拉伸成 30 段拖尾，最后再画两笔十字辉光；
//   这里全部交给 XiheFx（加色着色器）：箭体 = KnifeExtra1 光束 + Laser0 头部光球，
//   拖尾 = 每段一个 Ball 光球（radius = 15*0.2*scale，yScale = 2.5/0.2 = 12.5，等价原版 (0.2, 2.5) 拉伸）。
// - 源 PreDraw 遍历全部 30 段拖尾；为控制屏幕图元数量这里只画最新 8 段（AI 仍完整维护 30 段缓存）。
// - 源用 ParticleOrchestrator（原版粒子系统）；这里用 Dust.NewDustPerfect(267) 的等价小爆散代替。
using System;
using Il2CppMicrosoft.Xna.Framework;
using Color = Il2CppMicrosoft.Xna.Framework.Graphics.Color;
using Pylon.ModLoader;
using Terraria;
using Terraria.ID;

namespace XiheBoss
{
    public sealed class CrystalArrow : ModProjectile
    {
        /// <summary>源 SetStaticDefaults: Sets.TrailCacheLength[type] = 30。手机版代理数组没有 Length，固定用 30。</summary>
        private const int TrailCache = 8;

        /// <summary>实际提交给屏幕效果的拖尾段数（源画 30 段，这里按移植约定只画 8 段）。</summary>
        private const int TrailDraw = 8;

        // 手机版 Projectile.oldPos/oldRot 是 IL2CPP 定长结构体，整体回写会丢；用按 whoAmI
        // 索引的静态表保存，和 EoLProj 的做法一致。PushVisual 是 static，正好读这张表。
        private static Vector2[][] _oldPos;
        private static float[][] _oldRot;

        public override string TexturePath => null;

        public override void SetStaticDefaults()
        {
            Main.projFrames[Type] = 1;
        }

        public override void SetDefaults(Projectile proj)
        {
            proj.width = 10;
            proj.height = 10;
            proj.aiStyle = -1;
            proj.timeLeft = 180;
            proj.scale = 1.2f;
            proj.hostile = true;
            proj.friendly = false;
            proj.tileCollide = false;
            proj.penetrate = -1;
            proj.ignoreWater = true;
            proj.alpha = 255;
        }

        public override void OnSpawn(Projectile proj) => Alloc(proj);

        public override void OnKill(Projectile proj, int timeLeft) => Clear(proj);

        private static Vector2[] TrailOf(Projectile proj)
        {
            var count = Main.projectile.Length;
            if (_oldPos == null || _oldPos.Length != count) return null;
            var i = proj.whoAmI;
            return i >= 0 && i < count ? _oldPos[i] : null;
        }

        private static float[] RotOf(Projectile proj)
        {
            var count = Main.projectile.Length;
            if (_oldRot == null || _oldRot.Length != count) return null;
            var i = proj.whoAmI;
            return i >= 0 && i < count ? _oldRot[i] : null;
        }

        private static void Alloc(Projectile proj)
        {
            var count = Main.projectile.Length;
            if (_oldPos == null || _oldPos.Length != count) _oldPos = new Vector2[count][];
            if (_oldRot == null || _oldRot.Length != count) _oldRot = new float[count][];
            var i = proj.whoAmI;
            if (i < 0 || i >= count) return;
            _oldPos[i] = new Vector2[TrailCache];
            _oldRot[i] = new float[TrailCache];
        }

        private static void Clear(Projectile proj)
        {
            var count = Main.projectile.Length;
            var i = proj.whoAmI;
            if (i < 0 || i >= count) return;
            if (_oldPos != null && _oldPos.Length == count) _oldPos[i] = null;
            if (_oldRot != null && _oldRot.Length == count) _oldRot[i] = null;
        }

        public override void AI(Projectile proj)
        {
            var ai = proj.ai;

            // 源：alpha = timeLeft > 10 ? Lerp(alpha, 0, 0.1) : Lerp(alpha, 255, 0.1)
            proj.alpha = proj.timeLeft > 10
                ? (int)MathHelper.Lerp(proj.alpha, 0f, 0.1f)
                : (int)MathHelper.Lerp(proj.alpha, 255f, 0.1f);

            if (ai[0] == -1f)
            {
                // 源：velocity = Lerp(velocity, DirectionTo2(Main.MouseWorld) * 50, 0.1) + 粒子
                proj.velocity = Vector2.Lerp(proj.velocity,
                    XiheHelper.DirectionTo(proj.Center, Main.MouseWorld) * 50f, 0.1f);
                SparkBurst(proj.Center, ai[1]);
            }

            if (ai[0] == 0f)
            {
                if (proj.timeLeft > 160) proj.velocity *= 0.95f;
                if (proj.timeLeft == 145) proj.velocity = Vector2.Normalize(proj.velocity) * 3f;
                if (proj.timeLeft < 145) proj.velocity *= 1.035f;
            }
            else if (ai[0] == 1f)
            {
                var player = Main.player[Player.FindClosest(proj.Center, 1000, 1000)];
                if (proj.timeLeft > 150)
                {
                    proj.velocity = XiheHelper.RotatedBy(proj.velocity, XiheHelper.NextFloatDirection() * 0.1f);
                    proj.velocity *= 0.95f;
                }
                if (proj.timeLeft < 140 && proj.timeLeft > 90)
                {
                    proj.velocity = Vector2.Lerp(proj.velocity,
                        XiheHelper.DirectionTo(proj.Center, player.Center) * 30f, 0.04f);
                }
            }
            else if (ai[0] == 2f)
            {
                var player = Main.player[Player.FindClosest(proj.Center, 1000, 1000)];
                if (proj.timeLeft > 140)
                {
                    proj.velocity = XiheHelper.RotatedBy(proj.velocity, XiheHelper.NextFloatDirection() * 0.1f);
                    proj.velocity *= 0.98f;
                }
                if (proj.timeLeft < 140 && proj.timeLeft > 110)
                {
                    proj.velocity = Vector2.Lerp(proj.velocity,
                        XiheHelper.DirectionTo(proj.Center, player.Center) * 30f, 0.03f);
                }
            }
            else if (ai[0] == 3f)
            {
                if (proj.velocity.Y < 6f) proj.velocity = new Vector2(proj.velocity.X, proj.velocity.Y + 0.1f);
            }
            else if (ai[0] == 4f && proj.velocity.Y < 10f)
            {
                proj.velocity = new Vector2(proj.velocity.X, proj.velocity.Y + 0.1f);
            }

            proj.rotation = Utils.ToRotation(proj.velocity) + 1.57f;

            // 源：整段 oldPos/oldRot 前移一格，再把当前 Center/rotation 放进 [0]。
            var oldPos = TrailOf(proj);
            var oldRot = RotOf(proj);
            if (oldPos != null && oldRot != null)
            {
                for (var i = TrailCache - 1; i > 0; i--)
                {
                    oldPos[i] = oldPos[i - 1];
                    oldRot[i] = oldRot[i - 1];
                }
                oldPos[0] = proj.Center;
                oldRot[0] = proj.rotation;
            }

            if (proj.timeLeft % 5 == 0)
            {
                var d = Dust.NewDustDirect(proj.Center, 1, 1, 267, 0f, 0f, 0,
                    XiheHelper.HsvToRgb(ai[1]), 1f);
                d.noGravity = true;
            }

            if (proj.timeLeft == 1) SparkBurst(proj.Center, ai[1]);
        }

        /// <summary>源 ParticleOrchestraType 5 的近似：一圈不带重力的小尘土。</summary>
        private static void SparkBurst(Vector2 center, float hue)
        {
            var color = XiheHelper.HsvToRgb(hue);
            for (var i = 0; i < 8; i++)
            {
                var d = Dust.NewDustDirect(center + XiheHelper.NextVector2Unit() * 4f, 1, 1, 267, 0f, 0f, 0, color, 1f);
                d.noGravity = true;
                d.velocity = XiheHelper.NextVector2Unit() * 5f;
            }
        }

        public override bool ShouldUpdatePosition(Projectile proj) => true;

        public override bool PreDraw(Projectile proj, Color lightColor) => false;

        /// <summary>源 PreDraw：30 段 Ball 拖尾 + projTex 箭体 + 两笔 Ball 十字，全部 Colorize+Additive。</summary>
        public static void PushVisual(Projectile p)
        {
            var ai = p.ai;
            var color = XiheHelper.HsvToRgb(ai[1]) * (1f - p.alpha / 255f);
            if (color.A == 0) return;

            var scale = p.scale;
            var oldPos = TrailOf(p);
            var oldRot = RotOf(p);
            if (oldPos == null || oldRot == null) return;
            var texW = XiheTextures.WidthOf(Tex.ProjTex);
            var texH = XiheTextures.HeightOf(Tex.ProjTex);
            var texOrigin = new Vector2(texW, texH) / 2f;
            var ballOrigin = new Vector2(XiheTextures.WidthOf(Tex.Ball), XiheTextures.HeightOf(Tex.Ball)) / 2f;
            var length = (float)TrailCache;
            // 同屏精灵过多时（例如开场 CrystalArrow 雨），只画最近 8 段拖尾；
            // 正常战斗仍按 MEAC 的 30 段。
            var drawCount = XiheFx.SpriteCount > 200 ? 8 : TrailCache;

            // 源：for (i < oldPos.Length) Draw(Ball, oldPos[i], color*0.7*((length-i)/length),
            //     oldRot[i], Utils.Size(tex)/2, projectile.scale * (0.2, 2.5))，Colorize+Additive。
            for (var i = 0; i < drawCount; i++)
            {
                var pos = oldPos[i];
                if (i > 0 && pos == Vector2.Zero) break;
                XiheFx.Sprite(pos, Tex.Ball, color * (0.7f * (length - i) / length), oldRot[i],
                    scale * new Vector2(0.2f, 2.5f), texOrigin, additive: true, colorize: true);
            }

            // 箭体
            XiheFx.Sprite(p.Center, Tex.ProjTex, color, p.rotation, Vector2.One * scale,
                texOrigin, additive: true, colorize: true);

            // 十字
            var pulse = (1f + 0.2f * (float)Math.Sin(Main.timeForVisualEffects * 0.6f)) * 2f;
            XiheFx.Sprite(p.Center, Tex.Ball, color * 0.9f, 0f,
                new Vector2(3f, 0.1f) * pulse, ballOrigin, additive: true, colorize: true);
            XiheFx.Sprite(p.Center, Tex.Ball, color * 0.9f, 0f,
                new Vector2(0.1f, 1.8f) * pulse, ballOrigin, additive: true, colorize: true);
        }
    }
}







