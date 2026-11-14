using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Pylon.ModLoader.Tables;
using Terraria;
using Terraria.Localization;
using Terraria.Map;
using Color = Il2CppMicrosoft.Xna.Framework.Graphics.Color;

namespace Pylon.ModLoader;

/// <summary>
/// mod 方块的地图面：AddMapEntry 写进 colorLookup/tileLookup/tileOptionCounts 与图例；
/// 分块地图存读（块内残留 mod 色号会让无 mod 端读越界，压缩前摘格，存档进侧车、读档恢复）。
/// </summary>
internal static unsafe class ModTileMap
{
    private const int ChunkShift = 6; // WorldMapChunk.MapChunkSize = 64
    private const int ChunkSize = 1 << ChunkShift;
    private const int ChunkCells = ChunkSize * ChunkSize;
    private const string WallKeyPrefix = "wall:";

    private readonly record struct Entry(Color Color, LocalizedText? Name);

    private sealed class TypedEntries
    {
        public readonly List<Entry> List = [];
        public int Base; // 第一个色号（0 = 没有条目）
    }

    // 条目表：键 = 类型；Apply 时写回第一个色号，侧车恢复用它折色号。
    private static Dictionary<int, TypedEntries> _tiles = [];
    private static Dictionary<int, TypedEntries> _walls = [];

    // 展平的色号索引（pos + k → 类型/选项），侧车键与恢复共用。
    private static List<int> _indexType = [];
    private static List<byte> _indexOption = [];
    private static List<bool> _indexWall = [];

    private static IntPtr _colorsOurs; // 我们写出的 colorLookup；指针变了 = 游戏重建了表
    private static bool _appliedOnce;

    // 正在读 .map 的线程：期间压缩出的 mod 色号是旧文件的泄漏，摘掉当未探索。
    private static bool _loading;
    private static int _loadingThread;

    // 后台压缩暂存的 mod 格：块号 (X << 16 | Y) → (格内下标, 格子)，块回载时归还。
    private static readonly Dictionary<uint, List<(ushort K, MapTile Cell)>> _stash = [];

    // _stash/_marks/_saveCells/索引表的跨线程触点共用一把门（存档线程 + 主线程 + 后台块卸载/回载线程）。
    // 锁只包集合操作，不跨 orig/interop 调用。
    private static readonly object Gate = new();

    // 进行中的存档（只在存档线程动）：被压缩摘出的 mod 格，写侧车用。
    private static bool _saving;
    private static int _savingThread;
    private static ushort _savePos;
    private static WorldMap? _saveMap;
    private static List<(int X, int Y, MapTile Cell)> _saveCells = [];

    // 可能含 mod 格的块号（块 Y << 32 | 块 X）：存档只扫这些，不解压全图。
    private static readonly HashSet<long> _marks = [];
    private static bool _scanAll; // 块宽还没读到时兜底全扫

    // mod 缺席的等待行：读档时进不来（类型未知），存档时原样回写。
    private static List<TagCompound> _kept = [];

    private static int _chunkWidth;

    private static IModLog Log => LoaderHost.Current.Log;

    /// <summary>TileTable.Build 之后调用（墙壁安装先于方块安装，两侧条目都已就绪）。</summary>
    internal static void Build(IModLog log)
    {
        if (_installed) return;
        _installed = true;

        CollectEntries();
        InstallHooks();
        Apply(log);
    }

    private static bool _installed;

    private static void CollectEntries()
    {
        var tiles = new Dictionary<int, TypedEntries>();
        foreach (var tile in ModContent.GetContent<ModTile>())
        {
            var type = tile.Type;
            if (type < VanillaCounts.Tile || tile.MapEntries.Count <= 0) continue;

            var list = new TypedEntries();
            foreach (var entry in tile.MapEntries) list.List.Add(new Entry(entry.Color, entry.Name));
            tiles[type] = list;
        }
        _tiles = tiles;

        var walls = new Dictionary<int, TypedEntries>();
        foreach (var wall in ModContent.GetContent<ModWall>())
        {
            var type = wall.Type;
            if (type < VanillaCounts.Wall || wall.MapEntries.Count <= 0) continue;

            var list = new TypedEntries();
            foreach (var entry in wall.MapEntries) list.List.Add(new Entry(entry.Color, entry.Name));
            walls[type] = list;
        }
        _walls = walls;
    }

