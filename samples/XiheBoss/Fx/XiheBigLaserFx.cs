// EoLBigLaser 的专用渲染：直接复用 XiheMagic 的 XiheBeam.frag（同一套 tex3/tex0 滚动、落点柔光、
// MagicRing 的逐像素 3D 投影反解）。这样 Boss 的大光柱与「羲和魔杖」的光柱是同一份着色器实现。
//
// 每个 EoLBigLaser 一步 pass；环由 EoLBigLaser 的 timeLeft 时刻表驱动（350/330/320/260，模式 0 是 210）。
// 如果 shader 没加载成功，EoLBigLaser.PushVisual 会退回 XiheFx 的图元版本，画面不会完全消失。
using System;
using System.Collections.Generic;
using Il2CppMicrosoft.Xna.Framework;
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
                // 光柱 pass 不再跑环（环的 3D 反解很贵，之前每个屏幕像素都要跑最多 8 次）。
                fx.Uniform("uRingCount", 0f);

                // 环：按年龄展开到 4（源 SmoothStep 0.2 走 20 tick 的结果约 3.556），最后 10 tick 收掉。
                var ringData = new List<float>(state.Rings.Count * 4);
                var ringCount = 0;
                var rx0 = 1f; var ry0 = 1f; var rx1 = 0f; var ry1 = 0f;
                foreach (var r in state.Rings)
                {
                    var age = r.Birth - p.timeLeft;
                    if (age < 0) continue;
                    var remain = r.Life - age;
                    if (remain <= 0) continue;

                    var s = 4f * (1f - (float)Math.Exp(-age / 9f));
                    if (remain < 10) s *= remain / 10f;
                    if (s <= 0.01f) continue;

                    var radius = 110f * s * r.Ai0;
                    var halfW = 10f * s * r.Ai0;
                    var ringUv = XiheFx.ToUv(p.Center + new Vector2(0f, r.Y));
                    var rUv = (radius + halfW) / sh;
                    if (ringUv.X + rUv < 0f || ringUv.X - rUv > 1f || ringUv.Y + rUv < 0f || ringUv.Y - rUv > 1f)
                        continue;   // 屏幕外不提交
                    ringData.Add(ringUv.X);
                    ringData.Add(ringUv.Y);
                    ringData.Add(radius / sh);
                    ringData.Add(halfW / sh);
                    rx0 = Math.Min(rx0, ringUv.X - rUv); ry0 = Math.Min(ry0, ringUv.Y - rUv);
                    rx1 = Math.Max(rx1, ringUv.X + rUv); ry1 = Math.Max(ry1, ringUv.Y + rUv);
                    ringCount++;
                    if (ringCount >= 8) break;
                }

                if (XiheTextures.BigOuter != null) fx.Texture("uBeamTex", XiheTextures.BigOuter);
                if (XiheTextures.BigCore != null) fx.Texture("uCoreTex", XiheTextures.BigCore);
                if (XiheTextures.BigArc != null) fx.Texture("uArcTex", XiheTextures.BigArc);
                if (XiheTextures.BigStar != null) fx.Texture("uStarTex", XiheTextures.BigStar);
                if (XiheTextures.BigRing != null) fx.Texture("uRingStrip", XiheTextures.BigRing);
                frame.Add(fx);

                // 环单独一步，只在自己的包围盒里跑：和把环塞进全屏光柱 pass 的画面完全一致，
                // 但第二根光柱（环多）不会再让每个像素都跑 8 次 3D 反解。
                if (ringCount > 0)
                {
                    var ringFx = new ScreenEffect(Shader)
                    {
                        Layer = ScreenEffectLayer.World,
                        Box = new[]
                        {
                            MathHelper.Clamp(rx0 - 0.02f, 0f, 1f), MathHelper.Clamp(ry0 - 0.02f, 0f, 1f),
                            MathHelper.Clamp(rx1 + 0.02f, 0f, 1f), MathHelper.Clamp(ry1 + 0.02f, 0f, 1f),
                        },
                        Center = new UVector2(MathHelper.Clamp(uvx, 0f, 1f), MathHelper.Clamp(uvy, 0f, 1f)),
                        Intensity = 1f,
                    };
                    ringFx.Uniform("uCharge", 0f);
                    ringFx.Uniform("uBeam", 0f);
                    ringFx.Uniform("uFlash", 0f);
                    ringFx.Uniform("uSky", 0f);
                    ringFx.Uniform("uShake", 0f);
                    ringFx.Uniform("uImpact", 0f);
                    ringFx.Uniform("uBeamWidth", 0.01f);
                    ringFx.Uniform("uBeamX", -1f);
                    ringFx.Uniform("uTexOn", 1f);
                    ringFx.Uniform("uRingViewZ", 1000f / sh);
                    ringFx.Uniform("uRingHalo", 3f / sh);
                    ringFx.Uniform("uRings", ringData.ToArray());
                    ringFx.Uniform("uRingCount", ringCount);
                    if (XiheTextures.BigRing != null) ringFx.Texture("uRingStrip", XiheTextures.BigRing);
                    if (XiheTextures.BigArc != null) ringFx.Texture("uArcTex", XiheTextures.BigArc);
                    if (XiheTextures.BigStar != null) ringFx.Texture("uStarTex", XiheTextures.BigStar);
                    frame.Add(ringFx);
                }
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

        private static void AddRing(List<Ring> rings, int birth, float y, float scale, int life)
        {
            rings.Add(new Ring { Birth = birth, Y = y, Ai0 = 1f + scale, Life = life });
        }
    }
}

