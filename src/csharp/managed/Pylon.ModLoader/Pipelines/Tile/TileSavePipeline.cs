using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Patching;
using Pylon.ModLoader.Pipeline;
using Pylon.ModLoader.SaveData;
using Pylon.NaviteAPI.Host;
using Terraria;
using Terraria.IO;

namespace Pylon.ModLoader.Pipelines;
/// <summary>世界存档守卫：存档时把静态块换成 mod 定义遮成空气的副本（内核指令补丁 + x27 注入），全图裸指针扫描结果按 FullKey 写侧车 .wld.tiles.json（含作者数据 SaveData/LoadData），读档按行恢复、mod 缺席的行进等待名单；云存档不遮不收集，补丁失败降级为真定义数组遮蔽往返。</summary>
internal static class TileSavePipeline
{
    /// <summary>与内核 PylonTileRecord 逐字段镜像（20 字节，自然对齐）。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Record
    {
        public int X, Y;
        public ushort Type;
        public short FrameX, FrameY;
        public short SHeader;
        public byte B1, B2, B3;
    }

    private const string SaveSection = "tiles";
    private const string WallSection = "walls";

    private const int LoadStaticsOffset = 0x104;
    private const uint LoadStaticsInsn = 0xF9405D08u;
    private const uint MovFromX27Insn = 0xAA1B03E8u;
    private const short ActiveBit = 0x20;

    private const int ClassStaticFieldsOffset = 0xB8;
    private const int LookupSlotOffset = 0x68;
    private const int TypeSlotOffset = 0xA8;
    private const int HeaderSlotOffset = 0xB0;
    private const int FrameXSlotOffset = 0xB8;
    private const int FrameYSlotOffset = 0xC0;
    private const int WallSlotOffset = 0xC8;
    private const int B1SlotOffset = 0xD8;
    private const int B2SlotOffset = 0xE0;
    private const int B3SlotOffset = 0xE8;
    private const int BlockSize = 0xF0;

    /// <summary>TileData 的指针静态（现读）；代理指针属性不可用，一律走槽位偏移直读。</summary>
    private unsafe struct TilePtrs
    {
        public uint* Lookup;
        public ushort* Type;
        public short* SHeader;
        public short* FrameX, FrameY;
        public ushort* Wall;
        public byte* B1, B2, B3;
        public bool Ok;
    }

    private static unsafe TilePtrs ReadPtrs()
    {
        var b = StaticsBlock();
        if (b == IntPtr.Zero) return default;
        TilePtrs p;
        p.Lookup = *(uint**)((byte*)b + LookupSlotOffset);
        p.Type = *(ushort**)((byte*)b + TypeSlotOffset);
        p.SHeader = *(short**)((byte*)b + HeaderSlotOffset);
        p.FrameX = *(short**)((byte*)b + FrameXSlotOffset);
        p.FrameY = *(short**)((byte*)b + FrameYSlotOffset);
        p.Wall = *(ushort**)((byte*)b + WallSlotOffset);
        p.B1 = *(byte**)((byte*)b + B1SlotOffset);
        p.B2 = *(byte**)((byte*)b + B2SlotOffset);
        p.B3 = *(byte**)((byte*)b + B3SlotOffset);
        p.Ok = true;
        return p;
    }

    private static bool _installed;
    private static bool _patched;
    private static bool _wallsReady;

    private static IntPtr _body;
    private static IntPtr _trampoline;

    private static IntPtr _fakeBlock, _typeCopy, _sHeaderCopy, _wallCopy;
    private static int _copyCapacity, _wallCapacity;

    private static Record[] _records = new Record[256];
    private static int _recordCount;

    private struct WallRecord
    {
        public int X, Y;
        public ushort Type;
        public string? Key;
        public byte Color;
        public bool Invisible, Fullbright;
    }

    private static WallRecord[] _wallRecords = new WallRecord[64];
    private static int _wallRecordCount;

