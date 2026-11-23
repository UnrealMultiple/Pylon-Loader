using System;
using System.Collections.Generic;
using Il2CppMicrosoft.Xna.Framework;
using Color = Il2CppMicrosoft.Xna.Framework.Graphics.Color;
using Pylon.ModLoader;
using Pylon.ModLoader.Effects;
using Terraria;
using UVector2 = UnityEngine.Vector2;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;
using Pylon.ModLoader.Logging;

namespace XiheBoss
{
    internal static class XiheBigLaserFx
    {
        internal static ScreenGraphics.Shader Shader;

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
                Shader = ScreenGraphics.Shader.FromFile(mod, "assets/effects/XiheBigLaser.frag");
                log?.Info("[fx] XiheBigLaser.frag 已注册（复用 XiheMagic 的光柱着色器）");
            }
            catch (Exception e)
            {
                log?.Error("[fx] XiheBigLaser.frag 加载失败，大光柱退回图元版本：" + e);
            }
        }

        internal static void Reset() => States.Clear();

        internal static void Submit(ScreenFrame frame)
        {

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

                BuildRings(p);
            }

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

        private static void BuildRings(Projectile p)
        {
            if (!States.TryGetValue(p.whoAmI, out var state) || state.Rings.Count == 0) return;

            var sw = Math.Max(1, Main.screenWidth);
            var sh = Math.Max(1, Main.screenHeight);
            var scCenter = Main.screenPosition + new Vector2(sw, sh) / 2f;
            var t = -(float)(int)Main.timeForVisualEffects * 0.01f;
            const int counts = 50;
            var verts = new List<TriVertex>();

            foreach (var r in state.Rings)
            {
                var age = r.Birth - p.timeLeft;

                if (age < 0) continue;

                var s = RingScale(r, p.timeLeft, age);
                if (s <= 0.01f) continue;

                var radius = 110f * s * r.Ai0;
                var width = 10f * s * r.Ai0;
                if (radius < 0.5f || width < 0.05f) continue;

                var center = new Vector3(p.Center.X, p.Center.Y + r.Y, 0f);

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
                    var k = -1000f / (pa3.Z - 1000f) + 0.05f;
                    b[i] = XiheHelper.Projection(pb3, scCenter, 1000f);

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
