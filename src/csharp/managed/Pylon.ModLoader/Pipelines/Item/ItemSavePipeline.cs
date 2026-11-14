using System;
using System.Collections.Generic;
using System.IO;
using Terraria;

namespace Pylon.ModLoader;

/// <summary>
/// mod 物品持久化：原版 .plr 只认原版类型号，mod 物品（含"？"占位）读档即丢。
/// 侧车 <c>&lt;玩家文件&gt;.pl.json</c> 的 "items" 段（与发型管线共文件，各写各的键）：
/// 行 = 容器 + 槽位 + 数量 + 前缀（mod 前缀按名存、原版前缀存号 pv）+ 收藏 + FullKey + 作者数据；
/// 原版物品被上了 mod 前缀的，单独存行（c=真实容器、t=原版类型号；.plr 里前缀字节装不下 mod 前缀号）。
/// 读档：键 → 注册表查真类型，查不到走？池占位；槽上已是同物品 / 占位 / 过期 mod 号就原位恢复，
/// 否则找背包空格；mod 缺席的行进等待名单，下次保存原样回写。
/// 前缀的 mod 缺席时键进 pendingPrefix 记账（按容器#槽+类型），下次保存回填进行里继续等。
/// </summary>
internal sealed class ItemSavePipeline
{
    public static ItemSavePipeline Instance { get; } = new();

    private const string SaveSection = "items";

    /// <summary>存档期占位物品的原始行（含前缀 / 作者数据），键 = "容器#槽"。</summary>
    private readonly Dictionary<string, TagCompound> _pending = new(StringComparer.Ordinal);

    /// <summary>mod 物品的前缀 mod 缺席时暂存的前缀键，键 = "容器#槽"，值 = (前缀 FullKey, 物品类型)。</summary>
    private readonly Dictionary<string, (string Key, int Type)> _pendingPrefix = new(StringComparer.Ordinal);

    private readonly List<TagCompound>?[] _kept = new List<TagCompound>[2]; // 0 = 物品行，1 = 前缀行

    private ItemSavePipeline() { }

    public void Install()
    {
        On.Terraria.Player.InternalSavePlayerFile += SavePlayerFile;
        On.Terraria.Player.LoadPlayer += LoadPlayerFile;
    }

    // ===================== 保存 =====================

    private void SavePlayerFile(On.Terraria.Player.orig_InternalSavePlayerFile orig, Terraria.IO.PlayerFileData playerFile)
    {
        orig(playerFile);

        try
        {
            if (playerFile is null || playerFile.IsCloudSave || playerFile.Player is not { } player) return;
            if (DataFile(playerFile) is not { } file) return;

            var rows = new List<object?>();
            foreach (var (name, container) in ContainersOf(player))
            {
                for (var slot = 0; slot < container.Length; slot++)
                {
                    var row = RowOf(name, slot, container[slot]);
                    if (row is not null) rows.Add(row);
                }
            }

            // 等待行回写：前缀行要求槽上还是同类且前缀仍为 0，否则作废。
            rows.AddRange(CarryKept(player));

            var root = TagJson.Read(file);
            if (rows.Count > 0) root.Set(SaveSection, rows);
            else root.Remove(SaveSection);

            if (root.Count > 0) TagJson.Write(file, root);
            else File.Delete(file);
        }
        catch (Exception e)
        {
            ContentErrors.Report("[item] 存档失败：" + Safe.Describe(e));
        }
    }

    /// <summary>一个槽位一条行；空槽返回 null。</summary>
    private TagCompound? RowOf(string container, int slot, Item? item)
    {
        if (item is null || item.stack <= 0 || item.type <= 0) return null;

        var type = item.type;
        if (ModContent.IsCustom(ContentKind.Item, type))
        {
            _pending.Remove($"{container}#{slot}");
            return ModRow(container, slot, item);
        }

        // ？占位：优先原样带回顾存期间的前缀与作者数据。
        var key = UnloadedItems.KeyFor(type);
        if (key is not null)
        {
            if (_pending.TryGetValue($"{container}#{slot}", out var saved) && saved.GetString("k") == key) return saved;
            return new TagCompound { ["c"] = container, ["s"] = slot, ["n"] = item.stack, ["k"] = key };
        }

        // 原版物品只在前缀有主（mod 前缀）时才需要补一行：.plr 装不下 mod 前缀号。
        if (PrefixTable.Of(item.prefix) is not { } prefix) return null;
        return new TagCompound
        {
            ["c"] = container,
            ["s"] = slot,
            ["t"] = type,
            ["p"] = prefix.FullKey,
        };
    }