    private static ushort[]? _strippedTypes;
    private static short[]? _strippedHeaders;
    private static List<uint>? _strippedDefs;
    private static List<(long Pos, ushort Value)>? _strippedWalls;

    private static List<TagCompound>? _kept;
    private static List<TagCompound>? _keptWalls;

    private static IModLog Log => LoaderHost.Current.Log;

    internal static void Build(IModLog log)
    {
        if (_installed || ModContent.Count(ContentKind.Tile) <= 0) return;
        _installed = true;

        try
        {
            unsafe
            {
                var klass = Il2CppClassPointerStore<WorldFile>.NativeClassPtr;
                if (klass == IntPtr.Zero) throw new InvalidOperationException("WorldFile 类指针未就绪");

                var method = IL2CPP.GetIl2CppMethod(klass, false, "SaveWorldTilesFast", "System.Int32",
                    "System.IO.BinaryWriter");
                _body = method == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(method);

                var block = StaticsBlock();
                if (block == IntPtr.Zero)
                    throw new InvalidOperationException("TileData 静态块未就绪（cctor 没跑？）");

                _wallsReady = ModContent.Count(ContentKind.Wall) > 0;

                if (_body == IntPtr.Zero || *(uint*)((byte*)_body + LoadStaticsOffset) != LoadStaticsInsn)
                    throw new InvalidOperationException(
                        $"SaveWorldTilesFast+{LoadStaticsOffset:x} 指纹不符（解析到假桩或版本变化），不挂钩");

                _fakeBlock = Marshal.AllocHGlobal(BlockSize);

                var detour = (delegate* unmanaged<IntPtr, IntPtr, int>)&SaveTilesDetour;
                if (HookNative.Apply(_body, (IntPtr)detour, out _trampoline) != 0)
                    throw new InvalidOperationException("SaveWorldTilesFast 挂钩失败 rc 非零（见内核日志）");

                _patched = CodeWrite.TryWrite(
                    (IntPtr)((byte*)_body + LoadStaticsOffset), LoadStaticsInsn, MovFromX27Insn, out _);
            }

            On.Terraria.IO.WorldFile.LoadWorldTiles += LoadTiles;
            On.Terraria.WorldGen.clearWorld += ClearWorld;

            log.Info("[tile] 存档守卫已挂（" + (_patched ? "静态块注入" : "降级：真数组遮蔽") +
                     (_wallsReady ? "，含 mod 墙" : "") + "；侧车 .wld.tiles.json）");
        }
        catch (Exception e)
        {
            ContentErrors.Report("[tile] 存档守卫安装失败（mod 方块照旧按号进 .wld）：" + Safe.Describe(e));
        }
    }

    private static unsafe IntPtr StaticsBlock()
    {
        var klass = Il2CppClassPointerStore<TileData>.NativeClassPtr;
        if (klass == IntPtr.Zero) return IntPtr.Zero;
        try { IL2CPP.il2cpp_runtime_class_init(klass); } catch { }
        return *(IntPtr*)((byte*)klass + ClassStaticFieldsOffset);
    }

    [UnmanagedCallersOnly]
    private static int SaveTilesDetour(IntPtr writer, IntPtr method)
    {
        var block = StaticsBlock();
        var stripped = false;
        try
        {
            (block, stripped) = Prepare(block);
        }
        catch
        {
            block = StaticsBlock();
            stripped = false;
        }

        int result;
        try
        {
            result = PatchNative.CallWithStatics(_trampoline, block, writer, method);
        }
        finally
        {
            if (stripped) RestoreReal();
        }
        return result;
    }

