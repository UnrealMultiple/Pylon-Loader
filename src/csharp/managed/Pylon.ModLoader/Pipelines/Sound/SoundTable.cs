using System.Globalization;

namespace Pylon.ModLoader;

/// <summary>一个已注册音效的选项与活动流链表头。按注册下标密集存放，取用写 ref，不装箱。</summary>
internal struct SoundEntry
{
    public int SoundId;
    public float Volume;
    public float Pitch;
    public float PitchVariance;
    public int MaxInstances;
    public int LimitBehavior;
    public int Head;
    public int Tail;
    public int Active;
}

/// <summary>一次播放。槽位复用靠代次挡住老句柄，播放路径不分配对象、不进字典。</summary>
internal struct SoundStream
{
    public int StreamId;
    public int Entry;
    public int Generation;
    public long StartedMs;
    public int DurationMs;
    public int Next;
    public int Prev;
    public bool Live;
}

/// <summary>
/// 音效表：注册去重、文件解码缓存、活动流池。状态全是实例字段。
/// 只有这里知道"第几个 SoundStyle 对应哪一格"；播放路径只做下标与位运算。
/// </summary>
internal sealed class SoundTable
{
    /// <summary>句柄低 12 位放槽位，其余放代次；句柄因此永远为正且能挡住复用后的老句柄。</summary>
    private const int SlotBits = 12;
    private const int SlotMask = (1 << SlotBits) - 1;
    private const int MaxStreams = 1 << SlotBits;
    private const int MaxGeneration = (1 << (31 - SlotBits)) - 1;

    private SoundEntry[] _entries = new SoundEntry[8];
    private string[] _paths = new string[8];
    private int _entryCount;

    private SoundStream[] _streams = new SoundStream[32];
    private int _streamCount;
    private int _freeHead = -1;

    private readonly Dictionary<string, int> _byKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _files = new(StringComparer.Ordinal);

    internal int Count => _entryCount;

    internal string PathOf(int index) => _paths[index];

    internal ref SoundEntry Entry(int index) => ref _entries[index];

    internal ref SoundStream Stream(int slot) => ref _streams[slot];

    /// <summary>发号；同一组（路径 + 选项）复用同一格，因此也共用同一个 MaxInstances 计数。</summary>
    internal int Register(SoundStyle style)
    {
        var key = KeyOf(style);
        if (_byKey.TryGetValue(key, out var known)) return known;

        var index = _entryCount;
        EnsureEntries(index + 1);

        var soundId = 0;
        var file = AudioFiles.Anywhere(style.Path);
        if (file is null)
        {
            Safe.Once("sound.missing:" + style.Path,
                $"sound: 找不到 '{style.Path}'（Assets/ 下的 .ogg/.wav/.mp3）", true);
        }
        else
        {
            soundId = Load(file);
            if (soundId <= 0)
            {
                Safe.Once("sound.load:" + file,
                    $"sound: SoundPool 拒绝 {file}（{style.Path}）", true);
            }
        }

        ref var entry = ref _entries[index];
        entry.SoundId = soundId;
        entry.Volume = style.Volume;
        entry.Pitch = style.Pitch;
        entry.PitchVariance = style.PitchVariance;
        entry.MaxInstances = style.MaxInstances > 0 ? style.MaxInstances : 0;
        entry.LimitBehavior = style.SoundLimitBehavior;
        entry.Head = -1;
        entry.Tail = -1;
        entry.Active = 0;

        _paths[index] = style.Path;
        _entryCount = index + 1;
        _byKey[key] = index;
        return index;
    }

    private int Load(string file)
    {
        if (_files.TryGetValue(file, out var known)) return known;
        var id = AndroidAudio.LoadSound(file);
        _files[file] = id;
        return id;
    }

    /// <summary>取一格活动流并挂到条目链表尾；失败（池满）返回 false，调用方负责停掉已开的流。</summary>
    internal bool TryAlloc(int entryIndex, int streamId, int durationMs, out int handle)
    {
        var slot = _freeHead;
        if (slot >= 0)
        {
            _freeHead = _streams[slot].Next;
        }
        else
        {
            if (_streamCount >= MaxStreams)
            {
                handle = 0;
                return false;
            }
            slot = _streamCount++;
            EnsureStreams(_streamCount);
        }

        ref var stream = ref _streams[slot];
        var generation = stream.Generation + 1;
        if (generation > MaxGeneration) generation = 1;

        stream.StreamId = streamId;
        stream.Entry = entryIndex;
        stream.Generation = generation;
        stream.StartedMs = Environment.TickCount64;
        stream.DurationMs = durationMs > 0 ? durationMs : 1;
        stream.Next = -1;
        stream.Prev = -1;
        stream.Live = true;

        Link(slot);
        handle = (generation << SlotBits) | slot;
        return true;
    }

    private void Link(int slot)
    {
        ref var stream = ref _streams[slot];
        ref var entry = ref _entries[stream.Entry];
        stream.Prev = entry.Tail;
        if (entry.Tail >= 0) _streams[entry.Tail].Next = slot;
        else entry.Head = slot;
        entry.Tail = slot;
        entry.Active++;
    }

    internal void Release(int slot)
    {
        ref var stream = ref _streams[slot];
        if (!stream.Live) return;

        ref var entry = ref _entries[stream.Entry];
        if (stream.Prev >= 0) _streams[stream.Prev].Next = stream.Next;
        else entry.Head = stream.Next;
        if (stream.Next >= 0) _streams[stream.Next].Prev = stream.Prev;
        else entry.Tail = stream.Prev;
        if (entry.Active > 0) entry.Active--;

        stream.Live = false;
        stream.StreamId = 0;
        stream.Entry = -1;
        stream.Next = _freeHead;
        stream.Prev = -1;
        _freeHead = slot;
    }

    /// <summary>句柄 → 槽位；代次不符（槽位已被复用）或已结束返回 false。</summary>
    internal bool TryResolve(int handle, out int slot)
    {
        slot = handle & SlotMask;
        if (handle <= 0 || (uint)slot >= (uint)_streamCount) return false;
        ref var stream = ref _streams[slot];
        return stream.Live && stream.Generation == handle >> SlotBits;
    }

    private static string KeyOf(SoundStyle style) =>
        style.Path + "|" +
        style.Volume.ToString("R", CultureInfo.InvariantCulture) + "|" +
        style.Pitch.ToString("R", CultureInfo.InvariantCulture) + "|" +
        style.PitchVariance.ToString("R", CultureInfo.InvariantCulture) + "|" +
        style.MaxInstances.ToString(CultureInfo.InvariantCulture) + "|" +
        style.SoundLimitBehavior.ToString(CultureInfo.InvariantCulture);

    private void EnsureEntries(int size)
    {
        if (size <= _entries.Length) return;

        var grown = new SoundEntry[Math.Max(size, _entries.Length * 2)];
        Array.Copy(_entries, grown, _entries.Length);
        _entries = grown;

        var paths = new string[grown.Length];
        Array.Copy(_paths, paths, _paths.Length);
        _paths = paths;
    }

    private void EnsureStreams(int size)
    {
        if (size <= _streams.Length) return;

        var grown = new SoundStream[Math.Max(size, _streams.Length * 2)];
        Array.Copy(_streams, grown, _streams.Length);
        _streams = grown;
    }
}
