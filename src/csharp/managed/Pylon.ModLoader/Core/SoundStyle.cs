using Terraria.Audio;

namespace Pylon.ModLoader;

/// <summary>
/// Mod 音效：可以隐式转换成游戏的 <see cref="LegacySoundStyle"/>，直接放进
/// UseSound / HitSound / DeathSound。路径从 Assets/ 开始、不带扩展名。
/// </summary>
public sealed class SoundStyle
{
    /// <summary>Mod 声音标记的 SoundId 命名空间：LegacySoundPlayer 的 hook 靠它认出 Mod 声音。</summary>
    private const int ModSoundId = 1000;

    /// <summary>构造选项。</summary>
    public sealed class Options
    {
        /// <summary>音量 0~1。</summary>
        public float Volume { get; set; } = 1f;

        /// <summary>音高，八度为单位（1 = 高一个八度）。</summary>
        public float Pitch { get; set; }

        /// <summary>每次播放的随机音高抖动（八度）；0.2 = 上下最多 0.1。</summary>
        public float PitchVariance { get; set; }

        /// <summary>同时播放上限；0 = 不限。</summary>
        public int MaxInstances { get; set; } = 1;

        /// <summary>超限行为：0 = IgnoreNew，1 = ReplaceOldest。</summary>
        public int SoundLimitBehavior { get; set; } = 1;
    }

    public SoundStyle(string path, Options? options = null)
    {
        var settings = options ?? new Options();
        Path = path;
        Volume = settings.Volume;
        Pitch = settings.Pitch;
        PitchVariance = settings.PitchVariance;
        MaxInstances = settings.MaxInstances;
        SoundLimitBehavior = settings.SoundLimitBehavior;
        Style = SoundLoader.Register(this);
    }

    public string Path { get; }

    public float Volume { get; }

    public float Pitch { get; }

    public float PitchVariance { get; }

    public int MaxInstances { get; }

    public int SoundLimitBehavior { get; }

    /// <summary>声音管线表里的下标；注册时由管线填写。</summary>
    internal int Style { get; set; }

    /// <summary>直接当游戏的 LegacySoundStyle 用。</summary>
    public static implicit operator LegacySoundStyle(SoundStyle sound) =>
        new(ModSoundId, sound.Style, SoundType.Sound, 0);
}
