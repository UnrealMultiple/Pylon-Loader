using System;
using Pylon.ModLoader.Tables;
using Terraria;
using BoolRow = Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<bool>;
using RowTable = Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<bool>>;

namespace Pylon.ModLoader;

/// <summary>
/// Main.tileMerge 是 bool[754][754] 的锯齿表：默认扩表只扩外层，让所有 mod 行
/// 指向同一条 754 长的原版短行，tileMerge[mod][mod] = true 直接写越界。
/// 这里逐行拆成独立数组；再钩 SetupTileMerge——游戏回主菜单会整表重建，
/// 先快照 mod 行/列字节，让游戏重建，再扩回并回放，mod 的合并设置才不会每进一次菜单丢一次。
/// </summary>
internal static class TileMergeFix
{
    public static void Install() => On.Terraria.Main.SetupTileMerge += SetupTileMerge;

    /// <summary>内容装配后一次：把当时的表修到位（行独立 + 长度对齐总类型数）。</summary>
    public static void FixToTotal()
    {
        try
        {
            var total = ModContent.NextType(ContentKind.Tile);
            if (total > VanillaCounts.Tile) Fix(total);
        }
        catch (Exception e)
        {
            Safe.Report("tileMerge 修正", e);
        }
    }

    private static void SetupTileMerge(On.Terraria.Main.orig_SetupTileMerge orig)
    {
        var total = ModContent.NextType(ContentKind.Tile);
        if (total <= VanillaCounts.Tile)
        {
            orig();
            return;
        }

        var outer = Main.tileMerge;
        if (outer is null || outer.Length < total || !Fix(outer, total))
        {
            // 表还没扩 / 行还没独立：没有可快照的 mod 合并数据，让游戏先建。
            orig();
            return;
        }

        var mods = total - VanillaCounts.Tile;
        var rows = new byte[mods * total];
        var cols = new byte[VanillaCounts.Tile * mods];
        Snapshot(outer, rows, cols, total, mods);

        orig();

        outer = Main.tileMerge;
        if (outer is null) return;
        if (outer.Length < total)
        {
            // 重建后的外层还是 754 长：零扩（新行引用保持 null，不能指到原版行上，正是本补丁要防的共享）。
            var grown = new RowTable(total);
            for (var i = 0; i < outer.Length; i++) grown[i] = outer[i];
            Main.tileMerge = grown;
            outer = grown;
        }

        if (!Fix(outer, total)) return;
        Restore(outer, rows, cols, total, mods);
    }

    /// <summary>
    /// 逐行确保独立且长度 = total。own = 原版行或已独立的 mod 行（前缀扩容）；
    /// null / 与泥土行共享的引用（扩表器的锅）一律零新建。完成过一次就快速返回。
    /// </summary>
    public static bool Fix(int total)
    {
        var outer = Main.tileMerge;
        return outer is not null && outer.Length >= total && Fix(outer, total);
    }

    private static bool Fix(RowTable outer, int total)
    {
        var first = At(outer, 0);
        var last = At(outer, total - 1);
        if (first is not null && last is not null && last.Pointer != first.Pointer &&
            Length(last) >= total && Length(first) >= total)
            return true;

        // 泥土行的原引用先存下来：i=0 的行马上会被替换，mod 行的"共享"判定要对着旧引用比。
        var dirt = first;
        for (var i = 0; i < total; i++)
        {
            var row = At(outer, i);
            var own = row is not null && (i < VanillaCounts.Tile || row.Pointer != dirt?.Pointer);
            if (own && Length(row) >= total) continue;

            outer[i] = own ? ResizedCopy(row!, total) : NewRow(total);
        }

        return true;
    }

    // ===================== 快照 / 回放 =====================

    /// <summary>mod 行整行 + 原版行的 mod 列段（行数组里偏移 754 之后那截）。</summary>
    private static void Snapshot(RowTable outer, byte[] rows, byte[] cols, int total, int mods)
    {
        for (var t = 0; t < mods; t++)
        {
            var row = At(outer, VanillaCounts.Tile + t);
            if (row is null || Length(row) < total) continue;
            Copy(row.Pointer, 0, rows, t * total, total, intoRow: false);
        }

        for (var v = 0; v < VanillaCounts.Tile; v++)
        {
            var row = At(outer, v);
            if (row is null || Length(row) < total) continue;
            Copy(row.Pointer, VanillaCounts.Tile, cols, v * mods, mods, intoRow: false);
        }
    }

    private static void Restore(RowTable outer, byte[] rows, byte[] cols, int total, int mods)
    {
        for (var t = 0; t < mods; t++)
        {
            var row = At(outer, VanillaCounts.Tile + t);
            if (row is null || Length(row) < total) continue;
            Copy(row.Pointer, 0, rows, t * total, total, intoRow: true);
        }

        for (var v = 0; v < VanillaCounts.Tile; v++)
        {
            var row = At(outer, v);
            if (row is null || Length(row) < total) continue;
            Copy(row.Pointer, VanillaCounts.Tile, cols, v * mods, mods, intoRow: true);
        }
    }

    // ===================== 表访问 =====================

    /// <summary>行读取：包装可能包着零指针（外层扩表器的产物），统一归成 null。</summary>
    private static BoolRow? At(RowTable outer, int index)
    {
        var row = outer[index];
        return row is not null && row.Pointer != IntPtr.Zero ? row : null;
    }

    private static int Length(BoolRow? row) => row is null ? 0 : ArrayGrow.Length(row.Pointer);

    private static BoolRow NewRow(int total) => new(total);

    /// <summary>原版短行扩容：前缀拷贝，尾部零（bool 零 = 不合并，与游戏建表初值一致）。</summary>
    private static unsafe BoolRow ResizedCopy(BoolRow row, int total)
    {
        var fresh = NewRow(total);
        var length = Math.Min(Length(row), total);
        if (length > 0)
            Buffer.MemoryCopy(ArrayGrow.Data(row.Pointer), ArrayGrow.Data(fresh.Pointer), total, length);
        return fresh;
    }

    private static unsafe void Copy(IntPtr row, int rowOffset, byte[] buffer, int bufferOffset, int count, bool intoRow)
    {
        fixed (byte* pinned = buffer)
        {
            var at = pinned + bufferOffset;
            var data = ArrayGrow.Data(row) + rowOffset;
            if (intoRow) Buffer.MemoryCopy(at, data, count, count);
            else Buffer.MemoryCopy(data, at, count, count);
        }
    }
}
