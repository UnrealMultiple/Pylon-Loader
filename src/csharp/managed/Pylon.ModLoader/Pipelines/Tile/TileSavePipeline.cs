using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Pylon.NaviteAPI.Host;
using Terraria;
using Terraria.IO;

namespace Pylon.ModLoader;

/// <summary>
/// 世界存档守卫：存档时把静态块换成"mod 定义遮成空气"的副本（内核指令补丁 + x27 注入，见 PatchNative），
/// 游戏其余部分看真块；全图裸指针扫描的结果按 FullKey 写侧车 <c>&lt;world&gt;.wld.tiles.json</c>
/// （含作者数据 <see cref="ModTile.SaveData"/>/<see cref="ModTile.LoadData"/>），读档按行恢复，mod 缺席的行进等待名单。
/// 云存档不遮不收集；指令补丁失败降级为"真定义数组遮蔽 → 存 → 还原"。
/// 静态块 / 指针字段 / 方法体解析全走 interop 自己的面（见 Build）。
/// </summary>
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

    // 本构建 SaveWorldTilesFast+0x104 的 `ldr x8,[x8,#0xb8]`（取静态块）→ `mov x8, x27`
    private const int LoadStaticsOffset = 0x104;
    private const uint LoadStaticsInsn = 0xF9405D08u;
    private const uint MovFromX27Insn = 0xAA1B03E8u;
    private const short ActiveBit = 0x20;

    // TileData 静态块布局（refs/dump.cs），Build 时自检，不符即放弃。
    private const int ClassStaticFieldsOffset = 0xB8; // Il2CppClass::static_fields（IL2CPP 布局）
    private const int LookupSlotOffset = 0x68;        // TileLookup
    private const int TypeSlotOffset = 0xA8;          // TileType
    private const int HeaderSlotOffset = 0xB0;        // TileSHeader
    private const int FrameXSlotOffset = 0xB8;        // TileFrameX
    private const int FrameYSlotOffset = 0xC0;        // TileFrameY
    private const int WallSlotOffset = 0xC8;          // TileWall（每格 ushort，与定义数组不同层）
    private const int B1SlotOffset = 0xD8;            // TileBHeader
    private const int B2SlotOffset = 0xE0;            // TileBHeader2
    private const int B3SlotOffset = 0xE8;            // TileBHeader3
    private const int BlockSize = 0xF0;               // 到 TileBHeader3（0xE8）+ 8 为止

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
    private static bool _wallsReady; // TileWall 槽核实过（0xC8 ↔ 代理指针）；墙的遮蔽与侧车都靠它

    private static IntPtr _body;        // SaveWorldTilesFast 方法体（ShadowHook 入口在它头上）
    private static IntPtr _trampoline;

    // 假块与遮蔽副本：进程级复用的原生缓冲（存档频率低，不值得每次分配）
    private static IntPtr _fakeBlock, _typeCopy, _sHeaderCopy, _wallCopy;
    private static int _copyCapacity, _wallCapacity;

    // 扫描结果的密集暂存，侧车与遮蔽共用
    private static Record[] _records = new Record[256];
    private static int _recordCount;

    private struct WallRecord
    {
        public int X, Y;
        public ushort Type;
        public string? Key; // 登记过 = FullKey；没登记 = null，侧车存原号
        public byte Color;
        public bool Invisible, Fullbright;
    }

    private static WallRecord[] _wallRecords = new WallRecord[64];
    private static int _wallRecordCount;

    // 降级路径：真数组上按定义遮蔽的往返
    private static ushort[]? _strippedTypes;
    private static short[]? _strippedHeaders;
    private static List<uint>? _strippedDefs;
    private static List<(long Pos, ushort Value)>? _strippedWalls;

    private static List<TagCompound>? _kept;
    private static List<TagCompound>? _keptWalls;

    private static IModLog Log => LoaderHost.Current.Log;

    /// <summary>TileTable.Build 之后调用：有 mod 方块才装；装失败不致命（mod 方块照旧按号进 .wld）。</summary>
    internal static void Build(IModLog log)
    {
        if (_installed || ModContent.Count(ContentKind.Tile) <= 0) return;
        _installed = true;

        try
        {
            unsafe
            {
                // 解析全部就绪后才挂钩：中途失败绝不能留半个钩子。
                // 方法体走 interop 自己的解析路径（HookGen 定位 On.* 目标用的同一套）：
                // 类型指针来自强类型 store，方法按名匹配（WorldFile 上此名唯一）。
                var klass = Il2CppClassPointerStore<WorldFile>.NativeClassPtr;
                if (klass == IntPtr.Zero) throw new InvalidOperationException("WorldFile 类指针未就绪");

                var method = IL2CPP.GetIl2CppMethod(klass, false, "SaveWorldTilesFast", "System.Int32",
                    "System.IO.BinaryWriter");
                // MethodInfo 头部第一个指针宽字段就是 methodPointer（与内核 PylonMod_MethodPointer 同一布局）。
                // 失败时 GetIl2CppMethod 返回 missing-method 假桩而非 null——靠下面的指令指纹拦住它。
                _body = method == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(method);

                var block = StaticsBlock();
                if (block == IntPtr.Zero)
                    throw new InvalidOperationException("TileData 静态块未就绪（cctor 没跑？）");

                _wallsReady = ModContent.Count(ContentKind.Wall) > 0;

                // 指令指纹先验：解析到假桩 / 游戏版本变了都在挂钩之前拦下。
                if (_body == IntPtr.Zero || *(uint*)((byte*)_body + LoadStaticsOffset) != LoadStaticsInsn)
                    throw new InvalidOperationException(
                        $"SaveWorldTilesFast+{LoadStaticsOffset:x} 指纹不符（解析到假桩或版本变化），不挂钩");

                _fakeBlock = Marshal.AllocHGlobal(BlockSize);

                var detour = (delegate* unmanaged<IntPtr, IntPtr, int>)&SaveTilesDetour;
                if (HookNative.Apply(_body, (IntPtr)detour, out _trampoline) != 0)
                    throw new InvalidOperationException("SaveWorldTilesFast 挂钩失败 rc 非零（见内核日志）");

                // 补丁失败不回滚钩子：detour 走真块 + 真数组遮蔽降级（_patched=false）。
                _patched = PatchNative.PatchInstruction(_body, LoadStaticsOffset, LoadStaticsInsn,
                    MovFromX27Insn) == 0;
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

    /// <summary>TileData 的静态字段块：类指针 + 0xB8 解引用（每次存档现读）。</summary>
    private static unsafe IntPtr StaticsBlock()
    {
        var klass = Il2CppClassPointerStore<TileData>.NativeClassPtr;
        if (klass == IntPtr.Zero) return IntPtr.Zero;
        try { IL2CPP.il2cpp_runtime_class_init(klass); } catch { }
        return *(IntPtr*)((byte*)klass + ClassStaticFieldsOffset);
    }

    // ===================== 存档 detour =====================

    /// <summary>补丁是永久的：trampoline 必须且只能调一次，x27 必须有值——所以这里不许抛。</summary>
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

    /// <summary>扫描 + 侧车 + 组假块；返回这次调用该用的静态块和是否需要还原真数组。</summary>
    private static (IntPtr Block, bool Strip) Prepare(IntPtr block)
    {
        // 云存档：没有侧车落点，遮了 mod 方块就真丢了——保持旧行为（按号进 .wld）。
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

            // 降级：真数组按定义遮蔽（渲染线程会看到 mod 方块消失一瞬），存完还原。
            StripReal(width, height);
            return (block, true);
        }
    }

    /// <summary>全图扫描 mod 方块与 mod 墙（裸指针循环，原生速度）；记录进密集暂存。</summary>
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

    /// <summary>
    /// 一格 mod 墙：登记过记 FullKey，没登记（mod 缺席的残留号）记原号等重装；
    /// 色号/隐形/全亮经游戏自己的 getter 读——存储打包不在这里猜。
    /// </summary>
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

    /// <summary>行 = 位置 + FullKey + 完整格子状态 + 作者数据；mod 缺席的等待行原样回写。</summary>
    private static unsafe void WriteSidecar(TilePtrs ptrs)
    {
        var file = DataFile();
        if (file is null) return;

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

            if (tile.HookEnabled(HookId.SaveData))
            {
                var data = new TagCompound();
                try
                {
                    tile.SaveData(record.X, record.Y, data);
                }
                catch (Exception e)
                {
                    ContentErrors.Report($"[tile] {tile.Name}.SaveData 异常：{Safe.Describe(e)}");
                }
                if (data.Count > 0) row["d"] = data;
            }

            rows.Add(row);
        }

        // 等待行原样回写；占用与否读档时再裁决。
        if (_kept is not null) rows.AddRange(_kept);

        var root = TagJson.Read(file);
        if (rows.Count > 0) root.Set(SaveSection, rows);
        else root.Remove(SaveSection);

        if (_wallsReady) WriteWalls(root);

        try
        {
            if (root.Count > 0) TagJson.Write(file, root);
            else if (File.Exists(file)) File.Delete(file);
        }
        catch (Exception e)
        {
            ContentErrors.Report("[tile] 侧车写入失败：" + Safe.Describe(e));
        }
    }

    /// <summary>
    /// 墙段：这次扫出的行 + 等待行。等待行只在位置还没被任何墙占住时才继续等
    /// （被占了 = 占用者赢，行作废）。
    /// </summary>
    private static unsafe void WriteWalls(TagCompound root)
    {
        var rows = new List<object?>(_wallRecordCount);
        for (var i = 0; i < _wallRecordCount; i++)
        {
            ref readonly var w = ref _wallRecords[i];
            var row = new TagCompound { ["x"] = w.X, ["y"] = w.Y, ["c"] = (int)w.Color };
            if (w.Key is not null) row["t"] = w.Key;
            else row["n"] = (int)w.Type; // mod 缺席的原号：重装后按号认领（平移风险与不遮蔽时一致）
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
                    rows.Add(row); // 世界变小了之类：留到读档裁决
                    continue;
                }

                if (wall[y * (long)t._width + x] == 0) rows.Add(row);
            }
        }

        if (rows.Count > 0) root.Set(WallSection, rows);
        else root.Remove(WallSection);
    }

    /// <summary>
    /// 组"假静态块"：块整体拷贝，TileType/TileSHeader 槽指向 mod 定义遮成空气的副本，
    /// 有 mod 墙时 TileWall 槽指向整格墙遮 0 的副本（墙是每格数组，遮格不遮定义）。
    /// </summary>
    private static unsafe IntPtr BuildFakeBlock(int width, int height)
    {
        var ptrs = ReadPtrs();
        var realType = ptrs.Type;
        var realHeader = ptrs.SHeader;
        if (realType == null || realHeader == null) return StaticsBlock();

        // 定义数：TileSHeader 的定义数组紧跟 TileType 分配（邻接启发），取不到就按 TileBuffer 上限。
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

        // 块整体拷贝 + 只有 type/sHeader 两个槽指向副本；lookup/frames/wall 仍是真数组（存档照常读它们）。
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

    /// <summary>遮蔽副本缓冲：定义数变大了才扩（世界更大 / 更多方块）。</summary>
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

    /// <summary>墙格遮蔽副本缓冲：格数变大了才扩。</summary>
    private static unsafe void EnsureWallCopy(int cells)
    {
        if (_wallCapacity >= cells && _wallCopy != IntPtr.Zero) return;

        var bytes = (IntPtr)(cells * sizeof(ushort));
        _wallCopy = _wallCopy == IntPtr.Zero ? Marshal.AllocHGlobal(bytes) : Marshal.ReAllocHGlobal(_wallCopy, bytes);
        _wallCapacity = cells;
    }

    /// <summary>降级：真定义数组里把 mod 类型遮成空气（定义是共享的，改一次等于改掉全世界同内容方块）；
    /// mod 墙按格遮 0（墙是每格的，逐格记值还原）。</summary>
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

        // 存档期间游戏线程正忙着存档，定义不会被别的逻辑改写——直接写回原值。
        for (var i = 0; i < _strippedDefs.Count; i++)
        {
            type[_strippedDefs[i]] = _strippedTypes![i];
            sHeader[_strippedDefs[i]] = _strippedHeaders![i];
        }

        // 墙按格还原：位置上出现了别的墙（不该发生）就不覆盖。
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

    // ===================== 读档 =====================

    private static void LoadTiles(On.Terraria.IO.WorldFile.orig_LoadWorldTiles orig,
        Il2CppSystem.IO.BinaryReader reader, Il2CppStructArray<bool> importance)
    {
        orig(reader, importance);

        try
        {
            var file = DataFile();
            if (file is null || !File.Exists(file)) return;

            var rows = TagJson.Read(file).GetList(SaveSection);
            if (rows.Count == 0) return;

            var tile = Main.tile;
            if (tile is null) return;

            var width = tile._width;
            var height = tile._height;
            var kept = new List<TagCompound>();
            var restored = 0;
            var builtOver = 0;

            foreach (var entry in rows)
            {
                if (entry is not TagCompound row) continue;

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

                    // 位置被别的方块占了：占用者赢，这行作废。
                    if (tile[x, y].active())
                    {
                        builtOver++;
                        continue;
                    }

                    var index = width * y + x;
                    var cell = tile[x, y];

                    // 恢复顺序（先重内联再裸写其余字段）：
                    // ① SetType 重内联——裸写类型会落在共享的"空气定义"上，把全世界空格一起改掉；
                    // ② 其余字段裸写（此刻的定义可能与同类恢复格共享，短暂污染无妨，游戏线程正读档）；
                    // ③ ReregisterTile 按最终内容重新内联，把共享定义拆开。
                    // wall/liquid 不碰：.wld 对这些格子本来就存了它们（遮蔽只遮 type/sHeader）。
                    TileData.SetType(index, (ushort)modTile.Type);
                    cell.sTileHeader = (short)row.GetInt("s");
                    cell.frameX = (short)row.GetInt("fx");
                    cell.frameY = (short)row.GetInt("fy");
                    cell.bTileHeader = (byte)row.GetInt("b1");
                    cell.bTileHeader2 = (byte)row.GetInt("b2");
                    cell.bTileHeader3 = (byte)row.GetInt("b3");
                    TileData.ReregisterTile(index);

                    if (modTile.HookEnabled(HookId.LoadData) && row.ContainsKey("d"))
                    {
                        try
                        {
                            modTile.LoadData(x, y, row.GetCompound("d"));
                        }
                        catch (Exception e)
                        {
                            ContentErrors.Report($"[tile] {modTile.Name}.LoadData 异常：{Safe.Describe(e)}");
                        }
                    }

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

            if (_wallsReady) RestoreWalls(file, width, height);
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

    // ===================== 墙的读档 =====================

    /// <summary>
    /// 墙按行恢复（在方块行之后）："t" 行按 FullKey、"n" 行按原号认领；
    /// 位置上有任何墙就算占用（占用者赢）——墙可以在方块底下，与方块占用无关。
    /// 放完统一 WallFrame 重框。mod 缺席的行进等待名单。
    /// </summary>
    private static unsafe void RestoreWalls(string file, int width, int height)
    {
        if (!File.Exists(file)) return;

        var rows = TagJson.Read(file).GetList(WallSection);
        if (rows.Count == 0) return;

        var wall = ReadPtrs().Wall;
        if (wall == null) return;

        var tile = Main.tile;
        if (tile is null) return;

        var kept = new List<TagCompound>();
        var restored = 0;
        var builtOver = 0;
        var placed = new List<(int X, int Y)>();

        foreach (var entry in rows)
        {
            if (entry is not TagCompound row) continue;

            try
            {
                var x = row.GetInt("x");
                var y = row.GetInt("y");
                if (x < 0 || y < 0 || x >= width || y >= height) continue;

                // 认领：FullKey 优先（跨平移稳定）；原号行只在当前有 mod 认这个号时恢复。
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

    /// <summary>侧车路径：与 NPC 侧车同款命名（手机版 worldPathName 自带 .wld 后缀要剥掉）；云存档返回 null。</summary>
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