    private static void InstallHooks()
    {
        // Initialize 是 P2-5 的窗口：游戏在这里重建表，之后立即扩表重放。
        On.Terraria.Map.MapHelper.Initialize += Initialize;

        // 块压缩是唯一能把 mod 色号带进 .map 的路：摘格挂压缩，恢复挂读档与块回载。
        On.Terraria.Map.MapHelper.CreateMapTile += CreateMapTile;
        On.Terraria.Map.WorldMap.Load += MapLoad;
        On.Terraria.Map.WorldMapChunk.SaveCompressed += ChunkSaveCompressed;
        On.Terraria.Map.WorldMapChunk.LoadTiles += ChunkLoadTiles;
        On.Terraria.Map.MapHelper.InternalSaveMapCompressed += InternalSaveMapCompressed;
    }

    private static void Initialize(On.Terraria.Map.MapHelper.orig_Initialize orig)
    {
        orig();
        _colorsOurs = IntPtr.Zero;
        Apply(Log);
    }

    /// <summary>60 帧维护：colorLookup 指针变了 = 游戏重建了表，重放。</summary>
    internal static void Maintain()
    {
        if (!_installed) return;
        var colors = MapHelper.colorLookup;
        if (colors is null || colors.Pointer == _colorsOurs) return;
        Apply(Log);
    }

    // ===================== 条目写表 =====================

    /// <summary>表没就绪就静默等下一次（Initialize 钩 / Maintain）。</summary>
    private static void Apply(IModLog log)
    {
        try
        {
            ApplyInner(log);
        }
        catch (Exception e)
        {
            Safe.Once("map.apply", "[map] 条目写表失败：" + Safe.Describe(e), true);
        }
    }

    private static void ApplyInner(IModLog log)
    {
        var lookup = MapHelper.tileLookup;
        var options = MapHelper.tileOptionCounts;
        var colors = MapHelper.colorLookup;
        if (lookup is null || options is null || colors is null) return;

        var hell = MapHelper.hellPosition;
        if (hell <= 1 || colors.Length != hell + 1) return; // 图例没攒齐 = 游戏还没 Initialize

        var total = VanillaCounts.Tile + ModContent.Count(ContentKind.Tile);
        var wallTotal = VanillaCounts.Wall + ModContent.Count(ContentKind.Wall);

        // Initialize 后表可能回到原版长度：就地扩，别等看门（头两秒 CreateMapTile 读 tileLookup[mod] 越界）。
        if (lookup.Length < total)
        {
            var grown = ArrayGrow.Grow(lookup.Pointer, total, fillWithMode: true);
            if (grown == IntPtr.Zero) return;
            lookup = new Il2CppStructArray<ushort>(grown);
            MapHelper.tileLookup = lookup;
        }

        if (options.Length < total)
        {
            var grown = ArrayGrow.Grow(options.Pointer, total, fillWithMode: true);
            if (grown == IntPtr.Zero) return;
            options = new Il2CppStructArray<int>(grown);
            MapHelper.tileOptionCounts = options;
        }

        var wallLookup = MapHelper.wallLookup;
        var wallOptions = MapHelper.wallOptionCounts;
        if (wallTotal > VanillaCounts.Wall)
        {
            if (wallLookup is null || wallLookup.Length < wallTotal)
            {
                var at = wallLookup?.Pointer ?? IntPtr.Zero;
                var grown = ArrayGrow.Grow(at, wallTotal, fillWithMode: true);
                if (grown == IntPtr.Zero) return;
                wallLookup = new Il2CppStructArray<ushort>(grown);
                MapHelper.wallLookup = wallLookup;
            }

            if (wallOptions is null || wallOptions.Length < wallTotal)
            {
                var at = wallOptions?.Pointer ?? IntPtr.Zero;
                var grown = ArrayGrow.Grow(at, wallTotal, fillWithMode: true);
                if (grown == IntPtr.Zero) return;
                wallOptions = new Il2CppStructArray<int>(grown);
                MapHelper.wallOptionCounts = wallOptions;
            }
        }

        var pos = hell + 1; // mod 色号起点：hellPosition 已是原版最后一个颜色
        if (pos + 1 >= 0xFFFF) return;

        // 先在局部建完再原子换入：存档线程的快照要么看旧表要么看新表，不见半张。
        var indexType = new List<int>();
        var indexOption = new List<byte>();
        var indexWall = new List<bool>();

        WriteTypeEntries(lookup, options, _tiles, VanillaCounts.Tile, total, pos, isWall: false,
                         indexType, indexOption, indexWall);
        if (wallLookup is not null && wallOptions is not null)
            WriteTypeEntries(wallLookup, wallOptions, _walls, VanillaCounts.Wall, wallTotal, pos, isWall: true,
                             indexType, indexOption, indexWall);

        lock (Gate)
        {
            _indexType = indexType;
            _indexOption = indexOption;
            _indexWall = indexWall;
        }

        var count = indexType.Count;

        var fresh = new Il2CppStructArray<Color>(pos + count);
        unsafe
        {
            var copy = Math.Min(colors.Length, pos + count) * sizeof(Color);
            Buffer.MemoryCopy(ArrayGrow.Data(colors.Pointer), ArrayGrow.Data(fresh.Pointer),
                (long)(pos + count) * sizeof(Color), copy);

            var at = ArrayGrow.Data(fresh.Pointer);
            for (var k = 0; k < count; k++)
                *(Color*)(at + (pos + k) * sizeof(Color)) = EntryAt(k).Color;
        }

        MapHelper.colorLookup = fresh;
        _colorsOurs = fresh.Pointer;

        ApplyLegend(pos, count);
        _appliedOnce = true;

        if (count > 0) log.Info($"[map] 条目已写（mod 色 {count} 条，从 {pos} 起）");
    }