    private static (IntPtr Block, bool Strip) Prepare(IntPtr block)
    {
        var worldData = Main.ActiveWorldFileData;
        if (worldData is null || worldData.IsCloudSave) return (block, false);

        unsafe
        {
            var tile = Main.tile;
            if (tile is null || block == IntPtr.Zero) return (block, false);

            var width = tile._width;
            var height = tile._height;
            if (width <= 0 || height <= 0 || (long)width * height >= (1 << 27)) return (block, false);

            var ptrs = ReadPtrs();
            if (!ptrs.Ok || ptrs.Type == null || ptrs.SHeader == null || ptrs.Lookup == null)
                return (block, false);

            ScanModTiles(width, height, ptrs);
            WriteSidecar(ptrs);

            if (_recordCount == 0 && _wallRecordCount == 0) return (block, false);
            if (_patched) return (BuildFakeBlock(width, height), false);

            StripReal(width, height);
            return (block, true);
        }
    }

    private static unsafe void ScanModTiles(int width, int height, TilePtrs ptrs)
    {
        var type = ptrs.Type;
        var sHeader = ptrs.SHeader;
        var lookup = ptrs.Lookup;
        var frameX = ptrs.FrameX;
        var frameY = ptrs.FrameY;
        var b1 = ptrs.B1;
        var b2 = ptrs.B2;
        var b3 = ptrs.B3;
        var wall = _wallsReady ? ptrs.Wall : null;

        _recordCount = 0;
        _wallRecordCount = 0;
        long total = (long)width * height;
        for (long pos = 0; pos < total; pos++)
        {
            if (wall != null)
            {
                var wallType = wall[pos];
                if (wallType >= VanillaCounts.Wall) CollectWall((int)(pos % width), (int)(pos / width), wallType);
            }

            var def = lookup[pos];
            if ((sHeader[def] & ActiveBit) == 0) continue;

            var tileType = type[def];
            if (tileType < VanillaCounts.Tile) continue;

            if (_recordCount >= _records.Length) Array.Resize(ref _records, _records.Length * 2);

            ref var record = ref _records[_recordCount++];
            record.X = (int)(pos % width);
            record.Y = (int)(pos / width);
            record.Type = tileType;
            record.FrameX = frameX[def];
            record.FrameY = frameY[def];
            record.SHeader = sHeader[def];
            record.B1 = b1[def];
            record.B2 = b2[def];
            record.B3 = b3[def];
        }
    }

    private static unsafe void CollectWall(int x, int y, ushort wallType)
    {
        if (_wallRecordCount >= _wallRecords.Length) Array.Resize(ref _wallRecords, _wallRecords.Length * 2);

        var cell = Main.tile[x, y];
        ref var record = ref _wallRecords[_wallRecordCount++];
        record.X = x;
        record.Y = y;
        record.Type = wallType;
        record.Key = WallTable.Has(wallType) ? WallTable.Of(wallType)!.FullKey : null;
        record.Color = cell.wallColor();
        record.Invisible = cell.invisibleWall();
        record.Fullbright = cell.fullbrightWall();
    }

    private static unsafe void WriteSidecar(TilePtrs ptrs)
    {
        if (DataFile() is null) return;

        var rows = new List<object?>(_recordCount);
        for (var i = 0; i < _recordCount; i++)
        {
            ref readonly var record = ref _records[i];
            var tile = TileTable.Of(record.Type);
            if (tile is null)
            {
                Safe.Once("tile.sidecar", $"[tile] 方块 {record.Type} 没有登记表条目，跳过侧车行", true);
                continue;
            }

            var row = new TagCompound
            {
                ["x"] = record.X,
                ["y"] = record.Y,
                ["t"] = tile.FullKey,
                ["fx"] = (int)record.FrameX,
                ["fy"] = (int)record.FrameY,
                ["s"] = (int)record.SHeader,
                ["b1"] = (int)record.B1,
                ["b2"] = (int)record.B2,
                ["b3"] = (int)record.B3,
            };

            rows.Add(row);
        }

        if (_kept is not null) rows.AddRange(_kept);
        var root = WorldSidecar.Read();
        if (rows.Count > 0) root.Set(SaveSection, rows);
        else root.Remove(SaveSection);

        if (_wallsReady) WriteWalls(root);

        if (root.Count > 0) WorldSidecar.Write(root);
    }

