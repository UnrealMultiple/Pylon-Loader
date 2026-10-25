// EoLBigLaser 的专用渲染：直接复用 XiheMagic 的 XiheBeam.frag（同一套 tex3/tex0 滚动、落点柔光、
// MagicRing 的逐像素 3D 投影反解）。这样 Boss 的大光柱与「羲和魔杖」的光柱是同一份着色器实现。
//
// 每个 EoLBigLaser 一步 pass；环由 EoLBigLaser 的 timeLeft 时刻表驱动（350/330/320/260，模式 0 是 210）。
// 如果 shader 没加载成功，EoLBigLaser.PushVisual 会退回 XiheFx 的图元版本，画面不会完全消失。
using System;
using System.Collections.Generic;
using Il2CppMicrosoft.Xna.Framework;
using Color = Il2CppMicrosoft.Xna.Framework.Graphics.Color;
using Pylon.ModLoader;
using Pylon.ModLoader.Effects;
using Terraria;
using UVector2 = UnityEngine.Vector2;
using ScreenShader = Pylon.ModLoader.Effects.ScreenShader;

namespace XiheBoss
{
    internal static class XiheBigLaserFx
    {
        internal static ScreenShader Shader;

        private sealed class Ring
        {
            public int Birth;
            public float Y;
            public float Ai0;
            public int Life;
        }

        private sealed class State
        {
            public int LastTimeLeft;
            public readonly List<Ring> Rings = new();
        }

        private static readonly Dictionary<int, State> States = new();

        internal static void Init(Mod mod, ModLogger log)
        {
            try
            {
                Shader = ScreenShader.FromFile(mod, "assets/effects/XiheBigLaser.frag");
                log?.Info("[fx] XiheBigLaser.frag 已注册（复用 XiheMagic 的光柱着色器）");
            }
            catch (Exception e)
            {
                log?.Error("[fx] XiheBigLaser.frag 加载失败，大光柱退回图元版本：" + e.Message);
            }
        }

        internal static void Reset() => States.Clear();

