using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader.Pipelines;
using ReLogic.Utilities;
using Terraria.Audio;

namespace Pylon.ModLoader.Audio;
/// <summary>放游戏或 Mod 声音的静态入口：Mod 声音走 Sound 管线，原生音效请全限定调游戏的 SoundEngine。</summary>
public static class SoundEngine
{
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

    public static int FindActiveSound(SoundStyle style) => SoundPipeline.Instance.FindActive(style);

    public static int GetSoundState(int handle) => SoundPipeline.Instance.State(handle);

    public static void StopSound(int handle) => SoundPipeline.Instance.StopHandle(handle);
}