    /// <summary>有色号记 base 与条数，没有就归零（众数填充会把 mod 方块当泥土色）。</summary>
    private static void WriteTypeEntries(Il2CppStructArray<ushort> lookup, Il2CppStructArray<int> options,
                                         Dictionary<int, TypedEntries> source, int first, int total, int pos,
                                         bool isWall, List<int> indexType, List<byte> indexOption,
                                         List<bool> indexWall)
    {
        for (var type = first; type < total && type < lookup.Length; type++)
        {
            var count = 0;
            TypedEntries? typed = null;
            if (source.TryGetValue(type, out var found))
            {
                typed = found;
                count = found.List.Count;
            }

            if (typed is null || count <= 0 || pos + indexType.Count + count > 0xFFFF)
            {
                lookup[type] = 0;
                options[type] = 0;
                continue;
            }

            typed.Base = pos + indexType.Count;
            lookup[type] = (ushort)typed.Base;
            options[type] = count;

            for (var o = 0; o < count; o++)
            {
                indexType.Add(type);
                indexOption.Add((byte)o);
                indexWall.Add(isWall);
            }
        }
    }

    private static Entry EntryAt(int k)
    {
        var type = _indexType[k];
        var option = _indexOption[k];
        var source = _indexWall[k] ? _walls : _tiles;
        return source.TryGetValue(type, out var typed) && option < typed.List.Count
            ? typed.List[option]
            : new Entry(new Color(255, 0, 255), null);
    }

    private static void ApplyLegend(int pos, int count)
    {
        var legend = Lang._mapLegendCache;
        if (legend is null || legend.Length >= pos + count) return;

        var fresh = new Il2CppReferenceArray<LocalizedText>(pos + count);
        for (var i = 0; i < legend.Length; i++) fresh[i] = legend[i];

        for (var k = 0; k < count; k++) fresh[pos + k] = EntryAt(k).Name ?? Language.GetText("MapObject.Unknown");

        Lang._mapLegendCache = fresh;
    }

    // ===================== 分块存读 =====================

    private static ushort ModPosition
    {
        get
        {
            var hell = MapHelper.hellPosition;
            return hell <= 1 ? (ushort)0 : (ushort)(hell + 1);
        }
    }

    private static MapTile CreateMapTile(On.Terraria.Map.MapHelper.orig_CreateMapTile orig, int i, int j,
                                         byte light)
    {
        var tile = orig(i, j, light);
        var pos = ModPosition;
        if (pos != 0 && tile.Type >= pos) MarkChunk(i, j);
        return tile;
    }

    private static void MarkChunk(int x, int y)
    {
        if (_chunkWidth <= 0 || x < 0 || y < 0)
        {
            _scanAll = true;
            return;
        }

        lock (Gate) _marks.Add(((long)(y >> ChunkShift) << 32) | (uint)(x >> ChunkShift));
    }

