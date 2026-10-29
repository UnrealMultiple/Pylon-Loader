using Terraria.Audio;

namespace Pylon.ModLoader
{
    /// <summary>Mod 音效：new SoundStyle(path, 选项) 就是一个能放进 UseSound/HitSound/DeathSound 的游戏 LegacySoundStyle（路径从 Assets/ 开始、不带扩展名；SetDefaults 里建不花钱，是管线的缓存）。</summary>
    public sealed class SoundStyle
    {
        /// <summary>构造选项（对应 JS 的 options 对象；缺省值同参考）。</summary>
        public sealed class Options
        {
            /// <summary>音量 0~1，默认 1。</summary>
            public float Volume = 1f;

            /// <summary>音高，八度为单位：1 = 高一倍频，-1 = 低一倍频，默认 0。</summary>
            public float Pitch = 0f;

            /// <summary>每次播放的随机音高抖动（八度），默认 0；0.2 = 上下最多 0.1。</summary>
            public float PitchVariance = 0f;

            /// <summary>这个音效同时最多几个，默认 1；0 = 不限。</summary>
            public int MaxInstances = 1;

            /// <summary>超限行为，默认 1（ReplaceOldest 掐掉最老的；0 = IgnoreNew 不播新的；本版无此枚举，值照抄）。</summary>
            public int SoundLimitBehavior = 1;
        }

        /// <summary>新音效：path 从 Assets/ 开始、不带扩展名（'Sounds/Items/Guns/ExampleGun'）；options 不传 = 全默认（JS 的 option() 写法没翻）。</summary>
        public SoundStyle(string path, Options options = null)
        {
            var o = options ?? new Options();
            Path = path;
            Volume = o.Volume;
            Pitch = o.Pitch;
            PitchVariance = o.PitchVariance;
            MaxInstances = o.MaxInstances;
            SoundLimitBehavior = o.SoundLimitBehavior;
            Style = SoundLoader.Register(this);
        }

        /// <summary>路径（从 Assets/ 开始、不带扩展名）。</summary>
        public string Path { get; }

        /// <summary>音量（0~1）。</summary>
        public float Volume { get; }

        /// <summary>音高（八度）。</summary>
        public float Pitch { get; }

        /// <summary>随机音高抖动（八度）。</summary>
        public float PitchVariance { get; }

        /// <summary>同时播放数（0 = 不限）。</summary>
        public int MaxInstances { get; }

        /// <summary>超限行为（0=IgnoreNew、1=ReplaceOldest）。</summary>
        public int SoundLimitBehavior { get; }

        /// <summary>声音管线表里的下标（对应 JS 的 index/Style）；管线注册时填，默认 0 = 第 1 个 Mod 声音。</summary>
        internal int Style { get; set; } = 0;

        /// <summary>new SoundStyle(...) 可以直接当游戏 LegacySoundStyle 用（UseSound/HitSound/DeathSound 的类型）。</summary>
        public static implicit operator LegacySoundStyle(SoundStyle sound)
        {
            // 标记 = (ID=1000, style, SoundType.Sound, 0)；文件加载、注册、缓存、装 hook 都是声音管线的事
            return new LegacySoundStyle(SoundId, sound.Style, SoundType.Sound, 0);
        }

        // ID = 1000：Mod 声音标记的 SoundId 命名空间（LegacySoundPlayer 的 hook 靠它认出 Mod 声音）
        private const int SoundId = 1000;

        // 没翻（全是管线层的事）：文件解析/加载、同文件+同选项复用同一标记（缓存）、注册表都是桥接层；
        // 文件缺失不抛、静音并记日志（Safe.Once）；Android 播放排队是桥接层（Style 下标由它注册时填）；
        // Install() 装 LegacySoundPlayer hook 是 Harmony 层；ModContent.SoundStyle 快捷在 ModContent.cs（不在本批）。
    }
}