using System;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Terraria;

namespace Pylon.ModLoader.Tables;

/// <summary>
/// 实例表扩容：TableGrower 只认静态数组字段，四类表不在射程——mod 类型号一进来就是越界写
/// （il2cpp 数组越界不抛异常，直接读写越界内存，表现为崩溃或数据损坏）：
/// 1. Player.npcTypeNoAggro / ownedProjectileCounts：每玩家一张，NPC 选目标 / 仆从弹每帧写；
/// 2. Projectile.perIDStaticNPCImmunity：静态二维数组（type × NPC），ID 静态免疫用；
/// 3. QuickStacking.MatchingItemTypeDestinationList.firstEntryForType / ItemFilters.MiscFallback._fitsFilterByItemType：
///    快速堆叠与"杂项"过滤器的每实例缓存，嵌套类构造器钩 + 静态 scratch 实例补扩；
/// 4. EmergencyStacking._groupLookup：getter 惰性创建，静态字段扫描永远扫不到，必须钩 get_GroupLookup
///    在返回前扩（FindBestTransfers 同帧就查，帧后补扩对 mod ID 太晚）。
/// 新尾部一律零填充（"还不知道 / 还没有"语义）：仅当现长恰为原版数时扩。
/// </summary>
internal static class InstanceTables
{
    private static IModLog Log => LoaderHost.Current.Log;

    public static void Install()
    {
        // Player 只有一个无参构造器；interop 支持构造器钩子（HookGen 有测试先例）。
        On.Terraria.Player.ctor += PlayerCtor;
        On.Terraria.SceneMetrics.ctor += MetricsCtor;
        On.Terraria.GameContent.EmergencyStacking.get_GroupLookup += GroupLookup;
        On.Terraria.GameContent.QuickStacking.MatchingItemTypeDestinationList.ctor += DestinationListCtor;
        On.Terraria.GameContent.Creative.ItemFilters.MiscFallback.ctor += MiscFallbackCtor;
        On.GUICrafting.ctor += CraftingCtor;

        GrowExistingPlayers();
        GrowExistingMetrics();
        GrowScratchDestinationList();
        GrowImmunity();
        GrowExistingCrafting();

        Log.Info("[tables] 实例表已挂（noAggro / ownedCounts / adjTile / 免疫二维表 / 堆叠目的地 / 杂项过滤器 / groupLookup / 制作界面 oldAdjTile / 场景指标 tileCounts）");
    }

    // ===================== 制作界面快照表 =====================

    /// <summary>
    /// GUICrafting.oldAdjTile：制作界面打开时的 Player.adjTile 快照副本，构造器只建 754 长——
    /// 界面打开后新槽才认 mod 制作站。
    /// </summary>
    private static void CraftingCtor(On.GUICrafting.orig_ctor orig, GUICrafting self)
    {
        orig(self);
        GrowCrafting(self);
    }

    /// <summary>安装时 GUIInstance.Active 里已存在的界面补一遍（构造器钩只管新建的）。</summary>
    private static void GrowExistingCrafting()
    {
        try
        {
            GrowCrafting(GUIInstance.Active?.GUICrafting);
        }
        catch (Exception e)
        {
            Safe.Report("GUICrafting 实例表扩容", e);
        }
    }

    private static void GrowCrafting(GUICrafting? crafting)
    {
        if (crafting is null) return;

        try
        {
            if (GrowToVanilla(crafting.oldAdjTile, VanillaCounts.Tile, ModContent.NextType(ContentKind.Tile), out var grown))
                crafting.oldAdjTile = grown;
        }
        catch (Exception e)
        {
            Safe.Report("GUICrafting.oldAdjTile", e);
        }
    }

    // ===================== Player 每实例表 =====================

    private static void PlayerCtor(On.Terraria.Player.orig_ctor orig, Player self)
    {
        orig(self);
        GrowPlayerTables(self);
    }

    /// <summary>安装时 Main.player 里已存在的实例补一遍（构造器钩只管新建的）。</summary>
    private static void GrowExistingPlayers()
    {
        var players = Main.player;
        if (players is null) return;
        for (var i = 0; i < players.Length; i++) GrowPlayerTables(players[i]);
    }