    private TagCompound ModRow(string container, int slot, Item item)
    {
        var row = new TagCompound
        {
            ["c"] = container,
            ["s"] = slot,
            ["n"] = item.stack,
            ["k"] = KeyOf(item.type),
        };

        var slotKey = $"{container}#{slot}";
        if (PrefixTable.Of(item.prefix) is { } prefix)
        {
            row["p"] = prefix.FullKey;
            _pendingPrefix.Remove(slotKey);
        }
        else if (item.prefix > 0)
        {
            // 原版前缀号稳定，存号；SetDefaults 会抹掉前缀，读档时按号重上。
            row["pv"] = item.prefix;
            _pendingPrefix.Remove(slotKey);
        }
        else if (_pendingPrefix.TryGetValue(slotKey, out var pending) && pending.Type == item.type)
        {
            // 前缀的 mod 还没回来：把暂存的键写进行里继续等。
            row["p"] = pending.Key;
        }

        if (item.favorited) row["f"] = true;

        var mod = ItemPipeline.Instance.InstanceOf(item);
        if (mod is not null && mod.Plan.Has(HookId.SaveData))
        {
            var data = new TagCompound();
            try
            {
                mod.SaveData(item, data);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[item] {mod.Name}.SaveData 异常：{Safe.Describe(e)}");
            }

            if (data.Count > 0) row["d"] = data;
        }

        return row;
    }

    /// <summary>mod 缺席期间留在内存的行随保存回写；前缀行验证槽位仍然匹配。
    /// 行常驻（不清空，同会话第二次保存不能把已落盘的行抹掉），只在读档时重置。</summary>
    private List<TagCompound> CarryKept(Player player)
    {
        var result = new List<TagCompound>();

        var rows = _kept[0];
        if (rows is not null) result.AddRange(rows);

        var prefixes = _kept[1];
        if (prefixes is not null)
        {
            foreach (var row in prefixes)
            {
                var container = row.GetString("c");
                var slot = row.GetInt("s");
                if (ContainerOf(player, container) is not { } items || slot < 0 || slot >= items.Length) continue;

                var item = items[slot];
                var type = row.GetInt("t");
                if (item is { stack: > 0 } && item.type == type && item.prefix == 0) result.Add(row);
            }
        }

        return result;
    }

    // ===================== 读取 =====================

    private Terraria.IO.PlayerFileData LoadPlayerFile(On.Terraria.Player.orig_LoadPlayer orig,
                                                      string playerPath, bool cloudSave)
    {
        var data = orig(playerPath, cloudSave);

        try
        {
            // 每个存档的键集独立；重置要在恢复行之前。
            UnloadedItems.ResetSlots();
            _pending.Clear();
            _pendingPrefix.Clear();
            _kept[0] = null;
            _kept[1] = null;

            if (data is null || data.IsCloudSave || data.Player is not { } player) return data!;
            if (DataFile(data) is not { } file || !File.Exists(file)) return data!;

            var rows = TagJson.Read(file).GetList(SaveSection);
            foreach (var entry in rows)
            {
                if (entry is not TagCompound row) continue;

                try
                {
                    if (row.GetInt("t") > 0) ApplyVanillaPrefix(player, row);
                    else Restore(player, row);
                }
                catch (Exception e)
                {
                    ContentErrors.Report("[item] 恢复行失败：" + Safe.Describe(e));
                }
            }
        }
        catch (Exception e)
        {
            ContentErrors.Report("[item] 读档失败：" + Safe.Describe(e));
        }

        return data!;
    }

    /// <summary>原版物品的 mod 前缀：槽上还是同类且前缀为 0 才补，否则留到下次保存。</summary>
    private void ApplyVanillaPrefix(Player player, TagCompound row)
    {
        var container = row.GetString("c");
        var type = row.GetInt("t");
        var slot = row.GetInt("s");

        if (ContainerOf(player, container) is not { } items || slot < 0 || slot >= items.Length ||
            items[slot] is not { stack: > 0 } item || item.type != type || item.prefix != 0 ||
            PrefixByKey(row.GetString("p")) is not { } prefix)
        {
            Keep(row, index: 1);
            return;
        }

        item.Prefix(prefix.Type);
    }

    /// <summary>mod 行恢复：槽位选择 → SetDefaults → 数量 / 前缀 / 收藏 / 作者数据。</summary>
    private void Restore(Player player, TagCompound row)
    {
        var container = row.GetString("c");
        var slot = row.GetInt("s");
        var key = row.GetString("k");

        var type = TypeForKey(key);
        if (type <= 0)
        {
            Keep(row, index: 0);
            return;
        }

        var items = ContainerOf(player, container);
        if (items is null || slot < 0 || slot >= items.Length)
        {
            Keep(row, index: 0);
            return;
        }

        // 实际落位可能与原行不同（原槽被占挪进背包），占位期记账要跟着落位走。
        var placedContainer = container;
        var placedSlot = slot;

        // 原位恢复：空槽、同类型、"？"占位、过期 mod 号（注册表重建后号变了）。
        var current = items[slot];
        var replace = current is null || current.stack <= 0 || current.type <= 0 ||
                      current.type == type ||
                      UnloadedItems.IsUnloadedType(current.type) ||
                      (current.type >= VanillaCounts.Item && !ModContent.IsCustom(ContentKind.Item, current.type));

        if (!replace)
        {
            // 主背包（0..49）找空格，不动钱币 / 弹药 / 光标。
            for (var i = 0; i < 50; i++)
            {
                if (player.inventory is not { } inventory || i >= inventory.Length) break;
                if (inventory[i] is { stack: > 0, type: > 0 }) continue;

                items = inventory;
                placedContainer = "inventory";
                placedSlot = i;
                replace = true;
                break;
            }
        }

        if (!replace || !Place(items[placedSlot], type, row, $"{placedContainer}#{placedSlot}"))
        {
            Keep(row, index: 0);
            return;
        }

        if (UnloadedItems.IsUnloadedType(items[placedSlot].type))
        {
            // mod 还没回来：按实际落位记住原始行，下次保存原样带走（前缀与作者数据都在里面）。
            _pending[$"{placedContainer}#{placedSlot}"] = row;
        }
    }