    /// <summary>存档前把含 mod 格的块标脏逼游戏重压缩——否则早已压缩的块把旧色号直接带进文件。</summary>
    private static void PrepareSave(WorldMap map, ushort pos)
    {
        var width = map.MaxChunkWidth;
        var maxW = map.MaxWidth;
        var maxH = map.MaxHeight;
        var height = (maxH + ChunkSize - 1) >> ChunkShift;
        _chunkWidth = width;

        for (var cy = 0; cy < height; cy++)
        {
            for (var cx = 0; cx < width; cx++)
            {
                var key = ((long)cy << 32) | (uint)cx;
                if (!_scanAll)
                {
                    lock (Gate)
                    {
                        if (!_marks.Contains(key)) continue;
                    }
                }

                var chunk = GetChunk(map, cx << ChunkShift, cy << ChunkShift);
                if (chunk is null) continue;

                if (AnyModCell(chunk, pos)) chunk.dirty = true;
                else { lock (Gate) _marks.Remove(key); }
            }
        }

        _scanAll = false;
    }

    private static WorldMapChunk? GetChunk(WorldMap? map, int x, int y)
    {
        if (map is null || x < 0 || y < 0 || x >= map.MaxWidth || y >= map.MaxHeight) return null;
        var cell = map.GetChunkTile(x, y, out var chunk);
        return cell != null ? chunk : null;
    }

    private static bool AnyModCell(WorldMapChunk chunk, ushort pos)
    {
        var data = chunk.TileData;
        if (data == null) return false;

        for (var k = 0; k < ChunkCells; k++)
        {
            if (data[k].Type >= pos) return true;
        }
        return false;
    }

    /// <summary>
    /// 压缩前摘出 mod 格（文件里从此没有 mod 色号），压缩完还回内存视图。
    /// 存档期收集进存档清单，后台卸载进暂存，读档期丢弃（旧文件的泄漏）。
    /// </summary>
    private static void ChunkSaveCompressed(On.Terraria.Map.WorldMapChunk.orig_SaveCompressed orig,
                                            WorldMapChunk chunk)
    {
        var pos = _saving && _savingThread == Thread.CurrentThread.ManagedThreadId
            ? _savePos
            : ModPosition;
        var loading = _loading && _loadingThread == Thread.CurrentThread.ManagedThreadId;
        if (loading && pos == 0) pos = ModPosition;

        var data = chunk is not null && pos != 0 ? chunk.TileData : null;
        if (chunk is null || data == null)
        {
            orig(chunk!);
            return;
        }

        var cx = chunk.X;
        var cy = chunk.Y;
        var cells = new List<(ushort K, MapTile Cell)>();
        for (var k = 0; k < ChunkCells; k++)
        {
            if (data[k].Type < pos) continue;
            cells.Add(((ushort)k, data[k]));
            data[k] = default;
        }

        orig(chunk);

        if (loading) return;

        var key = ((uint)cx << 16) | ((uint)cy & 0xFFFFu);
        lock (Gate)
        {
            if (_saving || cells.Count == 0) _stash.Remove(key);
            else _stash[key] = cells;
        }

        if (cells.Count == 0) return;

        var x0 = cx << ChunkShift;
        var y0 = cy << ChunkShift;
        foreach (var (k, cell) in cells)
        {
            data[k] = cell;
            if (_saving)
            {
                lock (Gate) _saveCells.Add((x0 + (k & (ChunkSize - 1)), y0 + (k >> ChunkShift), cell));
            }
        }

        // 内存视图还留着 mod 格：标脏，下次压缩再摘。
        chunk.dirty = true;
    }

    /// <summary>块回载：暂存的 mod 格归位。</summary>
    private static void ChunkLoadTiles(On.Terraria.Map.WorldMapChunk.orig_LoadTiles orig,
                                       WorldMapChunk chunk, bool utilLoad)
    {
        orig(chunk, utilLoad);

        if (chunk is null) return;

        var key = ((uint)chunk.X << 16) | ((uint)chunk.Y & 0xFFFFu);
        List<(ushort K, MapTile Cell)>? cells;
        lock (Gate)
        {
            if (_stash.Count == 0 || !_stash.Remove(key, out cells)) return;
        }

        var data = chunk.TileData;
        if (data == null) return;

        foreach (var (k, cell) in cells) data[k] = cell;
        chunk.dirty = true;
        MarkChunk(chunk.X << ChunkShift, chunk.Y << ChunkShift);
    }