        internal static void Submit(ScreenFrame frame)
        {
            if (Shader == null) return;
            var type = ModContent.ProjectileType<EoLBigLaser>();
            if (type <= 0) return;

            var seen = new List<int>();
            var sw = Math.Max(1, Main.screenWidth);
            var sh = Math.Max(1, Main.screenHeight);
            var sp = Main.screenPosition;

            for (var i = 0; i < Main.projectile.Length; i++)
            {
                var p = Main.projectile[i];
                if (p == null || !p.active || p.type != type) continue;

                seen.Add(p.whoAmI);
                if (!States.TryGetValue(p.whoAmI, out var state))
                {
                    state = new State { LastTimeLeft = p.timeLeft + 1 };
                    States[p.whoAmI] = state;

                    var ai = p.ai;
                    if (ai[1] == 0f) AddRing(state.Rings, p.timeLeft, 0f, -0.2f, 210);
                }

                // 源的 CreateRing 时刻（模式 1）。
                var last = state.LastTimeLeft;
                var now = p.timeLeft;
                if (last > 350 && now <= 350) AddRing(state.Rings, now, 0f, -0.2f, 90);
                if (last > 330 && now <= 330) AddRing(state.Rings, now, 200f, -0.5f, 70);
                if (last > 320 && now <= 320) AddRing(state.Rings, now, 400f, 0f, 60);
                if (last > 260 && now <= 260)
                {
                    AddRing(state.Rings, now, 0f, 0f, 260);
                    AddRing(state.Rings, now, 200f, -0.3f, 260);
                    AddRing(state.Rings, now, 400f, 0.2f, 260);
                }
                state.LastTimeLeft = now;

                if (p.scale <= 0.001f) continue;

                var uvx = (p.Center.X - sp.X) / sw;
                var uvy = 1f - (p.Center.Y - sp.Y) / sh;
                var uvImpact = 1f - ((p.Center.Y + 300f) - sp.Y) / sh;

                var fx = new ScreenEffect(Shader)
                {
                    Layer = ScreenEffectLayer.World,
                    Box = new[] { 0f, 0f, 1f, 1f },
                    Center = new UVector2(MathHelper.Clamp(uvx, 0f, 1f), MathHelper.Clamp(uvy, 0f, 1f)),
                    Intensity = 1f,
                };

                var ai2 = p.ai;
                var beam = ai2[1] == 0f
                    ? MathHelper.Clamp(p.scale / 2f, 0f, 1f)
                    : 0.55f + 0.45f * MathHelper.Clamp(p.scale / 3.16f, 0f, 1f);

                fx.Uniform("uCharge", 0f);
                fx.Uniform("uBeam", beam);
                fx.Uniform("uFlash", 0f);
                fx.Uniform("uSky", 0f);
                fx.Uniform("uShake", 0f);
                fx.Uniform("uImpact", MathHelper.Clamp(uvImpact, -0.5f, 1.5f));
                fx.Uniform("uBeamWidth", MathHelper.Clamp(p.scale * 128f / sh, 0.002f, 0.5f));
                fx.Uniform("uBeamX", MathHelper.Clamp(uvx, -0.5f, 1.5f));
                fx.Uniform("uTexOn", 1f);
                fx.Uniform("uRingViewZ", 1000f / sh);
                fx.Uniform("uRingHalo", 3f / sh);
                // 光柱 pass 不再跑环（环改用硬件几何，见 BuildRings）。
                fx.Uniform("uRingCount", 0f);

                if (XiheTextures.BigOuter != null) fx.Texture("uBeamTex", XiheTextures.BigOuter);
                if (XiheTextures.BigCore != null) fx.Texture("uCoreTex", XiheTextures.BigCore);
                if (XiheTextures.BigArc != null) fx.Texture("uArcTex", XiheTextures.BigArc);
                if (XiheTextures.BigStar != null) fx.Texture("uStarTex", XiheTextures.BigStar);
                if (XiheTextures.BigRing != null) fx.Texture("uRingStrip", XiheTextures.BigRing);
                frame.Add(fx);

                // 环：按 MEAC.Entities.MagicRing.Draw 的 3D 顶点投影逐点生成（counts=50、sX=3、
                // 半径 110*scale*ai0、带宽 10*scale*ai0、PointWrap、Additive），交给硬件几何通道。
                // 旧实现用逐像素反解：环越大，包围盒里每个像素都要算 atan/acos/exp，这是
                // “法阵一扩大就掉帧”的根因；现在 GPU 只光栅化环带本身。
                BuildRings(p);
            }

            // 清掉已经不存在的弹幕状态。
            if (seen.Count == 0)
            {
                States.Clear();
            }
            else
            {
                var dead = new List<int>();
                foreach (var key in States.Keys)
                    if (!seen.Contains(key)) dead.Add(key);
                foreach (var key in dead) States.Remove(key);
            }
        }

        /// <summary>MEAC MagicRing.AI（laserOwner 存在时的分支）逐 tick 复算 scale。</summary>
        private static float RingScale(Ring r, int laserTimeLeft, int age)
        {
            var scale = 0f;
            var tl = r.Life;
            for (var i = 0; i < age; i++)
            {
                var laserTl = laserTimeLeft + (age - i);
                if (tl > r.Life - 20) scale = XiheHelper.SmoothStepQ(scale, 4f, 0.2f);
                else if (laserTl > 20 && tl > 15) tl = 20;

                if (laserTl <= 20) tl = 15;
                if (tl > 10 && tl < 15) scale = XiheHelper.SmoothStepQ(scale, 5f, 0.2f);
                if (tl < 10) scale = XiheHelper.SmoothStepQ(scale, 0f, 0.35f);
                tl--;
            }
            return scale;
        }

