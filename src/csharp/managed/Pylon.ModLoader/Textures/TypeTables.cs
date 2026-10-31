using System.Diagnostics;
using Il2CppInterop.Runtime;

namespace Pylon.ModLoader.Textures;

/// <summary>
/// 按类型索引的静态数组表扩容：直接走 il2cpp 原生 API（字段枚举 + 静态读写 + array_new + memcpy）。
/// 不用反射：interop 逐元素读写把 <c>Nullable&lt;T&gt;[]</c> 当 IntPtr 步长的内联结构，600+ 张表
/// 整批抛 TargetInvocationException、单张十几毫秒，表没扩成 → mod 类型越界读。
/// 原生侧只认"元素 class + 大小"：值类型 memcpy、引用拷指针，无 interop 包装。
/// </summary>
public static class TypeTables
{
    private sealed class TableClass(string ns, string name, string? nested = null, string? nested2 = null, string[]? skip = null)
    {
        internal string Ns { get; } = ns ?? "";
        internal string Name { get; } = name;
        internal string? Nested { get; } = nested;
        internal string? Nested2 { get; } = nested2;
        internal string[]? Skip { get; } = skip;

        public override string ToString() =>
            (Ns.Length == 0 ? "" : Ns + ".") + Name +
            (string.IsNullOrEmpty(Nested) ? "" : "." + Nested) +
            (string.IsNullOrEmpty(Nested2) ? "" : "." + Nested2);
    }

    /// <summary>libil2cpp 里所有 6196 长度的 new[]。</summary>
    private static readonly TableClass[] ItemClasses =
    [
        new("Terraria.ID", "ItemID", "Sets"),
        new("Terraria.ID", "ItemID"),
        new("Terraria.ID", "AmmoID", "Sets"),
        new("Terraria", "Item"),
        new("Terraria", "Lang"),
        new("Terraria", "Main"),
        new("Terraria", "Player"),
        new("Terraria", "Recipe"),
        new("Terraria.GameContent", "TextureAssets"),
        new("Terraria.GameContent", "EmergencyStacking"),
        new("Terraria.GameContent.Items", "ItemVariants"),
        new("Terraria.GameContent.Prefixes", "PrefixLegacy", "ItemSets"),
        new("Terraria.DataStructures", "ArmorSetBonuses"),
        new("Terraria.UI", "ItemSorting"),
        new("Terraria.UI", "ItemSlot"),
        new("", "VirtualControllerInputState"),   // 触控控制器（全局命名空间）
    ];

    private static readonly TableClass[] ProjectileClasses =
    [
        new("Terraria.ID", "ProjectileID", "Sets"),
        new("Terraria", "Main"),
        new("Terraria", "Lang"),
        new("Terraria", "Projectile"),
        new("Terraria.GameContent", "TextureAssets"),
    ];

    private static readonly TableClass[] NpcClasses =
    [
        new("Terraria.ID", "NPCID", "Sets"),
        new("Terraria", "Main"),
        new("Terraria", "Lang"),
        new("Terraria", "NPC"),
        new("Terraria.GameContent", "TextureAssets"),
        new("Terraria.GameContent.UI", "EmoteBubble"),
        new("Terraria.GameContent", "ConditionalDialogue"),
    ];

    private static readonly TableClass[] BuffClasses =
    [
        new("Terraria.ID", "BuffID", "Sets"),
        new("Terraria", "Main", null, null, ["item", "timeItemSlotCannotBeReusedFor", "itemFrame", "itemFrameCounter"]),
        new("Terraria", "Lang"),
        new("Terraria.GameContent", "TextureAssets"),
    ];

    // 扫描过 `mov #0x2f2` 的全部 754 长度表。
    private static readonly TableClass[] TileClasses =
    [
        new("Terraria", "Main"),
        new("Terraria.ID", "TileID", "Sets"),
        new("Terraria.ID", "TileID", "Sets", "Conversion"),
        new("Terraria.ID", "TileID", "Sets", "TileCutIgnore"),
        new("Terraria.ID", "TileID", "Sets", "ForAdvancedCollision"),
        new("Terraria.ID", "TileID", "Sets", "RoomNeeds"),
        new("Terraria.ID", "TileID", "Sets", "Wiring"),
        new("Terraria.GameContent", "TextureAssets"),
        new("Terraria", "WorldGen"),
        new("Terraria.Map", "MapHelper"),
        new("Terraria", "Recipe"),
        new("Terraria.GameContent.Metadata", "TileMaterials"),
        new("Terraria", "WorldGen", "Skyblock"),
        new("Terraria.GameContent.Biomes", "CorruptionPitBiome"),
        new("Terraria.GameContent.Biomes.CaveHouse", "HouseUtils"),
    ];