    private static void GrowPlayerTables(Player? player)
    {
        if (player is null) return;

        try
        {
            if (GrowToVanilla(player.npcTypeNoAggro, VanillaCounts.Npc, ModContent.NextType(ContentKind.Npc), out var noAggro))
                player.npcTypeNoAggro = noAggro;

            if (GrowToVanilla(player.ownedProjectileCounts, VanillaCounts.Projectile, ModContent.NextType(ContentKind.Projectile), out var owned))
                player.ownedProjectileCounts = owned;

            // 读档/重开会新建 Player，构造器钩是 adjTile 唯一可靠的补扩点
            //（TilePipeline.Maintain 的补扩是一次性门控，盖不住后来的实例）。
            if (GrowToVanilla(player.adjTile, VanillaCounts.Tile, ModContent.NextType(ContentKind.Tile), out var adj))
                player.adjTile = adj;
        }
        catch (Exception e)
        {
            Safe.Report("Player 实例表扩容", e);
        }
    }

    // ===================== 场景指标 =====================

    /// <summary>
    /// SceneMetrics._tileCounts：ScanTiles 对 mod 方块号无界递增，不扩就是越界写堆（三路补扩）。
    /// </summary>
    private static void MetricsCtor(On.Terraria.SceneMetrics.orig_ctor orig, Terraria.SceneMetrics self)
    {
        orig(self);
        GrowMetrics(self);
    }

    private static void GrowExistingMetrics()
    {
        try
        {
            GrowMetrics(Main.SceneMetrics);
            GrowMetrics(Main.PlayerSceneMetrics);
            GrowMetrics(Main._cameraSceneMetrics);
        }
        catch (Exception e)
        {
            Safe.Report("SceneMetrics 实例表扩容", e);
        }
    }

    private static void GrowMetrics(Terraria.SceneMetrics? metrics)
    {
        if (metrics is null) return;

        try
        {
            if (GrowToVanilla(metrics._tileCounts, VanillaCounts.Tile, ModContent.NextType(ContentKind.Tile), out var grown))
                metrics._tileCounts = grown;
        }
        catch (Exception e)
        {
            Safe.Report("SceneMetrics._tileCounts", e);
        }
    }

    // ===================== 二维免疫表 =====================

    /// <summary>
    /// Projectile.perIDStaticNPCImmunity（uint[弹幕类型, NPC 数]）。代理对二维数组生成不了包装
    /// （属性退化成 Il2CppObjectBase），读写都走数组对象本身，写回交给生成的静态属性；
    /// il2cpp 行是连续存储（索引 = type × dim1 + npc），旧数组整体就是新数组的前缀。
    /// </summary>
    private static unsafe void GrowImmunity()
    {
        try
        {
            var value = Projectile.perIDStaticNPCImmunity?.Pointer ?? IntPtr.Zero;
            if (value == IntPtr.Zero) return;

            // Il2CppArray：对象头 2 指针 + bounds + max_length（布局见 ArrayGrow）；bounds 条目 16 字节。
            var bounds = *(IntPtr*)(value + 0x10);
            if (bounds == IntPtr.Zero) return;
            var dim0 = *(ulong*)bounds;
            var lower0 = *(int*)(bounds + 8);
            var dim1 = *(ulong*)(bounds + 16);
            var lower1 = *(int*)(bounds + 24);
            if (dim0 != (ulong)VanillaCounts.Projectile || dim1 == 0) return;

            var total = ModContent.NextType(ContentKind.Projectile);
            if (total <= VanillaCounts.Projectile) return;

            var arrayClass = IL2CPP.il2cpp_object_get_class(value);
            var lengths = stackalloc ulong[2];
            lengths[0] = (ulong)total;
            lengths[1] = dim1;
            var lowers = stackalloc ulong[2];
            lowers[0] = (ulong)lower0;
            lowers[1] = (ulong)lower1;

            var grown = IL2CPP.il2cpp_array_new_full(arrayClass, ref *lengths, ref *lowers);
            if (grown == IntPtr.Zero) return;

            var copyBytes = (long)dim0 * (long)dim1 * sizeof(uint);
            Buffer.MemoryCopy((void*)(value + 0x20), (void*)(grown + 0x20), (long)total * (long)dim1 * sizeof(uint), copyBytes);
            Projectile.perIDStaticNPCImmunity = new Il2CppObjectBase(grown);

            Log.Info($"[tables] perIDStaticNPCImmunity {dim0}x{dim1} → {total}x{dim1}");
        }
        catch (Exception e)
        {
            Safe.Report("Projectile.perIDStaticNPCImmunity", e);
        }
    }

