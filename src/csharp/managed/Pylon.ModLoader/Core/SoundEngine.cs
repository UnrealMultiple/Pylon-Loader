using Il2CppMicrosoft.Xna.Framework;
using ReLogic.Utilities;
using Terraria.Audio;

namespace Pylon.ModLoader;

/// <summary>放游戏或 Mod 声音的静态入口：Mod 声音走 Sound 管线，原生音效请全限定调游戏的 SoundEngine。</summary>
public static class SoundEngine
{
    /// <summary>放一个声音；Mod 音效按位置衰减。返回请求句柄（被拒时无效）。</summary>
    public static SlotId PlaySound(in SoundStyle style, Vector2? position = null)
    {
        if (style.IsNative)
        {
            if (style.NativePointer != 0)
            {
                var legacy = new LegacySoundStyle(style.NativePointer);
                if (position is Vector2 at) Terraria.Audio.SoundEngine.PlaySound(legacy, at, 0f, 1f);
                else Terraria.Audio.SoundEngine.PlaySound(legacy, -1, -1, 0f, 1f);
            }
            else if (position is Vector2 at) Terraria.Audio.SoundEngine.PlaySound(style.NativeId, at, 1, 0f);
            else Terraria.Audio.SoundEngine.PlaySound(style.NativeId, -1, -1, 1, 1f, 0f);
            return default;
        }

        var handle = position is Vector2 p ? SoundPipeline.Instance.Play(style, p) : SoundPipeline.Instance.Play(style, null);
        return handle > 0 ? new SlotId((uint)handle) : default;
    }

    /// <summary>这个 Mod 声音现在有几个在响。</summary>
    public static int FindActiveSound(SoundStyle style) => SoundPipeline.Instance.FindActive(style);

    /// <summary>句柄状态：-1 失败、0 结束/未知、1 等待、2 播放、3 暂停、4 取消、5 超时丢弃。</summary>
    public static int GetSoundState(int handle) => SoundPipeline.Instance.State(handle);

    public static void StopSound(int handle) => SoundPipeline.Instance.StopHandle(handle);
}
