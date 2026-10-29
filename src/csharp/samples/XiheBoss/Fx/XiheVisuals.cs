// MEAC 的 VisualEffect 组（Boss 专用）：EOLEff / Wave / RingWarp / RingWarp2 / VLight / Circle /
// CircleWarp / LightDust / LightDust_CN / LineWarp。AI 逐条照抄 MEAC 的 SetDefault + AI，绘制改写成
// XiheFx 图元（EOLEff 的残留剪影例外：它画的是原版贴图，交给 Boss 的 PreDraw 用 SpriteBatch 画）。
using System;
using System.Collections.Generic;
using Il2CppMicrosoft.Xna.Framework;
using Color = Il2CppMicrosoft.Xna.Framework.Graphics.Color;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;

namespace XiheBoss
{
    internal enum VfxKind
    {
        EolEff,
        Wave,
        RingWarp,
        RingWarp2,
        VLight,
        Circle,
        CircleWarp,
        LightDust,
        LightDustCn,
        LineWarp,
        Explosion,
        MagicRingSun,
    }

    internal sealed class Vfx
    {
        public VfxKind Kind;
        public Vector2 Center;
        public Vector2 Velocity;
        public float Rotation;
        public int TimeLeft;
        public int MaxTime;
        public float Ai0;
        public float Ai1;
        public float Alpha = 1f;
        public float Scale = 1f;
        public Color DrawColor = Color.White;
        public Color BloomColor = Color.White;
        public bool Golden;
        public int OwnerNpc = -1;
        public int OwnerProjectile = -1;

        // EOLEff / LightDust 私有量
        public float Dis;
        public float VAngle;
        public Vector2 Acc;
        public float Random;
        public float Rand2;

        public void SetTimeLeft(int t)
        {
            MaxTime = t;
            TimeLeft = t;
        }
    }

    internal static class XiheVisuals
    {
        private static readonly List<Vfx> List = new();

        // 手机端性能：LightDust 是最多的粒子（每个 5 层精灵）。超过这个数量就不再生成新的
        // 小尘，保证密集弹幕时不把屏幕图元顶到 1000+。主效果（Wave/Ring/EOLEff/太阳环）不受限。
        private const int MaxLightParticles = 128;

        internal static IReadOnlyList<Vfx> All => List;

        internal static void Clear() => List.Clear();

        internal static Vfx Create(VfxKind kind, Vector2 center, Vector2 velocity, float rotation = 0f,
            float scale = 1f, int ownerNpc = -1)
        {
            var v = new Vfx { Kind = kind, Center = center, Velocity = velocity, Rotation = rotation, OwnerNpc = ownerNpc };
            var light = kind == VfxKind.LightDust || kind == VfxKind.LightDustCn;
            if (light && List.Count >= MaxLightParticles)
            {
                v.TimeLeft = 1;   // 未入列表的临时对象，调用方改字段不会影响画面
                return v;
            }
            switch (kind)
            {
                case VfxKind.EolEff:
                    v.MaxTime = 60;
                    v.Scale = 1f;
                    v.Ai1 = Main.rand.NextFloat() * 6.28f;
                    break;
                case VfxKind.Wave:
                    v.MaxTime = 20;
                    v.Scale *= 0.5f;
                    v.Ai0 = 1f;
                    break;
                case VfxKind.RingWarp:
                    v.MaxTime = 30;
                    v.Ai0 = 1f;
                    break;
                case VfxKind.RingWarp2:
                    v.MaxTime = 100;
                    v.Alpha = 0f;
                    break;
                case VfxKind.VLight:
                    v.MaxTime = 15;
                    break;
                case VfxKind.Circle:
                    v.MaxTime = 60;
                    v.Ai1 = 800f;
                    v.Alpha = 0f;
                    break;
                case VfxKind.CircleWarp:
                    v.MaxTime = 20;
                    v.Ai0 = 1f;
                    break;
                case VfxKind.LightDust:
                    v.MaxTime = 30;
                    v.Ai0 = 0.3f;
                    break;
                case VfxKind.LightDustCn:
                    v.MaxTime = 30;
                    v.Scale *= 0.75f;
                    break;
                case VfxKind.LineWarp:
                    v.MaxTime = 20;
                    break;
                case VfxKind.Explosion:
                    v.MaxTime = 21;
                    break;
                case VfxKind.MagicRingSun:
                    v.MaxTime = 300;
                    v.Scale = 0f;
                    v.Alpha = 0.8f;
                    v.Random = Main.rand.NextFloat() * 50f;
                    v.Rand2 = Main.rand.NextFloat() * 50f;
                    break;
            }
            v.TimeLeft = v.MaxTime;
            if (scale != 1f) v.Scale = scale;
            List.Add(v);
            return v;
        }

