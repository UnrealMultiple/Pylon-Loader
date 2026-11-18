using Terraria.Audio;

namespace Pylon.ModLoader;

/// <summary>
/// Mod 音效：路径从 Assets/ 开始、不带扩展名；可隐式转换成游戏的 <see cref="LegacySoundStyle"/>，
/// 也可隐式来自游戏音效号或 LegacySoundStyle（这时直接交回游戏播放）。
/// </summary>
public sealed class SoundStyle
{
    /// <summary>Mod 声音标记的 SoundId 命名空间：LegacySoundPlayer 的 hook 靠它认出 Mod 声音。管线读同一个常量。</summary>
    internal const int MarkerSoundId = 1000;

    public SoundStyle(string soundPath, SoundType type = SoundType.Sound)
    {
        SoundPath = soundPath;
        Type = type;
        Volume = 1f;
        MaxInstances = 1;
        SoundLimitBehavior = 1;
    }

    public SoundStyle(string soundPath, int numberOfVariants, SoundType type = SoundType.Sound)
        : this(soundPath, 1, numberOfVariants, type)
    {
    }

    public SoundStyle(string soundPath, int variantSuffixesStart, int numberOfVariants, SoundType type = SoundType.Sound)
        : this(soundPath, type)
    {
        if (numberOfVariants > 1)
        {
            var list = new int[numberOfVariants];
            for (var i = 0; i < numberOfVariants; i++) list[i] = variantSuffixesStart + i;
            Variants = list;
        }
    }

    public SoundStyle(string soundPath, ReadOnlySpan<int> variants, SoundType type = SoundType.Sound)
        : this(soundPath, type)
    {
        Variants = variants.IsEmpty ? null : variants.ToArray();
    }

    private SoundStyle(int nativeId, LegacySoundStyle? legacy)
    {
        SoundPath = "";
        NativeId = nativeId;
        NativePointer = legacy?.Pointer ?? 0;
        Volume = 1f;
        MaxInstances = 1;
        SoundLimitBehavior = 1;
    }

    public string SoundPath { get; init; }

    internal string Path => SoundPath;

    public SoundType Type { get; init; }

    public string? Identifier { get; init; }

    private float volume = 1f;

    public float Volume
    {
        get => volume;
        init => volume = Math.Clamp(value, 0f, 1f);
    }

    public float Pitch { get; init; }

    public float PitchVariance { get; init; }

    public (float minPitch, float maxPitch) PitchRange
    {
        get => (Pitch - PitchVariance * 0.5f, Pitch + PitchVariance * 0.5f);
        init
        {
            if (value.maxPitch < value.minPitch) throw new ArgumentException("Min pitch cannot be greater than max pitch.");
            Pitch = (value.minPitch + value.maxPitch) * 0.5f;
            PitchVariance = value.maxPitch - value.minPitch;
        }
    }

    public int MaxInstances { get; init; } = 1;

    public int SoundLimitBehavior { get; init; } = 1;

    public bool IsLooped { get; init; }

    public bool PlayOnlyIfFocused { get; init; }

    public ReadOnlySpan<int> Variants
    {
        get => variants ?? ReadOnlySpan<int>.Empty;
        init => variants = value.IsEmpty ? null : value.ToArray();
    }

    private int[]? variants;

    /// <summary>非零：这条是游戏原生音效号（来自 int 隐式转换），播放直接交给游戏。</summary>
    internal int NativeId { get; }

    /// <summary>非零：这条是游戏 LegacySoundStyle（来自隐式转换），播放直接交给游戏。</summary>
    internal nint NativePointer { get; }

    internal bool IsNative => NativeId != 0 || NativePointer != 0;

    /// <summary>声音管线表里的下标；首次播放时注册（-1 = 还没注册）。</summary>
    internal int Style { get; private set; } = -1;

    internal int EnsureRegistered()
    {
        if (IsNative) return -1;
        if (Style < 0) Style = SoundPipeline.Instance.Register(this);
        return Style;
    }

    public SoundStyle WithVolumeScale(float scale) => Copy(volume: Math.Clamp(Volume * scale, 0f, 1f));

    public SoundStyle WithPitchOffset(float offset) => Copy(pitch: Pitch + offset);

    private SoundStyle Copy(float? volume = null, float? pitch = null) =>
        new(SoundPath, Type)
        {
            Identifier = Identifier,
            IsLooped = IsLooped,
            PlayOnlyIfFocused = PlayOnlyIfFocused,
            Variants = Variants,
            Volume = volume ?? Volume,
            Pitch = pitch ?? Pitch,
            PitchVariance = PitchVariance,
            MaxInstances = MaxInstances,
            SoundLimitBehavior = SoundLimitBehavior,
        };

    public static implicit operator SoundStyle(int type) => new(type, null!);

    public static implicit operator SoundStyle(LegacySoundStyle style) => new(0, style);

    public static implicit operator LegacySoundStyle(SoundStyle sound)
    {
        if (sound.NativePointer != 0) return new(sound.NativePointer);
        return new(MarkerSoundId, sound.EnsureRegistered(), SoundType.Sound, 0);
    }
}
