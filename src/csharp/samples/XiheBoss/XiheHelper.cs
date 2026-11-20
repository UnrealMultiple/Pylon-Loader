using System;
using Il2CppMicrosoft.Xna.Framework;
using Color = Il2CppMicrosoft.Xna.Framework.Graphics.Color;
using Terraria;
using Pylon.ModLoader.Core;

namespace XiheBoss
{
    internal static class XiheHelper
    {

        public static void LerpValue(ref float value, float b, float n) => value = MathHelper.Lerp(value, b, n);

        public static Vector2 Normalize2(Vector2 v)
        {
            if (v == Vector2.Zero) return new Vector2(0f, 0.001f);
            return Vector2.Normalize(v);
        }

        public static Vector2 DirectionTo(Vector2 from, Vector2 to)
        {
            if (to - from == Vector2.Zero) return Vector2.Zero;
            return Vector2.Normalize(to - from);
        }

        public static Vector2 DirectionFrom(Vector2 from, Vector2 to)
        {
            if (to - from == Vector2.Zero) return Vector2.Zero;
            return -Vector2.Normalize(to - from);
        }

        public static Vector2 Vxr(float rot) => new Vector2((float)Math.Cos(rot), (float)Math.Sin(rot));

        public static Vector2 VerticalVec(Vector2 vec) => new Vector2(0f - vec.Y, vec.X);

        public static Color HsvToRgb(float H, float S = 1f, float V = 1f)
        {
            float R = 0f, G = 0f, B = 0f;
            H /= 60f;
            var i = (int)H;
            var f = H - i;
            var a = V * (1f - S);
            var b = V * (1f - S * f);
            var c = V * (1f - S * (1f - f));
            switch (i % 6)
            {
                case 0: R = V; G = c; B = a; break;
                case 1: R = b; G = V; B = a; break;
                case 2: R = a; G = V; B = c; break;
                case 3: R = a; G = b; B = V; break;
                case 4: R = c; G = a; B = V; break;
                case 5: R = V; G = a; B = b; break;
            }
            return new Color(R, G, B);
        }

        public static string RandomString(int length)
        {
            const string str = "abcdefghmnopqrstFGHMNOPQRSTUVWXYZ~!@#$%^&*()_+{}|[]<>;':,./?";
            var sum = "";
            for (var i = 0; i < length; i++) sum += str[Main.rand.Next(str.Length - 1)].ToString();
            return sum;
        }

        public static void MoveTo(NPC npc, Vector2 targetPos, float speed, float n)
        {
            var targetVec = Normalize2(targetPos - npc.Center) * speed;
            npc.velocity = (npc.velocity * n + targetVec) / (n + 1f);
        }

        public static void SpinAI(NPC npc, Vector2 center, float v)
        {
            var oldPos = npc.Center;
            npc.Center = center + RotatedBy(oldPos - center, v);
            npc.velocity = npc.Center - oldPos;
            npc.position -= npc.velocity;
        }

        public const float BeatTicks = 51.4285714f;
        public const float HalfBeatTicks = 25.7142857f;

        public static int BeatTime(int n) => (int)(n * BeatTicks);

        public static Vector2 RotatedBy(Vector2 v, float radians) =>
            Utils.RotatedBy(v, (double)radians, default(Vector2));

        public static Vector2 RotateRandom(Vector2 v, float maxRadians) =>
            RotatedBy(v, (float)((Main.rand.NextDouble() * 2.0 - 1.0) * maxRadians));

        public static double Gaussian(double mu, double sigma)
        {
            var u1 = 1.0 - Main.rand.NextDouble();
            var u2 = 1.0 - Main.rand.NextDouble();
            var z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
            return mu + sigma * z;
        }

        public static float SmoothStepQ(float from, float to, float amount)
        {
            amount = MathHelper.Clamp(amount, 0f, 1f);
            amount = amount * amount * (3f - 2f * amount);
            return from + (to - from) * amount;
        }

        public static bool RandBool() => Main.rand.Next(2) == 0;

        public static bool RandBool(int n) => n <= 1 || Main.rand.Next(n) == 0;

        public static float NextFloatDirection() => Main.rand.Next(2) == 0 ? -1f : 1f;

        public static float NextFloat(float min, float max) =>
            min + (float)Main.rand.NextDouble() * (max - min);

        public static float AngleLerp(float a, float b, float n) =>
            a + MathHelper.WrapAngle(b - a) * MathHelper.Clamp(n, 0f, 1f);

        public static Vector2 NextVector2Unit() =>
            Utils.NextVector2Unit(Main.rand, 0f, (float)(Math.PI * 2.0));

        public static Vector2 NextVector2Unit(float min, float max) => Utils.NextVector2Unit(Main.rand, min, max);

        public static Vector2 NextVector2Circular(float x, float y = -1f)
        {
            if (y < 0f) y = x;
            return new Vector2((float)(Main.rand.NextDouble() * 2.0 - 1.0) * x,
                (float)(Main.rand.NextDouble() * 2.0 - 1.0) * y);
        }

        public static Vector2 ScreenCenter =>
            Main.screenPosition + new Vector2(Main.screenWidth, Main.screenHeight) / 2f;

        public static float[] ReadAi(NPC npc)
        {
            var a = npc.ai;
            return new[] { a[0], a[1], a[2], a[3] };
        }

        public static void WriteAi(NPC npc, float a0, float a1, float a2, float a3)
        {
            var a = npc.ai;
            a[0] = a0; a[1] = a1; a[2] = a2; a[3] = a3;
            npc.ai = a;
        }

        public static float[] ReadLocalAi(Projectile proj)
        {
            var a = proj.localAI;
            return new[] { a[0], a[1], a[2] };
        }

        public static float[] ReadLocalAi(NPC npc)
        {
            var a = npc.localAI;
            return new[] { a[0], a[1], a[2] };
        }

        public static void WriteLocalAi(Projectile proj, float a0, float a1, float a2, float a3)
        {
            var a = proj.localAI;
            a[0] = a0; a[1] = a1; a[2] = a2;
            proj.localAI = a;
        }

        public static void WriteLocalAi(NPC npc, float a0, float a1, float a2)
        {
            var a = npc.localAI;
            a[0] = a0; a[1] = a1; a[2] = a2;
            npc.localAI = a;
        }

        public static Color WithAlpha0(Color c) => new Color(c.R, c.G, c.B, (byte)0);

        public static Color MulClamp(Color c, float s)
        {
            return new Color((byte)MathHelper.Clamp(c.R * s, 0f, 255f),
                (byte)MathHelper.Clamp(c.G * s, 0f, 255f),
                (byte)MathHelper.Clamp(c.B * s, 0f, 255f),
                (byte)MathHelper.Clamp(c.A * s, 0f, 255f));
        }

        public static Vector3 Vec3RotBy(string axis, Vector3 vec, float rot)
        {
            var c = (float)Math.Cos(rot);
            var s = (float)Math.Sin(rot);
            if (axis == "X") return new Vector3(vec.X, vec.Y * c + vec.Z * s, -vec.Y * s + vec.Z * c);
            if (axis == "Y") return new Vector3(vec.X * c - vec.Z * s, vec.Y, vec.X * s + vec.Z * c);
            return new Vector3(vec.X * c + vec.Y * s, -vec.X * s + vec.Y * c, vec.Z);
        }

        public static Vector2 Projection(Vector3 vec, Vector2 center, float viewZ = 500f)
        {
            var k = -viewZ / (vec.Z - viewZ);
            var v = new Vector2(vec.X, vec.Y);
            return v + (k - 1f) * (v - center);
        }
    }
}