    private static readonly TableClass[] WallClasses =
    [
        new("Terraria", "Main"),
        new("Terraria.ID", "WallID", "Sets"),
        new("Terraria.ID", "WallID", "Sets", "Conversion"),
        new("Terraria.GameContent", "TextureAssets"),
        new("Terraria.Map", "MapHelper"),
    ];

    public enum TableKind { Item, Projectile, Npc, Buff, Tile, Wall }

    private static TableClass[] Classes(TableKind kind) => kind switch
    {
        TableKind.Item => ItemClasses,
        TableKind.Projectile => ProjectileClasses,
        TableKind.Npc => NpcClasses,
        TableKind.Buff => BuffClasses,
        TableKind.Tile => TileClasses,
        _ => WallClasses,
    };

    private static string Name(TableKind kind) => kind switch
    {
        TableKind.Item => "物品",
        TableKind.Projectile => "弹幕",
        TableKind.Npc => "NPC",
        TableKind.Buff => "Buff",
        TableKind.Tile => "方块",
        _ => "墙壁",
    };

    private static int VanillaCount(TableKind kind) => kind switch
    {
        TableKind.Item => Terraria.ID.ItemID.Count,
        TableKind.Projectile => Terraria.ID.ProjectileID.Count,
        TableKind.Npc => Terraria.ID.NPCID.Count,
        TableKind.Buff => Terraria.ID.BuffID.Count,
        TableKind.Tile => 754,
        _ => 367,
    };

    private const uint FieldStatic = 0x0010;    // FieldAttributes.Static
    private const uint FieldLiteral = 0x0040;   // FieldAttributes.Literal（const）
    private const int TypeSzArray = 0x1D;       // Il2CppTypeEnum.SZARRAY（T[]，含交错数组）

    private static IntPtr _image;

    /// <summary>Assembly-CSharp 的 il2cpp image（清单里的类都在这里）。</summary>
    private static IntPtr Image
    {
        get
        {
            if (_image != IntPtr.Zero) return _image;
            try
            {
                var item = Il2CppClassPointerStore<Terraria.Item>.NativeClassPtr;
                if (item != IntPtr.Zero) _image = IL2CPP.il2cpp_class_get_image(item);
            }
            catch { /* 下面还有按程序集遍历的兜底 */ }

            if (_image == IntPtr.Zero) _image = FindImage("Assembly-CSharp.dll");
            return _image;
        }
    }

    /// <summary>按名字在域里的程序集找 image（GetIl2CppImage 是私有的，这里自己走一遍）。</summary>
    private static unsafe IntPtr FindImage(string imageName)
    {
        try
        {
            var domain = IL2CPP.il2cpp_domain_get();
            if (domain == IntPtr.Zero) return IntPtr.Zero;
            uint count = 0;
            var assemblies = IL2CPP.il2cpp_domain_get_assemblies(domain, ref count);
            if (assemblies == null) return IntPtr.Zero;
            for (var i = 0; i < count; i++)
            {
                var image = IL2CPP.il2cpp_assembly_get_image(assemblies[i]);
                if (image == IntPtr.Zero) continue;
                if (string.Equals(IL2CPP.il2cpp_image_get_name_(image), imageName, StringComparison.Ordinal))
                    return image;
            }
        }
        catch { }
        return IntPtr.Zero;
    }

    /// <summary>按 (命名空间, 类名, 嵌套名) 找原生 class；找不到返回 0。</summary>
    private static IntPtr FindClass(TableClass spec)
    {
        try
        {
            var image = Image;
            if (image == IntPtr.Zero) return IntPtr.Zero;
            var cls = IL2CPP.il2cpp_class_from_name(image, spec.Ns, spec.Name);
            if (cls == IntPtr.Zero) return IntPtr.Zero;
            if (!string.IsNullOrEmpty(spec.Nested)) cls = FindNested(cls, spec.Nested);
            if (cls != IntPtr.Zero && !string.IsNullOrEmpty(spec.Nested2)) cls = FindNested(cls, spec.Nested2);
            return cls;
        }
        catch { return IntPtr.Zero; }
    }