    private static unsafe void WriteWalls(TagCompound root)
    {
        var rows = new List<object?>(_wallRecordCount);
        for (var i = 0; i < _wallRecordCount; i++)
        {
            ref readonly var w = ref _wallRecords[i];
            var row = new TagCompound { ["x"] = w.X, ["y"] = w.Y, ["c"] = (int)w.Color };
            if (w.Key is not null) row["t"] = w.Key;
            else row["n"] = (int)w.Type;
            var flags = (w.Invisible ? 1 : 0) | (w.Fullbright ? 2 : 0);
            if (flags != 0) row["f"] = flags;
            rows.Add(row);
        }

        var wall = _wallsReady ? ReadPtrs().Wall : null;
        if (_keptWalls is not null && wall != null)
        {
            var t = Main.tile;
            foreach (var row in _keptWalls)
            {
                var x = row.GetInt("x");
                var y = row.GetInt("y");
                if (t is null || x < 0 || y < 0 || x >= t._width || y >= t._height)
                {
                    rows.Add(row);
                    continue;
                }

                if (wall[y * (long)t._width + x] == 0) rows.Add(row);
            }
        }

        if (rows.Count > 0) root.Set(WallSection, rows);
        else root.Remove(WallSection);
    }

    private static unsafe IntPtr BuildFakeBlock(int width, int height)
    {
        var ptrs = ReadPtrs();
        var realType = ptrs.Type;
        var realHeader = ptrs.SHeader;
        if (realType == null || realHeader == null) return StaticsBlock();

        long count = realHeader > (short*)realType
            ? ((byte*)realHeader - (byte*)realType) / sizeof(ushort)
            : 0;
        if (count < 1024 || count > (1 << 24)) count = 200000;

        EnsureCopyBuffers((int)count);

        var typeCopy = (ushort*)_typeCopy;
        var headerCopy = (short*)_sHeaderCopy;
        for (var d = 0; d < count; d++)
        {
            typeCopy[d] = realType[d];
            headerCopy[d] = realHeader[d];
            if (typeCopy[d] < VanillaCounts.Tile) continue;
            typeCopy[d] = 0;
            headerCopy[d] &= (short)~ActiveBit;
        }

        Buffer.MemoryCopy((void*)StaticsBlock(), (void*)_fakeBlock, BlockSize, BlockSize);
        *(ushort**)((byte*)_fakeBlock + TypeSlotOffset) = typeCopy;
        *(short**)((byte*)_fakeBlock + HeaderSlotOffset) = headerCopy;

        var realWall = _wallRecordCount > 0 ? ptrs.Wall : null;
        if (realWall != null)
        {
            var cells = (long)width * height;
            EnsureWallCopy((int)cells);
            var wallCopy = (ushort*)_wallCopy;
            Buffer.MemoryCopy(realWall, wallCopy, cells * sizeof(ushort), cells * sizeof(ushort));
            for (var pos = 0; pos < cells; pos++)
            {
                if (wallCopy[pos] >= VanillaCounts.Wall) wallCopy[pos] = 0;
            }
            *(ushort**)((byte*)_fakeBlock + WallSlotOffset) = wallCopy;
        }

        return _fakeBlock;
    }

    private static unsafe void EnsureCopyBuffers(int definitions)
    {
        if (_copyCapacity >= definitions && _typeCopy != IntPtr.Zero && _sHeaderCopy != IntPtr.Zero) return;

        var typeBytes = (IntPtr)(definitions * sizeof(ushort));
        var headerBytes = (IntPtr)(definitions * sizeof(short));
        _typeCopy = _typeCopy == IntPtr.Zero ? Marshal.AllocHGlobal(typeBytes) : Marshal.ReAllocHGlobal(_typeCopy, typeBytes);
        _sHeaderCopy = _sHeaderCopy == IntPtr.Zero
            ? Marshal.AllocHGlobal(headerBytes)
            : Marshal.ReAllocHGlobal(_sHeaderCopy, headerBytes);
        _copyCapacity = definitions;
    }

