namespace Pylon.ModLoader;

/// <summary>一首已登记的 Mod 曲：槽位、MediaPlayer 登记号与淡入淡出进度。按注册下标密集存放。</summary>
internal struct MusicTrack
{
    public int Slot;
    public int MusicId;
    public float Fade;
    public float Sent;
}

/// <summary>一个音乐盒格子：方块（可带 frameY）或道具。槽位只在本进程内有意义。</summary>
internal struct MusicBoxEntry
{
    public int TileType;
    public int FrameY;
    public int ItemType;
    public int Slot;
}

/// <summary>
/// 音乐表：槽位发号、文件去重、音乐盒登记。状态全是实例字段。
/// 槽位基数取编译期常量：运行时读一次再兜底会让版本变化静默错位。
/// </summary>
internal sealed class MusicTable
{
    // interop 把游戏的 const 转成了非编译期常量，而槽位基数要在算槽位时稳定可用；
    // 这里钉住本版 interop 的值（refs/dump.cs 里 MusicID.Count = 105），Install 时再和运行时比一次。
    internal const int VanillaCount = 105;

    private MusicTrack[] _tracks = new MusicTrack[8];
    private string[] _files = new string[8];
    private int _count;

    private MusicBoxEntry[] _boxes = new MusicBoxEntry[4];
    private int _boxCount;

    private readonly Dictionary<string, int> _byFile = new(StringComparer.Ordinal);

    internal int Base => VanillaCount;

    internal int Count => _count;

    internal int BoxCount => _boxCount;

    internal string PathAt(int index) => _files[index];

    internal ref MusicTrack TrackAt(int index) => ref _tracks[index];

    /// <summary>槽位 → 表下标；不是本表的槽位返回 -1。</summary>
    internal int IndexOf(int slot) =>
        slot >= VanillaCount && slot < VanillaCount + _count ? slot - VanillaCount : -1;

    /// <summary>登记一首曲子；找不到文件返回 0（与旧 API 的"0 = 没有"一致）。</summary>
    internal int Register(Mod? mod, string path)
    {
        var file = mod is null ? AudioFiles.Anywhere(path) : AudioFiles.In(mod, path);
        if (file is null)
        {
            Safe.Once("music:" + path,
                $"music: 找不到 '{path}'（Assets/Music 下的 .ogg/.wav/.mp3）", true);
            return 0;
        }

        if (_byFile.TryGetValue(file, out var known)) return VanillaCount + known;

        var index = _count;
        EnsureTracks(index + 1);

        var musicId = AndroidAudio.RegisterMusic(file);
        // 提前 prepareAsync：MediaPlayer 的等待不能落在第一次被选中那一帧上。
        AndroidAudio.PrimeMusic(musicId);

        ref var track = ref _tracks[index];
        track.Slot = VanillaCount + index;
        track.MusicId = musicId;
        track.Fade = 0f;
        track.Sent = -1f;

        _files[index] = file;
        _count = index + 1;
        _byFile[file] = index;
        return track.Slot;
    }

    internal bool Exists(Mod? mod, string path) =>
        (mod is null ? AudioFiles.Anywhere(path) : AudioFiles.In(mod, path)) is not null;

    /// <summary>登记音乐盒；参数非法直接抛（作者编程错误，早失败比静默不响好）。</summary>
    internal void AddBox(int slot, int itemType, int tileType, int frameY)
    {
        if (IndexOf(slot) < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(slot),
                $"音乐盒要已注册的 Mod 曲槽位（>= {VanillaCount}），收到 {slot}");
        }

        var tileBase = ContentKindTable.BaseCount(ContentKind.Tile);
        if (tileType < tileBase)
        {
            throw new ArgumentOutOfRangeException(nameof(tileType),
                $"音乐盒要 Mod 方块（>= {tileBase}），收到 {tileType}");
        }

        var normalized = frameY - frameY % 36;
        EnsureBoxes(_boxCount + 1);

        // 同一格重复登记：后写覆盖先写（作者重载配置时不该出现两份互相打架的映射）。
        for (var i = 0; i < _boxCount; i++)
        {
            ref var existing = ref _boxes[i];
            if (existing.TileType != tileType || existing.FrameY != normalized) continue;
            existing.Slot = slot;
            if (itemType > 0) existing.ItemType = itemType;
            return;
        }

        _boxes[_boxCount++] = new MusicBoxEntry
        {
            TileType = tileType,
            FrameY = normalized,
            ItemType = itemType > 0 ? itemType : 0,
            Slot = slot,
        };
    }

    internal int SlotForTile(int tileType, int frameY)
    {
        var normalized = frameY - frameY % 36;
        for (var i = 0; i < _boxCount; i++)
        {
            ref var box = ref _boxes[i];
            if (box.TileType == tileType && box.FrameY == normalized) return box.Slot;
        }
        return 0;
    }

    internal int SlotForItem(int itemType)
    {
        if (itemType <= 0) return 0;
        for (var i = 0; i < _boxCount; i++)
        {
            ref var box = ref _boxes[i];
            if (box.ItemType == itemType) return box.Slot;
        }
        return 0;
    }

    private void EnsureTracks(int size)
    {
        if (size <= _tracks.Length) return;

        var grown = new MusicTrack[Math.Max(size, _tracks.Length * 2)];
        Array.Copy(_tracks, grown, _tracks.Length);
        _tracks = grown;

        var files = new string[grown.Length];
        Array.Copy(_files, files, _files.Length);
        _files = files;
    }

    private void EnsureBoxes(int size)
    {
        if (size <= _boxes.Length) return;

        var grown = new MusicBoxEntry[Math.Max(size, _boxes.Length * 2)];
        Array.Copy(_boxes, grown, _boxes.Length);
        _boxes = grown;
    }
}