    // ===================== 堆叠 / 过滤器 =====================

    private static void DestinationListCtor(On.Terraria.GameContent.QuickStacking.MatchingItemTypeDestinationList.orig_ctor orig,
        Terraria.GameContent.QuickStacking.MatchingItemTypeDestinationList self)
    {
        orig(self);

        try
        {
            if (GrowToVanilla(self.firstEntryForType, VanillaCounts.Item, ModContent.NextType(ContentKind.Item), out var grown))
                self.firstEntryForType = grown;
        }
        catch (Exception e)
        {
            Safe.Report("MatchingItemTypeDestinationList.firstEntryForType", e);
        }
    }

    /// <summary>matchingItemTypeScratch 是同一结构的静态实例，安装时多半已存在。</summary>
    private static void GrowScratchDestinationList()
    {
        try
        {
            var scratch = Terraria.GameContent.QuickStacking.matchingItemTypeScratch;
            if (scratch is null) return;

            if (GrowToVanilla(scratch.firstEntryForType, VanillaCounts.Item, ModContent.NextType(ContentKind.Item), out var grown))
                scratch.firstEntryForType = grown;
        }
        catch (Exception e)
        {
            Safe.Report("QuickStacking.matchingItemTypeScratch", e);
        }
    }

    /// <summary>
    /// ItemFilters.MiscFallback._fitsFilterByItemType（Nullable&lt;bool&gt;[]）：
    /// 生成器给这张表的包装是引用数组形态，元素不能经包装读；扩容走数组对象本身，
    /// 再用同一包装类型写回属性。零填充 = "还不知道"。
    /// </summary>
    private static void MiscFallbackCtor(On.Terraria.GameContent.Creative.ItemFilters.MiscFallback.orig_ctor orig,
        Terraria.GameContent.Creative.ItemFilters.MiscFallback self,
        Il2CppSystem.Collections.Generic.List<Terraria.GameContent.Creative.IItemEntryFilter> otherFiltersToCheckAgainst)
    {
        orig(self, otherFiltersToCheckAgainst);

        try
        {
            var total = ModContent.NextType(ContentKind.Item);
            var table = self._fitsFilterByItemType;
            if (total <= VanillaCounts.Item || table is null || table.Length != VanillaCounts.Item) return;

            var grown = ArrayGrow.GrowZeroed(table.Pointer, total);
            if (grown != IntPtr.Zero)
                self._fitsFilterByItemType = new Il2CppReferenceArray<Il2CppSystem.Nullable<bool>>(grown);
        }
        catch (Exception e)
        {
            Safe.Report("ItemFilters.MiscFallback._fitsFilterByItemType", e);
        }
    }

    // ===================== getter 惰性表 =====================

    private static Il2CppReferenceArray<Terraria.GameContent.EmergencyStacking.Group> GroupLookup(
        On.Terraria.GameContent.EmergencyStacking.orig_get_GroupLookup orig)
    {
        var groups = orig();

        try
        {
            var total = ModContent.NextType(ContentKind.Item);
            if (groups is not null && groups.Length == VanillaCounts.Item && total > VanillaCounts.Item)
            {
                var grown = ArrayGrow.GrowZeroed(groups.Pointer, total);
                if (grown != IntPtr.Zero)
                {
                    var bigger = new Il2CppReferenceArray<Terraria.GameContent.EmergencyStacking.Group>(grown);
                    // 写回静态字段：getter 下次调用直接看到扩好的表。
                    Terraria.GameContent.EmergencyStacking._groupLookup = bigger;
                    return bigger;
                }
            }
        }
        catch (Exception e)
        {
            Safe.Report("EmergencyStacking._groupLookup", e);
        }

        return groups!;
    }

    // ===================== 公共件 =====================

    /// <summary>
    /// 值类型实例表扩容：仅当现长恰为原版数（游戏还没自己动过）且目标更长时扩，新尾部零填充。
    /// </summary>
    private static bool GrowToVanilla<T>(Il2CppStructArray<T>? table, int vanilla, int total, out Il2CppStructArray<T> grown)
        where T : unmanaged
    {
        grown = null!;
        if (table is null || table.Length != vanilla || total <= vanilla) return false;

        var pointer = ArrayGrow.GrowZeroed(table.Pointer, total);
        if (pointer == IntPtr.Zero) return false;

        grown = new Il2CppStructArray<T>(pointer);
        return true;
    }
}
