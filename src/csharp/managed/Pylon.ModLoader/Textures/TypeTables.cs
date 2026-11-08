using System.Diagnostics;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

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

    // 血腥粒子：平行表都在 GoreID.Sets 与 ChildSafety 里，长度 = TextureAssets.Gore 的原版长度。
    private static readonly TableClass[] GoreClasses =
    [
        new("Terraria.GameContent", "TextureAssets"),
        new("Terraria.ID", "GoreID", "Sets"),
        new("Terraria.GameContent", "ChildSafety"),
    ];

    // 云：只有一张贴图表，没有 Sets 平行表。
    private static readonly TableClass[] CloudClasses =
    [
        new("Terraria.GameContent", "TextureAssets"),
    ];

    // 发型：PlayerHair 与 PlayerHairAlt 长度相同，会被一起扩到同一个目标长度。
    private static readonly TableClass[] HairClasses =
    [
        new("Terraria.GameContent", "TextureAssets"),
    ];

    // 水样式：Liquid 与 LiquidSlope 长度相同，会被一起扩到同一个目标长度。
    private static readonly TableClass[] WaterClasses =
    [
        new("Terraria.GameContent", "TextureAssets"),
    ];

    // 瀑布样式：贴图表是 WaterfallManager 的私有静态字段（interop 只暴露这一张，没有 ID 平行表）。
    private static readonly TableClass[] WaterfallClasses = [new("Terraria", "WaterfallManager")];

    // 装备贴图（盔甲 + 饰品 / 翅膀 / 胡子）：都只有一张贴图表，没有 Sets 平行表。
    private static readonly TableClass[] EquipClasses = [new("Terraria.GameContent", "TextureAssets")];

    public enum TableKind
    {
        Item, Projectile, Npc, Buff, Tile, Wall, Gore, Cloud, Hair, WaterStyle, Waterfall,
        ArmorHead, ArmorBody, ArmorLeg, ArmorBodyComposite, ArmorArm,
        EquipHandOn, EquipHandOff, EquipBack, EquipFront, EquipShoes, EquipWaist,
        EquipWings, EquipShield, EquipNeck, EquipFace, EquipBalloon, EquipBeard,
        EquipHandOnComposite, EquipHandOffComposite,
        TreeTop, TreeBranch, TreeWood,
    }

    private static TableClass[] Classes(TableKind kind) => kind switch
    {
        TableKind.Item => ItemClasses,
        TableKind.Projectile => ProjectileClasses,
        TableKind.Npc => NpcClasses,
        TableKind.Buff => BuffClasses,
        TableKind.Tile => TileClasses,
        TableKind.Wall => WallClasses,
        TableKind.Cloud => CloudClasses,
        TableKind.Hair => HairClasses,
        TableKind.WaterStyle => WaterClasses,
        TableKind.Waterfall => WaterfallClasses,
        TableKind.ArmorHead => EquipClasses,
        TableKind.ArmorBody => EquipClasses,
        TableKind.ArmorLeg => EquipClasses,
        TableKind.EquipHandOn => EquipClasses,
        TableKind.EquipHandOff => EquipClasses,
        TableKind.EquipBack => EquipClasses,
        TableKind.EquipFront => EquipClasses,
        TableKind.EquipShoes => EquipClasses,
        TableKind.EquipWaist => EquipClasses,
        TableKind.EquipWings => EquipClasses,
        TableKind.EquipShield => EquipClasses,
        TableKind.EquipNeck => EquipClasses,
        TableKind.EquipFace => EquipClasses,
        TableKind.EquipBalloon => EquipClasses,
        TableKind.EquipBeard => EquipClasses,
        TableKind.ArmorBodyComposite => EquipClasses,
        TableKind.ArmorArm => EquipClasses,
        TableKind.EquipHandOnComposite => EquipClasses,
        TableKind.EquipHandOffComposite => EquipClasses,
        TableKind.TreeTop => EquipClasses,
        TableKind.TreeBranch => EquipClasses,
        TableKind.TreeWood => EquipClasses,
        _ => GoreClasses,
    };

    private static string Name(TableKind kind) => kind switch
    {
        TableKind.Item => "物品",
        TableKind.Projectile => "弹幕",
        TableKind.Npc => "NPC",
        TableKind.Buff => "Buff",
        TableKind.Tile => "方块",
        TableKind.Wall => "墙壁",
        TableKind.Cloud => "云",
        TableKind.Hair => "发型",
        TableKind.WaterStyle => "水样式",
        TableKind.Waterfall => "瀑布贴图",
        TableKind.ArmorHead => "头盔贴图",
        TableKind.ArmorBody => "胸甲贴图",
        TableKind.ArmorLeg => "护腿贴图",
        TableKind.EquipHandOn => "手部（前面）贴图",
        TableKind.EquipHandOff => "手部（后面）贴图",
        TableKind.EquipBack => "背部贴图",
        TableKind.EquipFront => "胸前贴图",
        TableKind.EquipShoes => "鞋贴图",
        TableKind.EquipWaist => "腰贴图",
        TableKind.EquipWings => "翅膀贴图",
        TableKind.EquipShield => "盾贴图",
        TableKind.EquipNeck => "项链贴图",
        TableKind.EquipFace => "面部贴图",
        TableKind.EquipBalloon => "气球贴图",
        TableKind.EquipBeard => "胡子贴图",
        TableKind.ArmorBodyComposite => "胸甲复合贴图",
        TableKind.ArmorArm => "手臂贴图",
        TableKind.EquipHandOnComposite => "手部前复合贴图",
        TableKind.EquipHandOffComposite => "手部后复合贴图",
        TableKind.TreeTop => "树顶贴图",
        TableKind.TreeBranch => "树枝贴图",
        TableKind.TreeWood => "树干贴图",
        _ => "血腥粒子",
    };

    private static int VanillaCount(TableKind kind) => kind switch
    {
        TableKind.Item => Terraria.ID.ItemID.Count,
        TableKind.Projectile => Terraria.ID.ProjectileID.Count,
        TableKind.Npc => Terraria.ID.NPCID.Count,
        TableKind.Buff => Terraria.ID.BuffID.Count,
        TableKind.Tile => 754,
        TableKind.Wall => 367,
        TableKind.Cloud => CloudVanilla,
        TableKind.Hair => HairVanilla,
        TableKind.WaterStyle => WaterVanilla,
        TableKind.Waterfall => WaterfallVanilla,
        TableKind.ArmorHead => EquipVanilla(0, TextureTable.ArmorHead),
        TableKind.ArmorBody => EquipVanilla(1, TextureTable.ArmorBody),
        TableKind.ArmorLeg => EquipVanilla(2, TextureTable.ArmorLeg),
        TableKind.EquipHandOn => EquipVanilla(3, TextureTable.AccHandsOn),
        TableKind.EquipHandOff => EquipVanilla(4, TextureTable.AccHandsOff),
        TableKind.EquipBack => EquipVanilla(5, TextureTable.AccBack),
        TableKind.EquipFront => EquipVanilla(6, TextureTable.AccFront),
        TableKind.EquipShoes => EquipVanilla(7, TextureTable.AccShoes),
        TableKind.EquipWaist => EquipVanilla(8, TextureTable.AccWaist),
        TableKind.EquipWings => EquipVanilla(9, TextureTable.Wings),
        TableKind.EquipShield => EquipVanilla(10, TextureTable.AccShield),
        TableKind.EquipNeck => EquipVanilla(11, TextureTable.AccNeck),
        TableKind.EquipFace => EquipVanilla(12, TextureTable.AccFace),
        TableKind.EquipBalloon => EquipVanilla(13, TextureTable.AccBalloon),
        TableKind.EquipBeard => EquipVanilla(14, TextureTable.AccBeard),
        TableKind.ArmorBodyComposite => EquipVanilla(15, TextureTable.ArmorBodyComposite),
        TableKind.ArmorArm => EquipVanilla(16, TextureTable.ArmorArm),
        TableKind.EquipHandOnComposite => EquipVanilla(17, TextureTable.AccHandsOnComposite),
        TableKind.EquipHandOffComposite => EquipVanilla(18, TextureTable.AccHandsOffComposite),
        TableKind.TreeTop => EquipVanilla(19, TextureTable.TreeTop),
        TableKind.TreeBranch => EquipVanilla(20, TextureTable.TreeBranch),
        TableKind.TreeWood => EquipVanilla(21, TextureTable.TreeWood),
        _ => GoreVanilla,
    };

    private static int _goreVanilla;

    /// <summary>
    /// 原版 gore 表长：GoreID 没有编译期 Count，只能在扩表之前按表长捕获一次。
    /// 读到 0（表还没建）时不缓存，下一轮再读。
    /// </summary>
    internal static int GoreVanilla
    {
        get
        {
            if (_goreVanilla > 0) return _goreVanilla;

            var length = TextureSlots.Length(TextureTable.Gore);
            if (length > 0) _goreVanilla = length;
            return _goreVanilla;
        }
    }

    private static int _cloudVanilla;

    /// <summary>原版云表长：云的编号没有编译期常量，只能在扩表之前按表长捕获一次。</summary>
    internal static int CloudVanilla
    {
        get
        {
            if (_cloudVanilla > 0) return _cloudVanilla;

            var length = TextureSlots.Length(TextureTable.Cloud);
            if (length > 0) _cloudVanilla = length;
            return _cloudVanilla;
        }
    }

    private static int _hairVanilla;

    /// <summary>原版发型表长：发型编号没有编译期常量，只能在扩表之前按表长捕获一次。</summary>
    internal static int HairVanilla
    {
        get
        {
            if (_hairVanilla > 0) return _hairVanilla;

            var length = TextureSlots.Length(TextureTable.PlayerHair);
            if (length > 0) _hairVanilla = length;
            return _hairVanilla;
        }
    }

    private static readonly int[] EquipVanillaCache = new int[24];

    /// <summary>
    /// 原版装备贴图表长：装备编号没有编译期常量，只能在扩表之前按表长捕获一次（按部位缓存）。
    /// </summary>
    internal static int EquipVanilla(int part, TextureTable table)
    {
        if ((uint)part < (uint)EquipVanillaCache.Length && EquipVanillaCache[part] > 0) return EquipVanillaCache[part];

        var length = TextureSlots.Length(table);
        if (length > 0 && (uint)part < (uint)EquipVanillaCache.Length) EquipVanillaCache[part] = length;
        return length > 0 ? length : 0;
    }

    private static int _waterVanilla;

    /// <summary>原版水样式表长：水样式没有编译期 Count，只能在扩表之前按表长捕获一次。</summary>
    internal static int WaterVanilla
    {
        get
        {
            if (_waterVanilla > 0) return _waterVanilla;

            var length = TextureSlots.Length(TextureTable.Liquid);
            if (length > 0) _waterVanilla = length;
            return _waterVanilla;
        }
    }

    private static int _waterfallVanilla;

    /// <summary>
    /// 原版瀑布表长：<c>WaterfallManager.waterfallTexture</c> 在 interop 代理里就是普通公开属性，
    /// 与其它表一样在扩表之前捕获一次（读到 0 说明表还没建，不缓存）。
    /// </summary>
    internal static int WaterfallVanilla
    {
        get
        {
            if (_waterfallVanilla > 0) return _waterfallVanilla;

            var array = Terraria.WaterfallManager.waterfallTexture;
            if (array is { Length: > 0 }) _waterfallVanilla = array.Length;
            return _waterfallVanilla;
        }
    }

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

    /// <summary>
    /// 目录表用的字段级指针访问（只给"按类名 + 字段名找表"这段老逻辑用；管线扩容走 <see cref="ArrayGrow"/>）。
    /// 偏移见 refs/il2cpp.h。
    /// </summary>
    private static unsafe class Meta
    {
        private const int ClassElementClass = 0x40;
        private const int ClassFields = 0x80;
        private const int ClassStaticFields = 0xB8;
        private const int ClassThreadStaticOffset = 0x110;
        private const int ClassFieldCount = 0x120;
        private const int FieldNameOff = 0x00;
        private const int FieldTypeOff = 0x08;
        private const int FieldParentOff = 0x10;
        private const int FieldOffsetOff = 0x18;
        private const int TypeBitsOff = 0x08;
        private const int ArrLength = 0x18;
        private const int ArrDataOff = 0x20;
        private const int ThreadStatic = -1;

        private static byte* B(IntPtr p) => (byte*)p;

        internal static int FieldCount(IntPtr klass) => klass == IntPtr.Zero ? 0 : *(ushort*)(B(klass) + ClassFieldCount);

        internal static IntPtr FieldAt(IntPtr klass, int index)
        {
            if (klass == IntPtr.Zero) return IntPtr.Zero;
            var table = *(IntPtr*)(B(klass) + ClassFields);
            return table == IntPtr.Zero ? IntPtr.Zero : ((IntPtr*)table)[index];
        }

        internal static string? FieldName(IntPtr field) => field == IntPtr.Zero ? null : IL2CPP.il2cpp_field_get_name_(field);
        internal static IntPtr TypeOf(IntPtr field) => field == IntPtr.Zero ? IntPtr.Zero : *(IntPtr*)(B(field) + FieldTypeOff);
        internal static int OffsetOf(IntPtr field) => field == IntPtr.Zero ? 0 : *(int*)(B(field) + FieldOffsetOff);
        internal static IntPtr ParentOf(IntPtr field) => field == IntPtr.Zero ? IntPtr.Zero : *(IntPtr*)(B(field) + FieldParentOff);

        internal static uint AttrsOf(IntPtr field)
        {
            var type = TypeOf(field);
            return type == IntPtr.Zero ? 0u : *(uint*)(B(type) + TypeBitsOff) & 0xFFFFu;
        }

        internal static int TypeKindOf(IntPtr type) =>
            type == IntPtr.Zero ? -1 : (int)((*(uint*)(B(type) + TypeBitsOff) >> 16) & 0xFFu);

        /// <summary>字段类型是不是 T[]（走 il2cpp 的 type 枚举，不自己解 Il2CppType.bits）。</summary>
        internal static bool IsSzArray(IntPtr field) =>
            IL2CPP.il2cpp_type_get_type(IL2CPP.il2cpp_field_get_type(field)) == TypeSzArray;

        internal static bool IsPlainStatic(IntPtr field)
        {
            var attrs = (uint)IL2CPP.il2cpp_field_get_flags(field);
            if ((attrs & FieldStatic) == 0 || (attrs & FieldLiteral) != 0) return false;
            return unchecked((int)IL2CPP.il2cpp_field_get_offset(field)) != ThreadStatic;
        }

        internal static bool NameIs(IntPtr field, string text) =>
            string.Equals(IL2CPP.il2cpp_field_get_name_(field), text, StringComparison.Ordinal);

        internal static IntPtr StaticAddress(IntPtr klass, IntPtr field)
        {
            if (klass == IntPtr.Zero || field == IntPtr.Zero) return IntPtr.Zero;
            var block = *(IntPtr*)(B(klass) + ClassStaticFields);
            return block == IntPtr.Zero ? IntPtr.Zero : block + OffsetOf(field);
        }

        /// <summary>
        /// 读静态字段。走 il2cpp 自己的 API（<c>field_static_get_value</c>）：
        /// 它内部处理 static_fields 基址与 thread-static 两种存储，比自己算地址稳。
        /// </summary>
        internal static unsafe IntPtr ReadStaticPtr(IntPtr klass, IntPtr field)
        {
            if (field == IntPtr.Zero) return IntPtr.Zero;
            var value = IntPtr.Zero;
            IL2CPP.il2cpp_field_static_get_value(field, &value);
            return value;
        }

        /// <summary>写静态字段（同样交给 il2cpp，引用类型的写屏障由它负责）。</summary>
        internal static unsafe void WriteStaticPtr(IntPtr klass, IntPtr field, IntPtr value)
        {
            if (field == IntPtr.Zero) return;
            IL2CPP.il2cpp_field_static_set_value(field, (void*)value);
        }

        internal static int Items(IntPtr array) => array == IntPtr.Zero ? 0 : (int)*(long*)(B(array) + ArrLength);
        internal static byte* Data(IntPtr array) => array == IntPtr.Zero ? null : B(array) + ArrDataOff;
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
        catch { /* il2cpp 镜像列表还不稳定：扫不到就当没找到，调用方会再试 */ }
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

    /// <summary>字段类型是不是 T[] 一维数组（多维 [,] 是 ARRAY，不算）。</summary>
    private static bool IsArrayField(IntPtr field) => Meta.IsSzArray(field);

    private static bool IsSkipped(TableClass spec, IntPtr field)
    {
        var skip = spec.Skip;
        if (skip is not { Length: > 0 }) return false;
        foreach (var candidate in skip)
            if (Meta.NameIs(field, candidate)) return true;
        return false;
    }

    /// <summary>
    /// 收集这个类的全部"静态 T[] 字段"。走 il2cpp 自己的字段枚举迭代器 —— 它会先跑类初始化、
    /// 也会过滤掉 <c>klass-&gt;fields</c> 里那些还没定型的槽位；自己按下标解 <c>klass-&gt;fields</c>
    /// 曾经在真机上扫到过非 FieldInfo 的指针，扩表直接把内存写坏（fault addr 都是 ASCII 字符串）。
    /// </summary>
    private static void CollectFields(IntPtr cls, TableClass spec, List<IntPtr> into)
    {
        var iter = IntPtr.Zero;
        while (true)
        {
            var field = IL2CPP.il2cpp_class_get_fields(cls, ref iter);
            if (field == IntPtr.Zero) return;
            if (!Meta.IsPlainStatic(field)) continue;
            if (IsSkipped(spec, field)) continue;
            if (!IsArrayField(field)) continue;
            into.Add(field);
        }
    }

    /// <summary>按名字找 FieldInfo（il2cpp 自带的名字查找）。</summary>
    private static IntPtr FindField(IntPtr cls, string name)
    {
        try { return IL2CPP.il2cpp_class_get_field_from_name(cls, name); }
        catch { return IntPtr.Zero; }
    }

    /// <summary>读静态字段里的数组对象指针（0 = 还没建 / 不是引用）。</summary>
    private static IntPtr ReadStatic(IntPtr field) =>
        Meta.ReadStaticPtr(Meta.ParentOf(field), field);

    /// <summary>写回静态字段（落值 + GC 写屏障）。</summary>
    private static void WriteStatic(IntPtr field, IntPtr array) =>
        Meta.WriteStaticPtr(Meta.ParentOf(field), field, array);

    private static int ArrayLength(IntPtr array) => Meta.Items(array);

    /// <summary>数组元素起始地址。</summary>
    private static unsafe byte* ArrayData(IntPtr array) => Meta.Data(array);

    /// <summary>
    /// 值类型元素按 <c>il2cpp_class_value_size</c> 整块拷；引用元素逐个走 GC 写屏障。
    /// <para>
    /// 引用元素<b>不能</b>用 memcpy 整块搬：那等于绕过增量 GC 的写屏障。新数组如果在标记
    /// 阶段被分配（表一大就会分配很多次，必然撞上），它已经被标黑、不会再被扫描，此时用
    /// memcpy 塞进去的指针没人去标记，下一轮回收就可能把那个对象收走 —— 现场表现就是十几
    /// 秒后一段完全无关的游戏代码读到野指针 SIGSEGV。<c>TypeTables::growArray</c>
    /// 对引用元素改用 <c>Array.Copy</c> / <c>Array.SetValue</c>，要的就是这个屏障。
    /// </para>
    /// <paramref name="fillWithMode"/> 时尾部填"原数组出现最多的值"（方块/墙壁必须用它：
    /// 填 0 会把新方块变成"实心泥土"）。
    /// </summary>
    internal static IntPtr GrowArray(IntPtr oldArray, int newCapacity, bool fillWithMode = false) =>
        ArrayGrow.Grow(oldArray, newCapacity, fillWithMode);

    /// <summary>
    /// 精确扩一张 interop 静态贴图表：
    /// 只动这一张表，不做类级扫描 —— 云 / 发型这类"清单里只有一两个字段"的种类
    /// 用 TableGrower 全类扫描会被 budget 截断，漏扩就是主菜单第一帧空指针。
    /// 已经够长返回 false（不重扩）；表还没建（null / 长 0）返回 false，调用方 Refresh 重试。
    /// </summary>
    public static bool GrowAssetTable<T>(ref Il2CppReferenceArray<T>? table, int total)
        where T : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
    {
        if (total <= 0) return false;
        IntPtr old;
        try { old = table?.Pointer ?? IntPtr.Zero; } catch { return false; }
        if (old == IntPtr.Zero) return false;
        if (ArrayLength(old) >= total) return false;

        var grown = GrowArray(old, total);
        if (grown == IntPtr.Zero) return false;
        table = new Il2CppReferenceArray<T>(grown);
        return true;
    }

    /// <summary>
    /// 按类名 + 字段名把一张静态数组扩到 total。给"游戏在世界加载时重建回原版长度"的表用
    /// （典型：Lang._npcNameCache —— 重写名字前必须先把它扩回来，否则写槽直接越界跳过）。
    /// </summary>
    internal static bool GrowStaticArray(string ns, string cls, string fieldName, int total)
    {
        if (total <= 0) return false;
        try
        {
            var klass = FindClass(new TableClass(ns, cls));
            if (klass == IntPtr.Zero) return false;
            var field = FindField(klass, fieldName);
            if (field == IntPtr.Zero) return false;
            var old = ReadStatic(field);
            if (old == IntPtr.Zero || ArrayLength(old) >= total) return false;
            var grown = GrowArray(old, total);
            if (grown == IntPtr.Zero) return false;
            WriteStatic(field, grown);
            return true;
        }
        catch
        {
            return false;
        }
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
            
                    log?.Invoke($"[grow] {_name} {FieldLabel(field)} len={_vanilla}→{_newCapacity} ptr=0x{array.ToInt64():x}");
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

        /// <summary>诊断用：<c>类名.字段名</c>，扩表流水账里标明是哪一张。</summary>
        private static string FieldLabel(IntPtr field)
        {
            try
            {
                var cls = Meta.ParentOf(field);
                var clsName = cls == IntPtr.Zero ? "?" : IL2CPP.il2cpp_class_get_name_(cls) ?? "?";
                return clsName + "." + (IL2CPP.il2cpp_field_get_name_(field) ?? "?");
            }
            catch { return "?"; }
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
            var field = FindField(cls, fieldName);
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
