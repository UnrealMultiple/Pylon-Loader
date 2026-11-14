using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Terraria;

namespace Pylon.ModLoader;

/// <summary>
/// TileData.TileLists 键表换入：游戏把方块内容按键（type&lt;&lt;12 | bHeader2&lt;&lt;4 | frame）
/// 内嵌进 754×4096 的定义列表表，mod 方块的键落在表外（读写都越界）。钩 Allocate：
/// 原版分配后换成自管表（total&lt;&lt;12 条），注册/查表从此走这张表。
/// </summary>
internal static class TileLists
{
    private const int ClassStaticFieldsOffset = 0xB8; // Il2CppClass::static_fields（同 TileSavePipeline）
    private const int ListsSlotOffset = 0x90;         // TileLists 在静态块里的槽（refs/dump.cs）

    private static bool _installed;
    private static bool _swapped;

    private static IntPtr _table;
    private static int _entries;

    private static IModLog Log => LoaderHost.Current.Log;

    /// <summary>TileTable.Build 之后调用：有 mod 方块才装；装不上不致命（等于没有 P1-5）。</summary>
    internal static void Build(IModLog log)
    {
        if (_installed || ModContent.Count(ContentKind.Tile) <= 0) return;
        _installed = true;

        try
        {
            unsafe
            {
                var statics = StaticsBlock();
                if (statics == IntPtr.Zero)
                    throw new InvalidOperationException("TileData 静态块未就绪（cctor 没跑？）");
            }

            On.Terraria.TileData.Allocate += Allocate;
            log.Info("[tile] 键表已挂（TileLists 每次进世界扩到全部类型）");
        }
        catch (Exception e)
        {
            ContentErrors.Report("[tile] TileLists 换入安装失败（mod 方块键仍会越界）：" + Safe.Describe(e));
        }
    }

    private static unsafe IntPtr StaticsBlock()
    {
        var klass = Il2CppClassPointerStore<TileData>.NativeClassPtr;
        if (klass == IntPtr.Zero) return IntPtr.Zero;
        // 安装期 TileData 的 cctor 可能还没跑（静态块未分配）——先强制初始化再读槽位。
        try { IL2CPP.il2cpp_runtime_class_init(klass); } catch { }
        return *(IntPtr*)((byte*)klass + ClassStaticFieldsOffset);
    }

    private static unsafe void Allocate(On.Terraria.TileData.orig_Allocate orig, TileData self, int x, int y)
    {
        orig(self, x, y);

        try
        {
            Swap();
        }
        catch (Exception e)
        {
            ContentErrors.Report("[tile] TileLists 换表失败：" + Safe.Describe(e));
        }
    }

    private static unsafe void Swap()
    {
        var total = VanillaCounts.Tile + ModContent.Count(ContentKind.Tile);
        if (total <= VanillaCounts.Tile) return;

        var entries = total << 12;
        var bytes = (long)entries * sizeof(uint);
        if (_table == IntPtr.Zero || _entries < entries)
        {
            _table = _table == IntPtr.Zero ? Marshal.AllocHGlobal((IntPtr)bytes) : Marshal.ReAllocHGlobal(_table, (IntPtr)bytes);
            _entries = entries;
        }

        var statics = StaticsBlock();
        if (statics == IntPtr.Zero) return;
        var slot = (uint**)((byte*)statics + ListsSlotOffset);

        var ours = (uint*)_table;
        if (*slot == ours) return; // 游戏没动指针（同一次分配内重入）

        // 0xFF = TileBufferNoEntry；原版段照抄游戏刚初始化的那张表。
        Unsafe.InitBlockUnaligned(ours, 0xFF, (uint)bytes);
        var old = *slot;
        if (old != null) Buffer.MemoryCopy(old, ours, bytes, (long)(VanillaCounts.Tile << 12) * sizeof(uint));

        *slot = ours;
        if (*slot != ours) return;

        if (!_swapped)
        {
            _swapped = true;
            Log.Info($"[tile] TileLists 已换入（{entries} 条 = {total} 类型，原版段 {VanillaCounts.Tile << 12} 条）");
        }
    }
}
