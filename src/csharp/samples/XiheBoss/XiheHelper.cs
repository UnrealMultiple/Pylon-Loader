// 数学 / 小工具：逐条照抄 MEAC/Helper.cs 里 Boss 用到的实现（数值口径完全一致）。
using System;
using Il2CppMicrosoft.Xna.Framework;
using Color = Il2CppMicrosoft.Xna.Framework.Graphics.Color;
using Terraria;

namespace XiheBoss
{
    internal static class XiheHelper
    {
        /// <summary>MEAC Helper.LerpValue：value = Lerp(value, b, n)。</summary>
        public static void LerpValue(ref float value, float b, float n) => value = MathHelper.Lerp(value, b, n);

        /// <summary>MEAC Helper.Normalize2：零向量给 (0, 0.001)。</summary>
        public static Vector2 Normalize2(Vector2 v)
        {
            if (v == Vector2.Zero) return new Vector2(0f, 0.001f);
            return Vector2.Normalize(v);
        }

        /// <summary>MEAC Helper.DirectionTo2：零向量给零。</summary>
        public static Vector2 DirectionTo(Vector2 from, Vector2 to)
        {
            if (to - from == Vector2.Zero) return Vector2.Zero;
            return Vector2.Normalize(to - from);
        }

        /// <summary>MEAC Helper.DirectionFrom2（反向）。</summary>
        public static Vector2 DirectionFrom(Vector2 from, Vector2 to)
        {
            if (to - from == Vector2.Zero) return Vector2.Zero;
            return -Vector2.Normalize(to - from);
        }

        /// <summary>MEAC Helper.Vxr：极角转单位向量。</summary>
        public static Vector2 Vxr(float rot) => new Vector2((float)Math.Cos(rot), (float)Math.Sin(rot));

        /// <summary>MEAC Helper.VerticalVec：逆时针 90°。</summary>
        public static Vector2 VerticalVec(Vector2 vec) => new Vector2(0f - vec.Y, vec.X);

        /// <summary>MEAC Helper.HsvToRgb：H 单位是度（可以 &gt; 360）。</summary>
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

        /// <summary>MEAC Helper.RandomString（Boss 名乱码阶段）。</summary>
        public static string RandomString(int length)
        {
            const string str = "abcdefghmnopqrstFGHMNOPQRSTUVWXYZ~!@#$%^&*()_+{}|[]<>;':,./?";
            var sum = "";
            for (var i = 0; i < length; i++) sum += str[Main.rand.Next(str.Length - 1)].ToString();
            return sum;
        }

        /// <summary>MEAC Helper.MoveTo：速度向目标插值。</summary>
        public static void MoveTo(NPC npc, Vector2 targetPos, float speed, float n)
        {
            var targetVec = Normalize2(targetPos - npc.Center) * speed;
            npc.velocity = (npc.velocity * n + targetVec) / (n + 1f);
        }

        /// <summary>MEAC Helper.SpinAI：绕点旋转并让位移等于速度。</summary>
        public static void SpinAI(NPC npc, Vector2 center, float v)
        {
            var oldPos = npc.Center;
            npc.Center = center + RotatedBy(oldPos - center, v);
            npc.velocity = npc.Center - oldPos;
            npc.position -= npc.velocity;
        }

        /// <summary>
        /// 羲和 BGM（Take Flight）是 140 BPM：1 拍 = 60/140 秒 = 25.714 tick，2 拍 = 51.4286 tick。
        /// MEAC 源码写的是 51.5（约 0.14% 偏长），一场 4 分钟的战斗会累计约 0.3 秒——
        /// 前面能卡上、到后面整拍偏掉。这里用歌曲的精确值。
        /// </summary>
        public const float BeatTicks = 51.4285714f;
        public const float HalfBeatTicks = 25.7142857f;

        /// <summary>MEAC Boss 的节拍：BeatTime(n) = n * 2 拍（140 BPM）。</summary>
        public static int BeatTime(int n) => (int)(n * BeatTicks);

        /// <summary>Utils.RotatedBy 的包装（避免每处都写 double 转换）。</summary>
        public static Vector2 RotatedBy(Vector2 v, float radians) =>
            Utils.RotatedBy(v, (double)radians, default(Vector2));