        internal static void Tick()
        {
            for (var i = List.Count - 1; i >= 0; i--)
            {
                var v = List[i];
                try { Step(v); }
                catch (Exception e) { XiheBossMod.Log?.Warn("[vfx] " + v.Kind + " 异常：" + e.Message); }

                // VisualEffect.Update：先 AI、再 timeLeft--、再 Center += Velocity
                v.TimeLeft--;
                v.Center += v.Velocity;
                if (v.TimeLeft <= 0) List.RemoveAt(i);
            }
        }

        private static void Step(Vfx v)
        {
            switch (v.Kind)
            {
                case VfxKind.EolEff:
                {
                    if (v.Ai0 == 0f)
                    {
                        if (v.TimeLeft < v.MaxTime / 2) v.Dis -= 12f;
                        else v.Dis += 12f;
                        v.VAngle += 6.28f / v.MaxTime;
                        if (v.OwnerNpc >= 0 && v.OwnerNpc < Main.npc.Length && Main.npc[v.OwnerNpc].active)
                        {
                            var npc = Main.npc[v.OwnerNpc];
                            var k = 2000f / (2000f - 250f * (float)Math.Sin(v.VAngle + v.Ai1));
                            v.Center = npc.Center + XiheHelper.Vxr(v.VAngle + v.Ai1) * v.Dis * k * 0.5f;
                        }
                    }
                    if (v.Ai0 == 1f)
                    {
                        if (v.TimeLeft == v.MaxTime) v.Dis = 250f;
                        v.TimeLeft -= 4;
                        v.Dis -= 18f;
                    }
                    break;
                }
                case VfxKind.Wave:
                {
                    v.BloomColor = new Color(0.8f, 0.5f, 0.2f) * v.Alpha;
                    v.Ai1 += 0.5f;
                    if (v.TimeLeft < 6) v.Alpha -= 0.166f;
                    break;
                }
                case VfxKind.RingWarp:
                {
                    var t = v.MaxTime / 2f;
                    if (v.TimeLeft < t && v.Alpha > 0f) v.Alpha -= 1f / t;
                    v.Scale += v.Ai0;
                    break;
                }
                case VfxKind.RingWarp2:
                {
                    v.Scale = MathHelper.Lerp(0f, 30f, (float)Math.Pow((float)v.TimeLeft / 100f, 3.0));
                    if (v.TimeLeft > v.MaxTime - 10) v.Alpha += 0.1f;
                    if (v.TimeLeft < 10) v.Alpha -= 0.1f;
                    break;
                }
                case VfxKind.VLight:
                {
                    v.DrawColor = Color.Lerp(v.BloomColor, Color.White, 0.75f);
                    if (v.OwnerNpc >= 0 && v.OwnerNpc < Main.npc.Length && Main.npc[v.OwnerNpc].active)
                        v.Center = Main.npc[v.OwnerNpc].Center;
                    if (v.TimeLeft < v.MaxTime / 2) v.Ai1 -= 1f;
                    else v.Ai1 += 1f;
                    break;
                }
                case VfxKind.Circle:
                {
                    if (v.Ai0 == 0f)
                    {
                        XiheHelper.LerpValue(ref v.Ai1, 0f, 0.07f);
                        if (v.TimeLeft > 10) XiheHelper.LerpValue(ref v.Alpha, 1f, 0.05f);
                        else XiheHelper.LerpValue(ref v.Alpha, 0f, 0.3f);
                    }
                    break;
                }
                case VfxKind.CircleWarp:
                {
                    if (v.TimeLeft > 10)
                    {
                        v.Scale += v.Ai0;
                        v.Alpha = 1f;
                    }
                    if (v.TimeLeft < 10) v.Alpha -= 0.1f;
                    break;
                }
                case VfxKind.LightDust:
                {
                    v.BloomColor = v.DrawColor;
                    var t = v.MaxTime * 5f / 6f;
                    if (v.TimeLeft < t)
                    {
                        v.Ai0 -= 0.3f / t;
                        v.Velocity *= 0.94f;
                    }
                    break;
                }
                case VfxKind.LightDustCn:
                {
                    v.BloomColor = v.DrawColor = new Color(255, 255, 125);
                    if (v.TimeLeft < 10)
                    {
                        v.Alpha -= 0.1f;
                        v.Velocity *= 0.94f;
                    }
                    break;
                }
                case VfxKind.LineWarp:
                {
                    if (v.TimeLeft > 10)
                    {
                        v.Ai0 += 2f;
                        v.Rotation = (float)Math.Atan2(v.Velocity.Y, v.Velocity.X);
                        v.Alpha = 1f;
                    }
                    if (v.TimeLeft < 10)
                    {
                        v.Alpha -= 0.1f;
                        v.Center -= v.Velocity;
                    }
                    break;
                }
                case VfxKind.Explosion:
                {
                    if (v.TimeLeft % 3 == 0) v.Ai1 += 1f;
                    v.BloomColor = v.DrawColor;
                    v.Velocity += v.Acc;
                    v.Acc += XiheHelper.NextVector2Unit() * XiheHelper.NextFloatDirection() * 0.1f;
                    if (v.Velocity.Length() > 2f) v.Velocity = XiheHelper.Normalize2(v.Velocity) * 2f;
                    break;
                }
                case VfxKind.MagicRingSun:
                {
                    if (v.OwnerProjectile < 0 || v.OwnerProjectile >= Main.projectile.Length
                        || !Main.projectile[v.OwnerProjectile].active)
                    {
                        v.TimeLeft = 0;
                        break;
                    }
                    var p = Main.projectile[v.OwnerProjectile];
                    v.Center = p.Center;
                    v.Scale = p.scale;
                    v.TimeLeft = p.timeLeft;
                    v.Alpha = p.ai[0];
                    v.Ai1 += 0.8f;
                    break;
                }
            }
        }