    /// <summary>读档：清状态 → 原版读 .map → 按侧车恢复 mod 格。</summary>
    private static void MapLoad(On.Terraria.Map.WorldMap.orig_Load orig, WorldMap self)
    {
        lock (Gate)
        {
            _marks.Clear();
            _stash.Clear();
        }
        _scanAll = false;
        _kept = [];

        _chunkWidth = self?.MaxChunkWidth ?? 0;
        _loadingThread = Thread.CurrentThread.ManagedThreadId;
        _loading = true;
        try
        {
            orig(self!);
        }
        finally
        {
            _loading = false;
        }

        if (self is null) return;
        _chunkWidth = self.MaxChunkWidth;

        var file = MapSidecarFile();
        if (file is null || !File.Exists(file)) return;

        var pos = ModPosition;
        if (pos == 0) return;
        lock (Gate)
        {
            if (_indexType.Count == 0) return;
        }

        var restored = 0;
        var skipped = 0;
        var kept = new List<TagCompound>();

        // 恢复窗口全程持游戏读档的同一把锁。
        var lockObject = self.LockObject;
        if (lockObject is not null) IL2CPP.il2cpp_monitor_enter(lockObject.Pointer);
        try
        {
            foreach (var entry in TagJson.Read(file).GetList("cells"))
            {
                if (entry is not TagCompound row) continue;

                try
                {
                    if (!ResolveKey(row.GetString("k"), out var isWall, out var type))
                    {
                        kept.Add(row);
                        continue;
                    }

                    var x = row.GetInt("x");
                    var y = row.GetInt("y");
                    var option = row.GetInt("o");
                    var typed = (isWall ? _walls : _tiles).GetValueOrDefault(type);
                    // mod 缺席或条目数变了：位置还黑着就等下一轮。
                    if (typed is null || typed.Base <= 0 || option >= typed.List.Count)
                    {
                        kept.Add(row);
                        continue;
                    }

                    var chunk = GetChunk(self, x, y);
                    if (chunk is null) continue;

                    var data = chunk.TileData;
                    if (data == null) continue;

                    var index = (y & (ChunkSize - 1)) * ChunkSize + (x & (ChunkSize - 1));
                    var cell = data[index];
                    if (cell.Type != 0 || cell.Light != 0)
                    {
                        skipped++; // 已探索的格不信侧车
                        continue;
                    }

                    data[index] = new MapTile
                    {
                        Type = (ushort)(typed.Base + option),
                        Light = (byte)row.GetInt("l"),
                        _extraData = (byte)row.GetInt("e"),
                    };
                    chunk.dirty = true;
                    MarkChunk(x, y);
                    restored++;
                }
                catch (Exception e)
                {
                    ContentErrors.Report("[map] 侧车行恢复失败：" + Safe.Describe(e));
                }
            }
        }
        finally
        {
            if (lockObject is not null) IL2CPP.il2cpp_monitor_exit(lockObject.Pointer);
        }

        _kept = kept;
        if (restored > 0 || kept.Count > 0)
            Log.Info($"[map] 侧车恢复 {restored} 格（等待 {_kept.Count}，已探索跳过 {skipped}）");
    }

    /// <summary>"wall:Mod/Wall" → (是墙, 类型)；普通键按 mod 方块找。</summary>
    private static bool ResolveKey(string key, out bool isWall, out int type)
    {
        isWall = false;
        type = 0;
        if (string.IsNullOrEmpty(key)) return false;

        if (key.StartsWith(WallKeyPrefix, StringComparison.Ordinal))
        {
            isWall = true;
            type = ModContent.TryFind<ModWall>(key[WallKeyPrefix.Length..], out var wall) ? wall.Type : 0;
            return type >= VanillaCounts.Wall;
        }

        type = ModContent.TryFind<ModTile>(key, out var tile) ? tile.Type : 0;
        return type >= VanillaCounts.Tile;
    }

    // ===================== 存档 =====================