        /// <summary>MagicRing.Draw 的顶点：50 段、画布中心投影（viewZ=1000）、sX=3 的 u 滚动。</summary>
        private static void BuildRings(Projectile p)
        {
            if (!States.TryGetValue(p.whoAmI, out var state) || state.Rings.Count == 0) return;

            var sw = Math.Max(1, Main.screenWidth);
            var sh = Math.Max(1, Main.screenHeight);
            var scCenter = Main.screenPosition + new Vector2(sw, sh) / 2f;
            var t = -(float)(int)Main.timeForVisualEffects * 0.01f;   // MagicRing.cn = true
            const int counts = 50;
            var verts = new List<TriVertex>();

            foreach (var r in state.Rings)
            {
                var age = r.Birth - p.timeLeft;
                if (age < 0 || age > r.Life + 32) continue;

                var s = RingScale(r, p.timeLeft, age);
                if (s <= 0.01f) continue;

                var radius = 110f * s * r.Ai0;
                var width = 10f * s * r.Ai0;
                if (radius < 0.5f || width < 0.05f) continue;

                var center = new Vector3(p.Center.X, p.Center.Y + r.Y, 0f);

                // 屏幕外不提交（投影后半径最多约 (radius + width) * viewZ/(viewZ-radius)）。
                var ringUv = XiheFx.ToUv(p.Center + new Vector2(0f, r.Y));
                var rUv = (radius + width) * 1.4f / sh;
                if (ringUv.X + rUv < 0f || ringUv.X - rUv > 1f || ringUv.Y + rUv < 0f || ringUv.Y - rUv > 1f)
                    continue;

                var a = new Vector2[counts + 1];
                var b = new Vector2[counts + 1];
                var colors = new Color[counts + 1];
                for (var i = 0; i <= counts; i++)
                {
                    var rot = XiheHelper.Vec3RotBy("Y", Vector3.UnitX,
                        i * (float)Math.PI * 2f / counts) * radius;
                    var pa3 = center + rot + new Vector3(0f, -width, 0f);
                    var pb3 = center + rot + new Vector3(0f, width, 0f);

                    a[i] = XiheHelper.Projection(pa3, scCenter, 1000f);
                    var k = -1000f / (pa3.Z - 1000f) + 0.05f;   // 源：第一个顶点 k += 0.05，第二个复用
                    b[i] = XiheHelper.Projection(pb3, scCenter, 1000f);

                    // 源 drawColor = White（MagicRing 没改 drawColor），顶点色 = White * k。
                    var kc = MathHelper.Clamp(k, 0f, 1f);
                    colors[i] = new Color(kc, kc, kc, 1f);
                }

                for (var i = 0; i < counts; i++)
                {
                    var u0 = 3f * i / counts + t;
                    var u1 = 3f * (i + 1) / counts + t;
                    verts.Add(new TriVertex { World = a[i], Uv = new Vector2(u0, 0f), Color = colors[i] });
                    verts.Add(new TriVertex { World = b[i], Uv = new Vector2(u0, 1f), Color = colors[i] });
                    verts.Add(new TriVertex { World = a[i + 1], Uv = new Vector2(u1, 0f), Color = colors[i + 1] });

                    verts.Add(new TriVertex { World = b[i], Uv = new Vector2(u0, 1f), Color = colors[i] });
                    verts.Add(new TriVertex { World = b[i + 1], Uv = new Vector2(u1, 1f), Color = colors[i + 1] });
                    verts.Add(new TriVertex { World = a[i + 1], Uv = new Vector2(u1, 0f), Color = colors[i + 1] });
                }
            }

            if (verts.Count >= 3)
                XiheMesh.AddGeometry(Tex.MagicRing, true, false, true, true, verts);
        }

        private static void AddRing(List<Ring> rings, int birth, float y, float scale, int life)
        {
            rings.Add(new Ring { Birth = birth, Y = y, Ai0 = 1f + scale, Life = life });
        }
    }
}