        // ================= 提交图元 =================

        internal static void PushAll()
        {
            foreach (var v in List)
            {
                try { Push(v); }
                catch (Exception e) { XiheBossMod.Log?.Warn("[vfx] " + v.Kind + " 绘制异常：" + e.Message); }
            }
        }

        private static Vector2 Origin(Tex tex) =>
            new Vector2(XiheTextures.WidthOf(tex), XiheTextures.HeightOf(tex)) / 2f;

        private static void Push(Vfx v)
        {
            switch (v.Kind)
            {
                case VfxKind.EolEff:
                    // 交给 Boss.PreDraw 画原版贴图剪影（见 DrawEolSprites）
                    break;

                case VfxKind.Wave:
                {
                    // 源：Perlin + GameShaders.Misc["ForceField"]，颜色 = drawColor(White)*alpha，
                    // scale*ai1*(1,0.7)，Additive。ForceField 的轻微色移没有等价入口，这里用原 Perlin
                    // 贴图 + 同一组位置/缩放/颜色（不再用环+球近似）。
                    var a = MathHelper.Clamp(v.Alpha, 0f, 1f);
                    var sc = 1200f / XiheTextures.WidthOf(Tex.Perlin) * v.Scale * v.Ai1;
                    XiheFx.Sprite(v.Center, Tex.Perlin, Color.White * a, 0f,
                        new Vector2(1f, 0.7f) * sc, Origin(Tex.Perlin), additive: true);
                    break;
                }
                case VfxKind.RingWarp:
                case VfxKind.RingWarp2:
                {
                    // 源：isWarp=true，画进 screenTargetSwap 当 KScreen0 的扭曲遮罩；
                    // 不是可见的环。半径按 WarpRing 128px × scale 折算。
                    var rad = 64f * Math.Abs(v.Scale) / Math.Max(1, Main.screenHeight);
                    XiheFx.Warp(v.Center, rad, MathHelper.Max(rad * 0.6f, 0.02f),
                        0.02f * MathHelper.Clamp(v.Alpha, 0f, 1f));
                    break;
                }
                case VfxKind.VLight:
                {
                    var alpha = MathHelper.Clamp(v.Ai1 / v.MaxTime, 0f, 1f);
                    var c = v.DrawColor;
                    c.A = 255;
                    var col = c * (alpha * 1.2f * v.Alpha);
                    var stretch = new Vector2(0.1f + alpha * 0.7f, 10f + alpha * 10f) * v.Scale;
                    var origin = Origin(Tex.Ball);
                    XiheFx.Sprite(v.Center, Tex.Ball, col * 0.5f, 0f, stretch, origin, additive: true);
                    XiheFx.Sprite(v.Center, Tex.Ball, col, 0f, stretch, origin, additive: true);
                    XiheFx.Sprite(v.Center, Tex.Ball, col, 0f, Vector2.One * (2f * v.Scale), origin,
                        additive: true);
                    break;
                }
                case VfxKind.Circle:
                {
                    // 源 Circle.Draw：200 段环带，半径 ai1、宽 18，颜色 (255,255,150)*alpha，Additive。
                    var a = MathHelper.Clamp(v.Alpha, 0f, 1f);
                    if (a <= 0.002f || v.Ai1 <= 0.5f) break;
                    var color = new Color(255, 255, 150) * a;
                    color.A = 255;

                    const int counts = 200;
                    const float d = 18f;
                    var radius = v.Ai1;
                    var verts = new List<TriVertex>(counts * 6);
                    var pa = new Vector2[counts + 1];
                    var pb = new Vector2[counts + 1];
                    for (var i = 0; i <= counts; i++)
                    {
                        var dir = XiheHelper.Vxr(i * (float)Math.PI * 2f / counts);
                        pa[i] = v.Center + dir * radius;
                        pb[i] = v.Center + dir * (radius + d);
                    }
                    for (var i = 0; i < counts; i++)
                    {
                        var u0 = i / (float)counts;
                        var u1 = (i + 1) / (float)counts;
                        verts.Add(new TriVertex { World = pa[i], Uv = new Vector2(u0, 0f), Color = color });
                        verts.Add(new TriVertex { World = pb[i], Uv = new Vector2(u0, 1f), Color = color });
                        verts.Add(new TriVertex { World = pa[i + 1], Uv = new Vector2(u1, 0f), Color = color });
                        verts.Add(new TriVertex { World = pb[i], Uv = new Vector2(u0, 1f), Color = color });
                        verts.Add(new TriVertex { World = pb[i + 1], Uv = new Vector2(u1, 1f), Color = color });
                        verts.Add(new TriVertex { World = pa[i + 1], Uv = new Vector2(u1, 0f), Color = color });
                    }
                    XiheFx.Tri(Tex.Wave, true, true, false, false, verts.ToArray());
                    break;
                }
                case VfxKind.CircleWarp:
                {
                    // 源：isWarp，WarpBall 当遮罩；半径按 81px × scale 折算。
                    var rad = 40f * Math.Abs(v.Scale) / Math.Max(1, Main.screenHeight);
                    XiheFx.Warp(v.Center, rad, MathHelper.Max(rad, 0.02f),
                        0.03f * MathHelper.Clamp(v.Alpha, 0f, 1f));
                    break;
                }
                case VfxKind.LightDust:
                {
                    var c = v.DrawColor;
                    c.A = 255;
                    var rot = (float)Math.Atan2(v.Velocity.Y, v.Velocity.X);
                    var sc = new Vector2(1.2f, 0.8f) * v.Ai0 * v.Scale;
                    var origin = Origin(Tex.Ball);
                    // 源是 5 层；手机端只画 3 层（0/2/4），拖尾长度与亮度几乎不变，精灵数少 40%。
                    for (var i = 0; i < 3; i++)
                    {
                        var layer = i * 2;
                        var pos = v.Center - v.Velocity * layer / 3f;
                        XiheFx.Sprite(pos, Tex.Ball, c * (v.Alpha * (1f - layer / 5f) * 1.2f), rot, sc,
                            origin, additive: true);
                    }
                    break;
                }
                case VfxKind.LightDustCn:
                {
                    var c = v.DrawColor;
                    c.A = 255;
                    XiheFx.Sprite(v.Center, Tex.DustLight, c * MathHelper.Clamp(v.Alpha, 0f, 1f), 0f,
                        Vector2.One * v.Scale, Origin(Tex.DustLight), additive: true);
                    break;
                }
                case VfxKind.LineWarp:
                {
                    // 源：isWarp，画一条 Ball 拉长的遮罩；这里用整屏环带扭曲近似（半径 0）。
                    XiheFx.Warp(v.Center, 0f, 0.6f, 0.02f * MathHelper.Clamp(v.Alpha, 0f, 1f));
                    break;
                }
                case VfxKind.Explosion:
                {
                    // 源：Explosion 7 帧，source=(0, ai1*h/7, w, h/7)，Colorize+AlphaBlend。
                    var h = XiheTextures.HeightOf(Tex.Explosion);
                    var fh = h / 7;
                    var frame = (int)MathHelper.Clamp(v.Ai1, 0f, 6f);
                    XiheFx.SpriteRect(v.Center, Tex.Explosion, new Rectangle(0, frame * fh,
                            XiheTextures.WidthOf(Tex.Explosion), fh),
                        v.DrawColor * v.Alpha, v.Rotation,
                        Vector2.One * v.Scale, new Vector2(XiheTextures.WidthOf(Tex.Explosion), fh) / 2f,
                        additive: false, colorize: true);
                    break;
                }
                case VfxKind.MagicRingSun:
                {
                    // 源 MagicRing_Sun.Draw：30 段 3D 环，半径 100*scale、带宽 width=32*scale，
                    // 逐点 Projection(viewZ=1200)；tex3 的 u 走 5 圈 PointWrap。
                    if (v.Scale <= 0.01f || v.TimeLeft <= 0) break;
                    // 不在屏幕里的法阵直接不提交（留着也是每个像素跑 40 个三角形）。
                    var cuv = XiheFx.ToUv(v.Center);
                    var crad = 140f * v.Scale / Math.Max(1, Main.screenHeight);
                    if (cuv.X + crad < 0f || cuv.X - crad > 1f || cuv.Y + crad < 0f || cuv.Y - crad > 1f) break;
                    var color = new Color(220, 100, 50) * v.Alpha;
                    color.A = 255;

                    var center3 = new Vector3(v.Center.X, v.Center.Y, 0f);
                    var scCenter = Main.screenPosition
                                   + new Vector2(Main.screenWidth, Main.screenHeight) / 2f;
                    var width = 32f * v.Scale;
                    var t = v.Ai1 * 0.1f;
                    const int counts = 30;   // 源 MagicRing_Sun.Draw: counts = 30

                    var lo = new TriVertex[counts + 1];
                    var hi = new TriVertex[counts + 1];
                    for (var i = 0; i <= counts; i++)
                    {
                        var rot = XiheHelper.Vec3RotBy("Y", Vector3.UnitX,
                            i * (float)Math.PI * 2f / counts) * 100f * v.Scale;
                        var a = center3 + RotV(rot + new Vector3(0f, -width, 0f), v.Ai1, v.Random, v.Rand2);
                        var b = center3 + RotV(rot + new Vector3(0f, width, 0f), v.Ai1, v.Random, v.Rand2);
                        var pa = XiheHelper.Projection(a, scCenter, 1200f);
                        var pb = XiheHelper.Projection(b, scCenter, 1200f);
                        // 源：第一个顶点 Projection 的 k += 0.05，第二个顶点复用同一个 k（没有重新取 out）。
                        var ka = -1200f / (a.Z - 1200f) + 0.05f;
                        var u = 5f * i / counts + t;
                        lo[i] = new TriVertex { World = pa, Uv = new Vector2(u, 0f), Color = color * ka };
                        hi[i] = new TriVertex { World = pb, Uv = new Vector2(u, 1f), Color = color * ka };
                    }

                    var tris = new System.Collections.Generic.List<TriVertex>(counts * 6);
                    for (var i = 0; i < counts; i++)
                    {
                        tris.Add(lo[i]); tris.Add(hi[i]); tris.Add(lo[i + 1]);
                        tris.Add(hi[i]); tris.Add(lo[i + 1]); tris.Add(hi[i + 1]);
                    }
                    XiheFx.Tri(Tex.Tex3, true, true, false, true, tris.ToArray());
                    break;
                }
            }
        }

