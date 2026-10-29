using System;
using Il2CppMicrosoft.Xna.Framework;
using Terraria.Audio;
using SoundEffectInstance = Il2CppMicrosoft.Xna.Framework.Audio.SoundEffectInstance;

namespace Pylon.ModLoader
{
    /// <summary>放游戏/Mod 声音的静态入口；Mod 声音是异步请求（返回句柄，0 = 被拒）。</summary>
    public static class SoundEngine
    {
        /// <summary>放一个 Mod 声音（SoundStyle）；返回请求句柄，0 = 被拒。</summary>
        public static int PlaySound(SoundStyle style, Vector2 position) => SoundLoader.Play(style, position);

        /// <summary>放一个 Mod 声音，不按距离衰减；返回请求句柄，0 = 被拒。</summary>
        public static int PlaySound(SoundStyle style) => SoundLoader.Play(style, null);

        /// <summary>放一个游戏 SoundID（旧 type 号），在位置上。</summary>
        public static SoundEffectInstance PlaySound(int type, Vector2 position) =>
            Terraria.Audio.SoundEngine.PlaySound(type, (int)Math.Floor(position.X), (int)Math.Floor(position.Y), 1, 1f, 0f);

        /// <summary>放一个游戏 SoundID（旧 type 号），不按距离衰减。</summary>
        public static SoundEffectInstance PlaySound(int type) =>
            Terraria.Audio.SoundEngine.PlaySound(type, -1, -1, 1, 1f, 0f);

        /// <summary>放一个 LegacySoundStyle，在位置上。</summary>
        public static SoundEffectInstance PlaySound(LegacySoundStyle style, Vector2 position) =>
            Terraria.Audio.SoundEngine.PlaySound(style, position, 0f, 1f);

        /// <summary>放一个 LegacySoundStyle，不按距离衰减。</summary>
        public static SoundEffectInstance PlaySound(LegacySoundStyle style) =>
            Terraria.Audio.SoundEngine.PlaySound(style, -1, -1, 0f, 1f);

        /// <summary>这个 Mod 声音现在有几个在响；不是 Mod 声音或没有时 0。</summary>
        public static int FindActiveSound(SoundStyle style) => SoundLoader.FindActive(style);

        /// <summary>句柄状态：-1 失败、0 结束/未知、1 等待、2 播放、3 暂停、4 取消、5 超时丢弃。</summary>
        public static int GetSoundState(int handle) => SoundLoader.State(handle);

        /// <summary>取消这个句柄的请求。</summary>
        public static void StopSound(int handle) => SoundLoader.StopHandle(handle);
    }
}
