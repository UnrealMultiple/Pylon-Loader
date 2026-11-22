using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMicrosoft.Xna.Framework;

namespace Pylon.ModLoader.Content;
/// <summary>Collision.TML.cs 的等价扩展成员：托管数组的激光扫描（interop 数组桥）。</summary>
public static class CollisionTml
{
    extension(Terraria.Collision)
    {
        /// <summary>
        /// 沿射线按步采样落点距离，结果写回托管数组（tML <c>Collision.LaserScan</c> 形状；samples[i] = 第 i 步的距离）。
        /// <para>注意：游戏自带 <c>Collision.LaserScan(Vector2, Vector2, float, float, Il2CppStructArray&lt;float&gt;)</c>，
        /// 而生成层有 float[] → Il2CppStructArray 的隐式转换，所以传 float[] 的调用会优先绑到游戏方法上——
        /// 数组被拷进去、结果拷不出来（静默丢结果）。要走本包装（结果写回托管数组），显式写
        /// <c>CollisionTml.LaserScan(...)</c>，或自己持有 Il2CppStructArray&lt;float&gt; 调游戏方法后 AsSpan 读回。</para>
        /// </summary>
        public static void LaserScan(Vector2 samplingPoint, Vector2 directionUnit, float samplingWidth, float maxDistance, float[] samples)
        {
            if (samples is null) throw new ArgumentNullException(nameof(samples));

            var buffer = new Il2CppStructArray<float>(samples.Length);
            Terraria.Collision.LaserScan(samplingPoint, directionUnit, samplingWidth, maxDistance, buffer);
            buffer.AsSpan().CopyTo(samples);
        }
    }
}