        /// <summary>EOLEff 的原版剪影（Boss.PreDraw 里调，保持和 MEAC 一样的翅膀/身体贴图）。</summary>
        internal static void DrawEolSprites(SpriteBatch sb, Vector2 screenPos, float npcAlpha)
        {
            foreach (var v in List)
            {
                if (v.Kind != VfxKind.EolEff) continue;
                try
                {
                    var frame = v.TimeLeft / 2 % 11;
                    var color = XiheHelper.HsvToRgb(v.Golden ? 50f : (v.TimeLeft * 5f + v.Ai1 * 10f) % 360f, 0.6f)
                                * ((v.Dis / 300f) * (v.Dis / 300f)) * v.Alpha;
                    color.A = (byte)MathHelper.Clamp(255f * (v.Dis / 300f) * (v.Dis / 300f) * v.Alpha, 0f, 255f);
                    DrawBossEcho(sb, v.Center - screenPos, frame, color);
                }
                catch { }
            }
        }

        private static Vector3 RotV(Vector3 vec, float ai1, float random, float rand2)
        {
            vec = XiheHelper.Vec3RotBy("Z", vec, ai1 * 0.025f + random * 0.2f + rand2);
            vec = XiheHelper.Vec3RotBy("X", vec, ai1 * 0.01f + random * 0.3f + rand2 * 1.2f);
            vec = XiheHelper.Vec3RotBy("Y", vec, ai1 * 0.015f + random * 0.4f + rand2 * 3f);
            return vec;
        }

        private static void DrawBossEcho(SpriteBatch sb, Vector2 pos, int frame, Color color)
        {
            var tex = XiheBossNpc.BodyTexture;
            var wing1 = XiheBossNpc.WingTexture;
            var wing2 = XiheBossNpc.WingGlowTexture;
            if (tex == null || wing1 == null || wing2 == null) return;

            const int wingFrames = 11;
            var fr = Utils.Frame(wing1, 1, wingFrames, 0, frame % wingFrames, 0, 0);
            var origin = new Vector2(wing1.Width, wing1.Height / wingFrames) / 2f;
            XiheDraw.Sprite(sb, wing2, pos, fr, color, 0f, origin, 1.8f);
            XiheDraw.Sprite(sb, wing1, pos, fr, color, 0f, origin, 1.8f);
            XiheDraw.Sprite(sb, tex, pos, Utils.Frame(tex, 1, 2, 0, 1, 0, 0), color, 0f, new Vector2(tex.Width, tex.Height / 2) / 2f, 1f);
        }
    }
}