    private bool Place(Item? item, int type, TagCompound row, string slotKey)
    {
        if (item is null) return false;

        item.SetDefaults(type, null);
        if (item.type != type) return false;   // 落回原版分支被归零 = 类型不可用

        item.stack = Math.Max(1, row.GetInt("n"));

        var prefixKey = row.GetString("p");
        if (PrefixByKey(prefixKey) is { } prefix)
        {
            item.Prefix(prefix.Type);
        }
        else if (row.GetInt("pv") > 0)
        {
            item.Prefix(row.GetInt("pv"));
        }
        else if (!string.IsNullOrEmpty(prefixKey))
        {
            // 前缀的 mod 缺席：键暂存（按落位记账），下次保存回填进行里继续等。
            _pendingPrefix[slotKey] = (prefixKey, type);
        }

        if (row.GetBool("f")) item.favorited = true;

        if (row.Get("d") is TagCompound data &&
            ItemPipeline.Instance.InstanceOf(item) is { } mod &&
            mod.Plan.Has(HookId.LoadData))
        {
            try
            {
                mod.LoadData(item, data);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[item] {mod.Name}.LoadData 异常：{Safe.Describe(e)}");
            }
        }

        return true;
    }

    // ===================== 公共件 =====================

    private void Keep(TagCompound row, int index)
    {
        _kept[index] ??= [];
        _kept[index]!.Add(row);
    }

    /// <summary>键 → 类型：先查注册表（mod 在），查不到给？池占位号（mod 不在），再不行 -1。</summary>
    private static int TypeForKey(string key) =>
        ModContent.TryFind<ModItem>(key, out var item) ? item.Type : UnloadedItems.TypeFor(key);

    private static string KeyOf(int type) =>
        ModContent.Find(ContentKind.Item, type) is { } content ? content.FullKey : "";

    private static ModPrefix? PrefixByKey(string key)
    {
        foreach (var prefix in ModContent.GetContent<ModPrefix>())
        {
            if (prefix.FullKey == key) return prefix;
        }

        return null;
    }

    /// <summary>容器名 → 数组。bank 系列是 InventoryStorage.item；loadout 按展开名寻址。</summary>
    private static Item[]? ContainerOf(Player player, string name)
    {
        if (player.Loadouts is { } loadouts)
        {
            for (var i = 0; i < loadouts.Length; i++)
            {
                var loadout = loadouts[i];
                if (loadout is null) continue;
                if (name == $"loadout{i}.armor") return loadout.Armor;
                if (name == $"loadout{i}.dye") return loadout.Dye;
            }
        }

        return name switch
        {
            "inventory" => player.inventory,
            "armor" => player.armor,
            "dye" => player.dye,
            "misc" => player.miscEquips,
            "miscdye" => player.miscDyes,
            "bank" => player.bank?.item,
            "bank2" => player.bank2?.item,
            "bank3" => player.bank3?.item,
            "bank4" => player.bank4?.item,
            _ => null,
        };
    }

    /// <summary>存档期容器清单：背包 / 装备 / 染料 / 杂项与染料 / 四个仓库 / 每套装扮槽。</summary>
    private static IEnumerable<(string Name, Item[] Items)> ContainersOf(Player player)
    {
        if (player.inventory is { } inventory) yield return ("inventory", inventory);
        if (player.armor is { } armor) yield return ("armor", armor);
        if (player.dye is { } dye) yield return ("dye", dye);
        if (player.miscEquips is { } misc) yield return ("misc", misc);
        if (player.miscDyes is { } miscDyes) yield return ("miscdye", miscDyes);
        if (player.bank?.item is { } bank) yield return ("bank", bank);
        if (player.bank2?.item is { } bank2) yield return ("bank2", bank2);
        if (player.bank3?.item is { } bank3) yield return ("bank3", bank3);
        if (player.bank4?.item is { } bank4) yield return ("bank4", bank4);

        if (player.Loadouts is { } loadouts)
        {
            for (var i = 0; i < loadouts.Length; i++)
            {
                if (loadouts[i]?.Armor is { } loadoutArmor) yield return ($"loadout{i}.armor", loadoutArmor);
                if (loadouts[i]?.Dye is { } loadoutDye) yield return ($"loadout{i}.dye", loadoutDye);
            }
        }
    }

    private static string? DataFile(Terraria.IO.PlayerFileData data)
    {
        if (data.IsCloudSave) return null;
        var path = data.Path;
        return string.IsNullOrEmpty(path) ? null : path + ".pl.json";
    }
}
