using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Pylon.ModLoader.Tables;
using Terraria;

namespace Pylon.ModLoader;

/// <summary>
/// 物品排序白名单：原版 SetupWhiteLists 只给原版类型建层，mod 物品不在任何白名单里，
/// 背包"整理"时会被整层跳过（白名单按层过滤可排序格，不在名单 = 物品消失）。
/// 这里复刻原版分类核：mod 样例数组 + 剩余下标表逐层喂给原版 SortingMethod，
/// 每层挑走的下标换算回类型写进白名单，最后剩不下的由 Trash 层兜底吃掉。
/// </summary>
internal static class ItemSortingHooks
{
    public static void Install() => On.Terraria.UI.ItemSorting.SetupWhiteLists += SetupWhiteLists;

    /// <summary>InstallPlan 在物品样例之后也催一次：游戏首跑可能晚于内容安装。</summary>
    public static void Classify() => Classify(LoaderHost.Current.Log);

    private static void SetupWhiteLists(On.Terraria.UI.ItemSorting.orig_SetupWhiteLists orig)
    {
        orig();
        Classify(LoaderHost.Current.Log);
    }

    private static Dictionary<string, Terraria.UI.ItemSorting.ItemSortingLayer>? _layersByName;
    private static bool _layersTried;

    private static unsafe Dictionary<string, Terraria.UI.ItemSorting.ItemSortingLayer>? LayersByName()
    {
        if (_layersTried) return _layersByName;
        _layersTried = true;
        try
        {
            var klass = Il2CppClassPointerStore<Terraria.UI.ItemSorting.ItemSortingLayers>.NativeClassPtr;
            if (klass == IntPtr.Zero) return null;
            try { IL2CPP.il2cpp_runtime_class_init(klass); } catch { }

            var byName = new Dictionary<string, Terraria.UI.ItemSorting.ItemSortingLayer>();
            var iterator = IntPtr.Zero;
            while (true)
            {
                var field = IL2CPP.il2cpp_class_get_fields(klass, ref iterator);
                if (field == IntPtr.Zero) break;
                if (((uint)IL2CPP.il2cpp_field_get_flags(field) & 0x10) == 0) continue; // 只要静态
                var value = IntPtr.Zero;
                IL2CPP.il2cpp_field_static_get_value(field, &value);
                if (value == IntPtr.Zero) continue;
                var layer = new Terraria.UI.ItemSorting.ItemSortingLayer(value);
                if (string.IsNullOrEmpty(layer.Name)) continue;
                byName[layer.Name] = layer;
            }
            _layersByName = byName;
        }
        catch
        {
            _layersByName = null;
        }
        return _layersByName;
    }

    private static void Classify(IModLog log)
    {
        try
        {
            var from = VanillaCounts.Item;
            var to = ModContent.NextType(ContentKind.Item);
            if (to <= from) return;

            var whiteLists = Terraria.UI.ItemSorting._layerWhiteLists;
            if (whiteLists is null || whiteLists.Count == 0) return;

            var byName = LayersByName();
            if (byName is null || byName.Count == 0)
            {
                log.Warn("[item] 排序白名单：ItemSortingLayers 静态层对象解析失败，mod 类型不进层");
                return;
            }

            // 样例：每类一个 Item，SetDefaults 走 mod 分支拿真实属性供原版分层逻辑判断。
            var count = to - from;
            var samples = new Il2CppReferenceArray<Item>(count);
            for (var i = 0; i < count; i++)
            {
                var sample = new Item();
                sample.SetDefaults(from + i, null);
                samples[i] = sample;
            }

            // 剩余下标表：每层从里面挑走自己的，最后一层把剩余全部挑走。
            var indices = new Il2CppSystem.Collections.Generic.List<int>();
            for (var i = 0; i < count; i++) indices.Add(i);

            // 层序号表扩到 mod 类型（0 = 还没有层）。
            var layerOf = Terraria.UI.ItemSorting._layerIndexForItemType;
            if (layerOf is not null && layerOf.Length < to)
            {
                var grown = ArrayGrow.GrowZeroed(layerOf.Pointer, to);
                if (grown != IntPtr.Zero)
                    Terraria.UI.ItemSorting._layerIndexForItemType = new Il2CppStructArray<int>(grown);
            }
            layerOf = Terraria.UI.ItemSorting._layerIndexForItemType;

            // 期间换成空字典：SortingMethod 内部读 _layerWhiteLists 时拿到的状态
            // 与原版 SetupWhiteLists 的初始状态一致（先空后填），跑完换回。
            var saved = Terraria.UI.ItemSorting._layerWhiteLists;
            Terraria.UI.ItemSorting._layerWhiteLists =
                new Il2CppSystem.Collections.Generic.Dictionary<string, Il2CppSystem.Collections.Generic.List<int>>();

            var placed = 0;
            var layerNo = 0;
            try
            {
                var keys = new Il2CppStringArray(whiteLists.Count);
                whiteLists.Keys.CopyTo(keys, 0);
                for (var l = 0; l < keys.Length; l++)
                {
                    var name = keys[l];
                    if (name is null) continue;
                    if (!byName.TryGetValue(name, out var layer) || layer is null) continue;
                    if (!whiteLists.TryGetValue(name, out var whitelist) || whitelist is null) continue;

                    var picked = layer.SortingMethod.Invoke(layer, samples, indices);
                    if (picked is null) continue;

                    for (var k = 0; k < picked.Count; k++)
                    {
                        var type = from + picked[k];
                        whitelist.Add(type);
                        if (layerOf is not null && (uint)type < (uint)layerOf.Length) layerOf[type] = layerNo;
                        placed++;
                    }
                    layerNo++;
                }
            }
            finally
            {
                Terraria.UI.ItemSorting._layerWhiteLists = saved;
            }

            if (indices.Count > 0)
                log.Warn($"[item] 排序白名单：{indices.Count}/{count} 个类型没进任何层（整理背包不会动它们）");
            else if (placed > 0)
                log.Info($"[item] 排序白名单：{placed} 个 mod 类型已分层");
        }
        catch (Exception e)
        {
            Safe.Report("ItemSorting 分类", e);
        }
    }
}
