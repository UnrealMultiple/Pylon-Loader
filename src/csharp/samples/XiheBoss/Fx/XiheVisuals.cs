using System;
using System.Collections.Generic;
using Il2CppMicrosoft.Xna.Framework;
using Color = Il2CppMicrosoft.Xna.Framework.Graphics.Color;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;
using Pylon.ModLoader.Install;

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
        LightDust00,
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

        internal static IReadOnlyList<Vfx> All => List;

        internal static void Clear() => List.Clear();

        internal static Vfx Create(VfxKind kind, Vector2 center, Vector2 velocity, float rotation = 0f,
            float scale = 1f, int ownerNpc = -1)
        {
            var v = new Vfx { Kind = kind, Center = center, Velocity = velocity, Rotation = rotation, OwnerNpc = ownerNpc };
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
                case VfxKind.LightDust00:

                    v.MaxTime = 30;
                    v.Ai0 = 0.3f;
                    v.Random = XiheHelper.RandBool() ? 1f : -1f;
                    break;
            }
            v.TimeLeft = v.MaxTime;
            if (scale != 1f) v.Scale = scale;
            List.Add(v);
            return v;
        }

        private static readonly HashSet<VfxKind> _stepLogged = [];
        private static readonly HashSet<VfxKind> _pushLogged = [];
        private static bool _echoLogged;

        private static void ReportOnce(HashSet<VfxKind> seen, VfxKind kind, string stage, Exception e)
        {
            if (!seen.Add(kind)) return;
            XiheBossMod.Log?.Warn("[vfx] " + kind + " " + stage + "：" + e);
        }

        internal static void Tick()
        {
            for (var i = List.Count - 1; i >= 0; i--)
            {
                var v = List[i];
                try { Step(v); }
                catch (Exception e) { ReportOnce(_stepLogged, v.Kind, "异常", e); }

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

                            var orbit = XiheHelper.Vec3RotBy("Y", Vector3.UnitX, v.VAngle);
                            orbit = XiheHelper.Vec3RotBy("Z", orbit, v.Ai1);
                            orbit = XiheHelper.Vec3RotBy("Y", orbit, v.Ai1);
                            var k = 2000f / (2000f - orbit.Z);
                            v.Scale = k;
                            v.Center = npc.Center + new Vector2(orbit.X, orbit.Y) * (v.Dis * k);
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
                case VfxKind.LightDust00:
                {

                    var t = v.MaxTime / 3f;
                    v.Velocity = XiheHelper.RotatedBy(v.Velocity, 0.05f * v.Random) * 1.02f;
                    if (v.TimeLeft < t)
                    {
                        v.Ai0 -= 0.3f / t;
                        v.Velocity *= 0.94f;
                    }
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

        internal static void PushAll()
        {
            foreach (var v in List)
            {
                try { Push(v); }
                catch (Exception e) { ReportOnce(_pushLogged, v.Kind, "绘制异常", e); }
            }
        }

        private static Vector2 Origin(Tex tex) =>
            new Vector2(XiheTextures.WidthOf(tex), XiheTextures.HeightOf(tex)) / 2f;

        private static void Push(Vfx v)
        {
            switch (v.Kind)
            {
                case VfxKind.EolEff:

                    break;

                case VfxKind.Wave:
                {

                    break;
                }
                case VfxKind.RingWarp:
                case VfxKind.RingWarp2:
                {

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
                    var col = XiheHelper.MulClamp(c, alpha * 1.2f * v.Alpha);
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

                    var rad = 40f * Math.Abs(v.Scale) / Math.Max(1, Main.screenHeight);
                    XiheFx.Warp(v.Center, rad, MathHelper.Max(rad, 0.02f),
                        0.03f * MathHelper.Clamp(v.Alpha, 0f, 1f));
                    break;
                }
                case VfxKind.LightDust:
                {
                    var c = v.DrawColor;
                    c.A = 0;
                    var rot = (float)Math.Atan2(v.Velocity.Y, v.Velocity.X);
                    var sc = new Vector2(1.2f, 0.8f) * v.Ai0 * v.Scale;

                    for (var i = 0; i < 5; i++)
                    {
                        var pos = v.Center - v.Velocity * i / 3f;
                        XiheMesh.AddBallGlow(pos, rot, sc, XiheHelper.MulClamp(c, v.Alpha * (1f - i / 5f) * 1.2f));
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

                    XiheFx.Warp(v.Center, 0f, 0.6f, 0.02f * MathHelper.Clamp(v.Alpha, 0f, 1f));
                    break;
                }
                case VfxKind.Explosion:
                {

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
                case VfxKind.LightDust00:
                {

                    var rot = (float)Math.Atan2(v.Velocity.Y, v.Velocity.X);
                    var offset = Math.Min(v.Velocity.Length(), 18f * v.Scale);
                    var sc = new Vector2(1.5f, 0.7f) * v.Ai0 * v.Scale;
                    var c = v.BloomColor;
                    c.A = 0;
                    var dir = XiheHelper.Normalize2(v.Velocity);
                    if (dir == Vector2.Zero) dir = XiheHelper.Vxr(rot);
                    for (var i = 0; i < 5; i++)
                    {
                        var pos = v.Center - dir * offset * i / 3f;
                        XiheMesh.AddBallGlow(pos, rot, sc, XiheHelper.MulClamp(c, v.Alpha * (1f - i / 5f) * 1.2f));
                    }
                    break;
                }
                case VfxKind.MagicRingSun:
                {

                    if (v.Scale <= 0.01f || v.TimeLeft <= 0) break;

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
                    const int counts = 30;

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

        internal static void DrawEolSprites(SpriteBatch sb, Vector2 screenPos, float npcAlpha)
        {
            foreach (var v in List)
            {
                if (v.Kind != VfxKind.EolEff) continue;
                try
                {
                    var frame = v.TimeLeft / 2 % 11;
                    var color = XiheHelper.MulClamp(
                        XiheHelper.HsvToRgb(v.Golden ? 50f : (v.TimeLeft * 5f + v.Ai1 * 10f) % 360f, 0.6f),
                        (v.Dis / 300f) * (v.Dis / 300f) * v.Alpha);

                    color.A = 0;
                    DrawBossEcho(sb, v.Center - screenPos, frame, color);
                }
                catch (Exception e)
                {
                    if (!_echoLogged) { _echoLogged = true; XiheBossMod.Log?.Warn("[echo] 绘制异常：" + e); }
                }
            }
        }

        private static Vector3 RotV(Vector3 vec, float ai1, float random, float rand2)
        {
            vec = XiheHelper.Vec3RotBy("Z", vec, ai1 * 0.025f + random * 0.2f + rand2);
            vec = XiheHelper.Vec3RotBy("X", vec, ai1 * 0.01f + random * 0.3f + rand2 * 1.2f);
            vec = XiheHelper.Vec3RotBy("Y", vec, ai1 * 0.015f + random * 0.4f + rand2 * 3f);
            return vec;
        }

        private static Texture2D _echoWing;
        private static Texture2D _echoWingGlow;

        private static void DrawBossEcho(SpriteBatch sb, Vector2 pos, int frame, Color color)
        {
            var tex = XiheBossNpc.BodyTexture;

            _echoWing ??= Main.Assets.Request<Texture2D>("Images/Extra_157", ReLogic.Content.AssetRequestMode.ImmediateLoad)?.Value;
            _echoWingGlow ??= Main.Assets.Request<Texture2D>("Images/Extra_159", ReLogic.Content.AssetRequestMode.ImmediateLoad)?.Value;
            var wing1 = _echoWing;
            var wing2 = _echoWingGlow;
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