    private static unsafe void EnsureWallCopy(int cells)
    {
        if (_wallCapacity >= cells && _wallCopy != IntPtr.Zero) return;

        var bytes = (IntPtr)(cells * sizeof(ushort));
        _wallCopy = _wallCopy == IntPtr.Zero ? Marshal.AllocHGlobal(bytes) : Marshal.ReAllocHGlobal(_wallCopy, bytes);
        _wallCapacity = cells;
    }

    private static unsafe void StripReal(int width, int height)
    {
        var ptrs = ReadPtrs();
        var type = ptrs.Type;
        var sHeader = ptrs.SHeader;
        var lookup = ptrs.Lookup;
        if (type == null || sHeader == null || lookup == null) return;

        var seen = new HashSet<uint>();
        var defs = new List<uint>();
        long total = (long)width * height;
        for (long pos = 0; pos < total; pos++)
        {
            var def = lookup[pos];
            if ((sHeader[def] & ActiveBit) == 0 || type[def] < VanillaCounts.Tile || !seen.Add(def)) continue;
            defs.Add(def);
        }

        _strippedTypes = new ushort[defs.Count];
        _strippedHeaders = new short[defs.Count];
        _strippedDefs = defs;
        for (var i = 0; i < defs.Count; i++)
        {
            _strippedTypes[i] = type[defs[i]];
            _strippedHeaders[i] = sHeader[defs[i]];
            type[defs[i]] = 0;
            sHeader[defs[i]] &= (short)~ActiveBit;
        }

        var wall = _wallsReady && _wallRecordCount > 0 ? ptrs.Wall : null;
        if (wall != null)
        {
            var stripped = new List<(long, ushort)>(_wallRecordCount);
            for (var i = 0; i < _wallRecordCount; i++)
            {
                var pos = (long)_wallRecords[i].Y * width + _wallRecords[i].X;
                stripped.Add((pos, wall[pos]));
                wall[pos] = 0;
            }
            _strippedWalls = stripped;
        }
    }

    private static unsafe void RestoreReal()
    {
        var ptrs = ReadPtrs();
        var type = ptrs.Type;
        var sHeader = ptrs.SHeader;
        if (_strippedDefs is null || !ptrs.Ok || type == null || sHeader == null) return;

        for (var i = 0; i < _strippedDefs.Count; i++)
        {
            type[_strippedDefs[i]] = _strippedTypes![i];
            sHeader[_strippedDefs[i]] = _strippedHeaders![i];
        }

        var wall = ptrs.Wall;
        if (_strippedWalls is not null && wall != null)
        {
            foreach (var (pos, value) in _strippedWalls)
            {
                if (wall[pos] == 0) wall[pos] = value;
            }
        }

        _strippedTypes = null;
        _strippedHeaders = null;
        _strippedDefs = null;
        _strippedWalls = null;
    }