    /// <summary>
    /// 存档窗口：前置扫块标脏，orig 里逐块压缩摘格，finally 写侧车——原版中途抛异常也不丢
    /// （try/finally 一步到位）。
    /// </summary>
    private static void InternalSaveMapCompressed(
        On.Terraria.Map.MapHelper.orig_InternalSaveMapCompressed orig)
    {
        var map = Main.Map;
        var pos = ModPosition;
        if (map is null || pos == 0 || !_appliedOnce || _indexType.Count == 0)
        {
            orig();
            return;
        }

        PrepareSave(map, pos);
        _savingThread = Thread.CurrentThread.ManagedThreadId;
        _saving = true;
        _savePos = pos;
        _saveMap = map;
        _saveCells = [];
        var saved = false;
        try
        {
            orig();
            saved = true;
        }
        finally
        {
            FinishSave(saved);
        }
    }

    private static void FinishSave(bool saved)
    {
        if (!_saving) return;
        _saving = false;

        try
        {
            if (!saved)
            {
                Log.Warn("[map] 存档中途失败，侧车保持原样");
                return;
            }

            // 暂存里被提前压缩的块也是这次的 mod 格，合并进来。
            lock (Gate)
            {
                foreach (var pair in _stash)
                {
                    var x0 = (int)(pair.Key >> 16) << ChunkShift;
                    var y0 = (int)(pair.Key & 0xFFFFu) << ChunkShift;
                    foreach (var (k, cell) in pair.Value)
                        _saveCells.Add((x0 + (k & (ChunkSize - 1)), y0 + (k >> ChunkShift), cell));
                }
            }

            var file = MapSidecarFile();
            if (file is null) return;

            // 等待行：位置还黑着才继续等（被探索过就作废）。
            var kept = new List<TagCompound>();
            foreach (var row in _kept)
            {
                var x = row.GetInt("x");
                var y = row.GetInt("y");
                var index = (y & (ChunkSize - 1)) * ChunkSize + (x & (ChunkSize - 1));
                var data = GetChunk(_saveMap, x, y)?.TileData;
                var cell = data != null ? data[index] : default;
                if (cell.Type == 0 && cell.Light == 0) kept.Add(row);
            }

            var rows = new List<object?>(_saveCells.Count + kept.Count);
            List<byte> options;
            List<string> keys;
            lock (Gate)
            {
                keys = BuildKeys();
                options = _indexOption;
            }
            foreach (var (x, y, cell) in _saveCells)
            {
                var k = cell.Type - _savePos;
                if (k < 0 || k >= keys.Count) continue;
                rows.Add(new TagCompound
                {
                    ["x"] = x,
                    ["y"] = y,
                    ["l"] = (int)cell.Light,
                    ["e"] = (int)cell._extraData,
                    ["o"] = (int)options[k],
                    ["k"] = keys[k],
                });
            }
            rows.AddRange(kept);

            var root = TagJson.Read(file);
            if (rows.Count > 0) root.Set("cells", rows);
            else root.Remove("cells");

            if (root.Count > 0) TagJson.Write(file, root);
            else if (File.Exists(file)) File.Delete(file);

            if (rows.Count > 0) Log.Info($"[map] 侧车写入 {rows.Count} 格");
        }
        catch (Exception e)
        {
            ContentErrors.Report("[map] 侧车写入失败：" + Safe.Describe(e));
        }
        finally
        {
            _saveCells = [];
            lock (Gate) _stash.Clear();
        }
    }

    private static List<string> BuildKeys()
    {
        var keys = new List<string>(_indexType.Count);
        for (var k = 0; k < _indexType.Count; k++)
        {
            var type = _indexType[k];
            if (_indexWall[k])
            {
                var wall = WallTable.Of(type);
                keys.Add(wall is null ? "" : WallKeyPrefix + wall.FullKey);
            }
            else
            {
                var tile = TileTable.Of(type);
                keys.Add(tile is null ? "" : tile.FullKey);
            }
        }
        return keys;
    }

    private static string? MapSidecarFile()
    {
        var player = Main.ActivePlayerFileData;
        var world = Main.ActiveWorldFileData;
        if (player is null || world is null) return null;

        // 教程人物没有 _path，TryGetMapPath 会空引用。
        if (string.IsNullOrEmpty(player._path)) return null;
        if (!WorldMap.TryGetMapPath(player, world, out var path) || string.IsNullOrEmpty(path)) return null;

        return path + ".json";
    }
}