    private static IntPtr FindNested(IntPtr parent, string name)
    {
        var iter = IntPtr.Zero;
        while (true)
        {
            var nested = IL2CPP.il2cpp_class_get_nested_types(parent, ref iter);
            if (nested == IntPtr.Zero) return IntPtr.Zero;
            if (string.Equals(IL2CPP.il2cpp_class_get_name_(nested), name, StringComparison.Ordinal))
                return nested;
        }
    }

    /// <summary>
    /// 字段类型是不是 T[]（只认"[]"结尾；多维 [,] 不算）。不能用 il2cpp_class_get_element_class 判：
    /// 它只是 `return klass->element_class`，不做数组检查 —— 非数组类上也会拿到非 0 值
    /// （真机踩过：把 int 静态字段 50 当数组指针，il2cpp_array_length(0x32) 直接 SIGSEGV）。
    /// </summary>
    private static bool IsArrayField(IntPtr field)
    {
        var type = IL2CPP.il2cpp_field_get_type(field);
        return type != IntPtr.Zero && IL2CPP.il2cpp_type_get_type(type) == TypeSzArray;
    }

    private static bool IsSkipped(TableClass spec, IntPtr field)
    {
        var skip = spec.Skip;
        if (skip is not { Length: > 0 }) return false;
        var name = IL2CPP.il2cpp_field_get_name_(field);
        if (string.IsNullOrEmpty(name)) return false;
        foreach (var candidate in skip)
            if (string.Equals(candidate, name, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>收集这个类的全部"静态 T[] 字段"。</summary>
    private static void CollectFields(IntPtr cls, TableClass spec, List<IntPtr> into)
    {
        var iter = IntPtr.Zero;
        while (true)
        {
            var field = IL2CPP.il2cpp_class_get_fields(cls, ref iter);
            if (field == IntPtr.Zero) return;

            var flags = (uint)IL2CPP.il2cpp_field_get_flags(field);
            if ((flags & FieldStatic) == 0 || (flags & FieldLiteral) != 0) continue;
            // thread-static 的 offset 是 (uint32)-1（这里按 int32 判）
            if (unchecked((int)IL2CPP.il2cpp_field_get_offset(field)) == -1) continue;
            if (IsSkipped(spec, field)) continue;
            if (!IsArrayField(field)) continue;
            into.Add(field);
        }
    }

    /// <summary>读静态字段里的数组对象指针（0 = 还没建 / 不是引用）。</summary>
    private static unsafe IntPtr ReadStatic(IntPtr field)
    {
        var value = IntPtr.Zero;
        IL2CPP.il2cpp_field_static_get_value(field, &value);
        return value;
    }

    /// <summary>写回静态字段（<c>field_static_set_value</c> 直接收对象指针）。</summary>
    private static unsafe void WriteStatic(IntPtr field, IntPtr array) =>
        IL2CPP.il2cpp_field_static_set_value(field, (void*)array);

    private static int ArrayLength(IntPtr array) => array == IntPtr.Zero ? 0 : (int)IL2CPP.il2cpp_array_length(array);

    /// <summary>数组元素起始地址：Il2CppArray = 对象头（2 指针）+ bounds + max_length → 4 个指针。</summary>
    private static unsafe byte* ArrayData(IntPtr array) => (byte*)array + 4 * IntPtr.Size;

    /// <summary>
    /// 值类型元素按 <c>il2cpp_class_value_size</c> 整块拷；引用元素拷指针。
    /// <paramref name="fillWithMode"/> 时尾部填"原数组出现最多的值"（方块/墙壁必须用它：
    /// 填 0 会把新方块变成"实心泥土"）。写回前新数组没人看得见，绕过写屏障安全；
    /// Android IL2CPP 是非移动 Boehm GC，本就没有写屏障。
    /// </summary>
    internal static unsafe IntPtr GrowArray(IntPtr oldArray, int newCapacity, bool fillWithMode = false)
    {
        if (oldArray == IntPtr.Zero || newCapacity <= 0) return IntPtr.Zero;

        var oldLength = ArrayLength(oldArray);
        var arrayClass = IL2CPP.il2cpp_object_get_class(oldArray);
        if (arrayClass == IntPtr.Zero) return IntPtr.Zero;
        var elementClass = IL2CPP.il2cpp_class_get_element_class(arrayClass);
        if (elementClass == IntPtr.Zero) return IntPtr.Zero;

        int elementSize;
        if (IL2CPP.il2cpp_class_is_valuetype(elementClass))
        {
            var align = 0u;
            elementSize = IL2CPP.il2cpp_class_value_size(elementClass, ref align);
        }
        else
        {
            elementSize = IntPtr.Size;   // 引用元素就是一个指针
        }
        if (elementSize <= 0) return IntPtr.Zero;

        var grown = IL2CPP.il2cpp_array_new(elementClass, (ulong)newCapacity);
        if (grown == IntPtr.Zero) return IntPtr.Zero;

        var src = ArrayData(oldArray);
        var dst = ArrayData(grown);
        var copyBytes = (long)oldLength * elementSize;
        if (copyBytes > 0)
            Buffer.MemoryCopy(src, dst, (long)newCapacity * elementSize, copyBytes);

        // 尾部填"没设置"的原值：一般 [0]，方块/墙壁用模式值。
        if (oldLength > 0 && newCapacity > oldLength)
        {
            var model = src + (fillWithMode ? ModeIndex(src, oldLength, elementSize) : 0) * elementSize;
            for (var i = oldLength; i < newCapacity; i++)
                Buffer.MemoryCopy(model, dst + (long)i * elementSize, elementSize, elementSize);
        }
        return grown;
    }

    /// <summary>元素字节里出现次数最多的下标。</summary>
    private static unsafe int ModeIndex(byte* data, int length, int elementSize)
    {
        if (length <= 0 || elementSize > 8) return 0;

        var seen = new Dictionary<ulong, (int Count, int First)>(length);
        var best = 0;
        var bestCount = 0;
        for (var i = 0; i < length; i++)
        {
            ulong value = 0;
            var at = data + (long)i * elementSize;
            for (var b = 0; b < elementSize; b++) value |= (ulong)at[b] << (8 * b);
            seen.TryGetValue(value, out var entry);
            entry = (entry.Count + 1, entry.Count == 0 ? i : entry.First);
            seen[value] = entry;
            if (entry.Count <= bestCount) continue;
            bestCount = entry.Count;
            best = entry.First;
        }
        return best;
    }

    /// <summary>已经跑过（还在盯着的）扩表器：每帧补 pending / 每 120 帧查重建。</summary>
    private static readonly List<TableGrower> Active = [];
    private static int _watchTicks;

    private static TableGrower? Find(TableKind kind)
    {
        for (var i = 0; i < Active.Count; i++)
            if (Active[i].Kind == kind) return Active[i];
        return null;
    }

    /// <summary>
    /// 被游戏重建回原版长度的表。返回本次真正扩了几张；<paramref name="regrown"/> 拿到种类回调，
    /// 贴图/名字要往新表重写一遍。
    /// </summary>
    public static int Maintenance(Action<string>? log = null, Action<TableKind>? regrown = null)
    {
        if (Active.Count == 0) return 0;

        var grown = 0;
        for (var i = 0; i < Active.Count; i++)
        {
            var n = Active[i].CheckPending(log);
            if (n <= 0) continue;
            grown += n;
            Notify(regrown, Active[i], log);
        }

        if (++_watchTicks >= 120)
        {
            _watchTicks = 0;
            for (var i = 0; i < Active.Count; i++)
            {
                var n = Active[i].Watch(log);
                if (n <= 0) continue;
                grown += n;
                Notify(regrown, Active[i], log);
            }
        }
        return grown;
    }

    /// <summary>表换了内容（补扩/重建）：贴图与名字要往新表再写一遍。</summary>
    private static void Notify(Action<TableKind>? regrown, TableGrower grower, Action<string>? log)
    {
        if (regrown is null) return;
        try { regrown(grower.Kind); }
        catch (Exception e) { log?.Invoke($"[typetables] {grower.Name} 换了新表后重写失败：{e.Message}"); }
    }

    /// <summary>
    /// 分帧扩一个种类的表：每帧扩最多 budget 张，扫完清单才 Done。一次原生 memcpy，
    /// 预算只是"别把整批硬塞进一帧"的保险。
    /// </summary>
    internal sealed class TableGrower
    {
        private readonly TableKind _kind;
        private readonly string _name;
        private readonly int _newCapacity;
        private readonly int _vanilla;
        private readonly TableClass[] _classes;
        private readonly bool _fillWithMode;

        private int _classIndex;
        private readonly List<IntPtr> _fields = [];      // 扫到的全部静态数组字段（Watch 用）
        private readonly List<IntPtr> _candidates = [];  // 其中"长度 == 原版数、该扩"的表（自检用）
        private readonly List<IntPtr> _pending = [];     // 扫到时还是 null、之后每帧补的表
        private int _fieldIndex;
        private int _grown;

        internal TableGrower(TableKind kind, int newCapacity)
        {
            _kind = kind;
            _name = Name(kind);
            _newCapacity = newCapacity;
            _vanilla = VanillaCount(kind);
            _classes = Classes(kind);
            // 方块表用模式值：填 0 会让新方块默认实心、不发光、不参与边框，和原版多数类型相反。
            _fillWithMode = kind == TableKind.Tile;

            // 同种类的旧扩表器（重装/诊断重跑）换成这一份，别让 Maintenance 双份跟踪
            for (var i = 0; i < Active.Count; i++)
            {
                if (Active[i].Kind != kind) continue;
                Active[i] = this;
                return;
            }
            Active.Add(this);
        }

        internal TableKind Kind => _kind;
        internal string Name => _name;

        /// <summary>清单走完、且扫到的字段都处理过（扩了或判过不是这类表）。</summary>
        internal bool Done => _newCapacity <= _vanilla || (_classIndex >= _classes.Length && _fieldIndex >= _fields.Count);

        /// <summary>扩下一批（最多 budget 张表）；返回本批扩了几张。Done 后恒为 0。</summary>
        internal int NextBatch(int budget, Action<string>? log)
        {
            if (_newCapacity <= _vanilla || budget <= 0) return 0;

            var watch = Stopwatch.StartNew();
            var grownBefore = _grown;
            var batch = 0;
            while (batch < budget)
            {
                if (_fieldIndex >= _fields.Count)
                {
                    if (_classIndex >= _classes.Length) break;
                    var spec = _classes[_classIndex++];
                    if (!BeginClass(spec, log)) continue;
                    continue;   // BeginClass 往 _fields 里加了字段，回到上面处理
                }

                var field = _fields[_fieldIndex++];
                var array = ReadStatic(field);
                if (array == IntPtr.Zero)
                {
                    // 静态构造没建它：之后每帧补一次
                    _pending.Add(field);
                    continue;
                }
                if (ArrayLength(array) != _vanilla) continue;   // 不是这一类（同长度靠 Skip 排除）
                _candidates.Add(field);                        // 这一张就该扩（自检按这份名单数）
                if (!GrowField(field, array, log)) continue;
                _grown++;
                batch++;
            }

            // 真机自检过：整批一般是几毫秒；冒到几十毫秒就说明某个类的静态构造/扫描很贵，值得单独记一条。
            if (watch.ElapsedMilliseconds >= 20)
                log?.Invoke($"[typetables] {_name} 这批 {watch.ElapsedMilliseconds}ms：扩了 {_grown - grownBefore} 张，" +
                            $"已扫到 {_fields.Count} 张表");
            return batch;
        }

        /// <summary>换一个类：按名解析 → 跑静态构造 → 收集静态数组字段。</summary>
        private bool BeginClass(TableClass spec, Action<string>? log)
        {
            var cls = FindClass(spec);
            if (cls == IntPtr.Zero)
            {
                log?.Invoke($"[typetables] {_name} 找不到 {spec}，跳过");
                return false;
            }

            // 先跑静态构造：没跑过时 Sets 整类还是 null（此时 mod 已读过/写过越界位置），只能当 pending 等下帧补。
            var watch = Stopwatch.StartNew();
            try { IL2CPP.il2cpp_runtime_class_init(cls); }
            catch (Exception e) { log?.Invoke($"[typetables] {_name} {spec} 静态构造异常（继续扫描）：{e.Message}"); }
            var initMs = watch.ElapsedMilliseconds;

            var before = _fields.Count;
            try { CollectFields(cls, spec, _fields); }
            catch (Exception e)
            {
                log?.Invoke($"[typetables] {_name} 枚举 {spec} 字段异常：{e.Message}");
                return false;
            }
            var scanMs = watch.ElapsedMilliseconds - initMs;
            // 冒到几十毫秒就记一条：静态构造贵（游戏内容没建全）或字段特别多时会看到。
            if (initMs + scanMs >= 20)
                log?.Invoke($"[typetables] {_name} {spec}：静态构造 {initMs}ms，枚举 {scanMs}ms，" +
                            $"{_fields.Count - before} 张表");
            return true;
        }

        private bool GrowField(IntPtr field, IntPtr array, Action<string>? log)
        {
            try
            {
                var grown = GrowArray(array, _newCapacity, _fillWithMode);
                if (grown == IntPtr.Zero) return false;
                WriteStatic(field, grown);
                return true;
            }
            catch (Exception e)
            {
                log?.Invoke($"[typetables] {_name} 成员 {IL2CPP.il2cpp_field_get_name_(field)} 异常：{e.Message}");
                return false;
            }
        }

        /// <summary>扫到时还是 null 的表：建出来就补扩（每帧）。</summary>
        internal int CheckPending(Action<string>? log)
        {
            if (_pending.Count == 0 || _newCapacity <= _vanilla) return 0;

            var grown = 0;
            for (var i = _pending.Count - 1; i >= 0; i--)
            {
                var field = _pending[i];
                var array = ReadStatic(field);
                if (array == IntPtr.Zero) continue;   // 还没建，下一帧再试
                _pending.RemoveAt(i);

                if (ArrayLength(array) != _vanilla) continue;
                _candidates.Add(field);
                if (!GrowField(field, array, log)) continue;
                _grown++;
                grown++;
            }
            if (grown > 0) log?.Invoke($"[typetables] {_name} 补扩之后才建出来的表：{grown} 张");
            return grown;
        }

        /// <summary>游戏把表重建回原版长度（切语言、重载资源）：再扩一遍。</summary>
        internal int Watch(Action<string>? log)
        {
            if (_newCapacity <= _vanilla) return 0;

            var grown = 0;
            for (var i = 0; i < _fields.Count; i++)
            {
                var field = _fields[i];
                var array = ReadStatic(field);
                if (array == IntPtr.Zero) continue;
                var length = ArrayLength(array);
                if (length >= _newCapacity || length != _vanilla) continue;
                if (!GrowField(field, array, log)) continue;
                _grown++;
                grown++;
            }
            if (grown > 0) log?.Invoke($"[typetables] {_name} 被游戏重建后又扩：{grown} 张");
            return grown;
        }

        /// <summary>
        /// 自检/诊断：本种类"该扩的表"里，扩到位 / 还是原版 / 还没建 各几张。
        /// 名单只算扫到时长度 == 原版数、或被补扩过的那些，不含 Main 里长度本来就够大的数组。
        /// </summary>
        internal void Audit(out int total, out int grown, out int stale, out int missing,
                           List<string>? missingNames = null)
        {
            total = grown = stale = missing = 0;
            if (_newCapacity <= _vanilla) return;

            for (var i = 0; i < _candidates.Count; i++)
            {
                total++;
                var array = ReadStatic(_candidates[i]);
                if (array == IntPtr.Zero)
                {
                    missing++;
                    missingNames?.Add(FieldName(_candidates[i]));
                    continue;
                }
                var length = ArrayLength(array);
                if (length >= _newCapacity) grown++;
                else if (length == _vanilla) stale++;
            }
            // 扫到时还是 null、到现在也没建出来的也算"还没建"
            for (var i = 0; i < _pending.Count; i++)
            {
                total++;
                if (ReadStatic(_pending[i]) != IntPtr.Zero) continue;
                missing++;
                missingNames?.Add(FieldName(_pending[i]));
            }
        }

        private static string FieldName(IntPtr field)
        {
            try { return IL2CPP.il2cpp_field_get_name_(field) ?? "?"; }
            catch { return "?"; }
        }
    }

    /// <summary>自检：按原生名字读一张表的当前长度（0 = 类/字段/数组不存在）。</summary>
    public static int TableLength(string ns, string name, string? nested, string fieldName)
    {
        try
        {
            var cls = FindClass(new TableClass(ns, name, nested));
            if (cls == IntPtr.Zero) return 0;
            var field = IL2CPP.il2cpp_class_get_field_from_name(cls, fieldName);
            if (field == IntPtr.Zero) return 0;
            var array = ReadStatic(field);
            return ArrayLength(array);
        }
        catch { return 0; }
    }

    /// <summary>
    /// 自检：本种类里扩到位 / 还是原版 / 还没建 各几张。
    /// 只读原生字段与数组长度，不碰 interop 包装（<c>Nullable&lt;T&gt;[]</c> 的包装本身是坏的）。
    /// </summary>
    public static bool Audit(TableKind kind, out int total, out int grown, out int stale, out int missing,
                             List<string>? missingNames = null)
    {
        total = grown = stale = missing = 0;
        var grower = Find(kind);
        if (grower is null) return false;
        grower.Audit(out total, out grown, out stale, out missing, missingNames);
        return true;
    }
}