    private static void LoadTiles(On.Terraria.IO.WorldFile.orig_LoadWorldTiles orig,
        Il2CppSystem.IO.BinaryReader reader, Il2CppStructArray<bool> importance)
    {
        orig(reader, importance);

        try
        {
            if (DataFile() is null) return;

            var rows = WorldSidecar.Read().GetList<TagCompound>(SaveSection);
            if (rows.Count == 0) return;

            var tile = Main.tile;
            if (tile is null) return;

            var width = tile._width;
            var height = tile._height;
            var kept = new List<TagCompound>();
            var restored = 0;
            var builtOver = 0;

            foreach (var row in rows)
            {
                try
                {
                    if (!ModContent.TryFind<ModTile>(row.GetString("t"), out var modTile))
                    {
                        kept.Add(row);
                        continue;
                    }

                    var x = row.GetInt("x");
                    var y = row.GetInt("y");
                    if (x < 0 || y < 0 || x >= width || y >= height) continue;

                    if (tile[x, y].active())
                    {
                        builtOver++;
                        continue;
                    }

                    var index = width * y + x;
                    var cell = tile[x, y];

                    TileData.SetType(index, (ushort)modTile.Type);
                    cell.sTileHeader = (short)row.GetInt("s");
                    cell.frameX = (short)row.GetInt("fx");
                    cell.frameY = (short)row.GetInt("fy");
                    cell.bTileHeader = (byte)row.GetInt("b1");
                    cell.bTileHeader2 = (byte)row.GetInt("b2");
                    cell.bTileHeader3 = (byte)row.GetInt("b3");
                    TileData.ReregisterTile(index);

                    restored++;
                }
                catch (Exception e)
                {
                    ContentErrors.Report("[tile] 侧车行恢复失败：" + Safe.Describe(e));
                    kept.Add(row);
                }
            }

            _kept = kept.Count > 0 ? kept : null;
            Log.Info($"[tile] 侧车恢复 {restored} 个 mod 方块（等待 {_kept?.Count ?? 0}，被占用 {builtOver}）");

            if (_wallsReady) RestoreWalls(width, height);
        }
        catch (Exception e)
        {
            ContentErrors.Report("[tile] 侧车恢复失败：" + Safe.Describe(e));
        }
    }

    private static void ClearWorld(On.Terraria.WorldGen.orig_clearWorld orig)
    {
        _kept = null;
        _keptWalls = null;
        orig();
    }

    private static unsafe void RestoreWalls(int width, int height)
    {
        var rows = WorldSidecar.Read().GetList<TagCompound>(WallSection);
        if (rows.Count == 0) return;

        var wall = ReadPtrs().Wall;
        if (wall == null) return;

        var tile = Main.tile;
        if (tile is null) return;

        var kept = new List<TagCompound>();
        var restored = 0;
        var builtOver = 0;
        var placed = new List<(int X, int Y)>();

        foreach (var row in rows)
        {
            try
            {
                var x = row.GetInt("x");
                var y = row.GetInt("y");
                if (x < 0 || y < 0 || x >= width || y >= height) continue;

                ushort type;
                if (row.ContainsKey("t"))
                {
                    if (!ModContent.TryFind<ModWall>(row.GetString("t"), out var modWall))
                    {
                        kept.Add(row);
                        continue;
                    }
                    type = (ushort)modWall.Type;
                }
                else
                {
                    type = (ushort)row.GetInt("n");
                    if (!WallTable.Has(type))
                    {
                        kept.Add(row);
                        continue;
                    }
                }

                var offset = (long)y * width + x;
                if (wall[offset] != 0)
                {
                    builtOver++;
                    continue;
                }

                var cell = tile[x, y];
                cell.wall = type;
                var color = row.GetInt("c");
                if (color != 0) cell.wallColor((byte)color);
                var flags = row.ContainsKey("f") ? row.GetInt("f") : 0;
                if ((flags & 1) != 0) cell.invisibleWall(true);
                if ((flags & 2) != 0) cell.fullbrightWall(true);
                placed.Add((x, y));
                restored++;
            }
            catch (Exception e)
            {
                ContentErrors.Report("[tile] 墙侧车行恢复失败：" + Safe.Describe(e));
                kept.Add(row);
            }
        }

        foreach (var (x, y) in placed) Framing.WallFrame(x, y, true);

        _keptWalls = kept.Count > 0 ? kept : null;
        if (restored > 0 || kept.Count > 0)
            Log.Info($"[tile] 墙侧车恢复 {restored} 面（等待 {_keptWalls?.Count ?? 0}，被占用 {builtOver}）");
    }

    private static string? DataFile()
    {
        var worldData = Main.ActiveWorldFileData;
        if (worldData is null || worldData.IsCloudSave) return null;

        var path = Main.worldPathName;
        if (string.IsNullOrEmpty(path)) return null;

        if (path.EndsWith(".wld", StringComparison.OrdinalIgnoreCase)) path = path[..^4];
        return path + ".wld.tiles.json";
    }
}