        /// <summary>随机方向旋转：-max..+max（度？不，弧度；MEAC 的 RotateRandom 用弧度）。</summary>
        public static Vector2 RotateRandom(Vector2 v, float maxRadians) =>
            RotatedBy(v, (float)((Main.rand.NextDouble() * 2.0 - 1.0) * maxRadians));

        /// <summary>MEAC Helper.Gaussian：Box–Muller，均值 mu / 标准差 sigma。</summary>
        public static double Gaussian(double mu, double sigma)
        {
            var u1 = 1.0 - Main.rand.NextDouble();
            var u2 = 1.0 - Main.rand.NextDouble();
            var z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
            return mu + sigma * z;
        }

        /// <summary>XNA MathHelper.SmoothStep 口径（重载带 amount 插值）。</summary>
        public static float SmoothStepQ(float from, float to, float amount)
        {
            amount = MathHelper.Clamp(amount, 0f, 1f);
            amount = amount * amount * (3f - 2f * amount);
            return from + (to - from) * amount;
        }


        /// <summary>手机上 Utils 没有 NextBool（tModLoader 才加的），这里按同一个语义补。</summary>
        public static bool RandBool() => Main.rand.Next(2) == 0;

        /// <summary>n 分之一概率（等价 Utils.NextBool(rand, n)）。</summary>
        public static bool RandBool(int n) => n <= 1 || Main.rand.Next(n) == 0;
        /// <summary>Utils.NextFloatDirection = -1 或 +1。</summary>
        public static float NextFloatDirection() => Main.rand.Next(2) == 0 ? -1f : 1f;

        /// <summary>随机单位向量（0..2π）。</summary>
        public static Vector2 NextVector2Unit() =>
            Utils.NextVector2Unit(Main.rand, 0f, (float)(Math.PI * 2.0));

        /// <summary>MEAC 常用的 Utils.NextVector2Unit(Main.rand, 0, 2π)。</summary>
        public static Vector2 NextVector2Unit(float min, float max) => Utils.NextVector2Unit(Main.rand, min, max);

        /// <summary>屏幕中心（世界坐标）。</summary>
        public static Vector2 ScreenCenter =>
            Main.screenPosition + new Vector2(Main.screenWidth, Main.screenHeight) / 2f;

        /// <summary>把 npc.ai 拷出来/写回去（IL2CPP 的定长数组属性要整体回写）。</summary>
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

        /// <summary>手机版 Projectile.localAI 只有 3 格（Float_FixedArray_3），不要碰 [3]。</summary>
        public static float[] ReadLocalAi(Projectile proj)
        {
            var a = proj.localAI;
            return new[] { a[0], a[1], a[2] };
        }

        public static void WriteLocalAi(Projectile proj, float a0, float a1, float a2, float a3)
        {
            var a = proj.localAI;
            a[0] = a0; a[1] = a1; a[2] = a2;
            proj.localAI = a;
        }

        public static Color WithAlpha0(Color c) => new Color(c.R, c.G, c.B, (byte)0);

        /// <summary>MEAC Helper.Vec3RotBy（轴 "X"/"Y"/"Z"）。</summary>
        public static Vector3 Vec3RotBy(string axis, Vector3 vec, float rot)
        {
            var c = (float)Math.Cos(rot);
            var s = (float)Math.Sin(rot);
            if (axis == "X") return new Vector3(vec.X, vec.Y * c + vec.Z * s, -vec.Y * s + vec.Z * c);
            if (axis == "Y") return new Vector3(vec.X * c - vec.Z * s, vec.Y, vec.X * s + vec.Z * c);
            return new Vector3(vec.X * c + vec.Y * s, -vec.X * s + vec.Y * c, vec.Z);
        }

        /// <summary>MEAC Helper.Projection：透视投影到世界屏幕中心。</summary>
        public static Vector2 Projection(Vector3 vec, Vector2 center, float viewZ = 500f)
        {
            var k = -viewZ / (vec.Z - viewZ);
            var v = new Vector2(vec.X, vec.Y);
            return v + (k - 1f) * (v - center);
        }
    }
}





