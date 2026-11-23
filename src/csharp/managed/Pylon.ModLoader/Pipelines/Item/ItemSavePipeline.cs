using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Pylon.ModLoader.SaveData;
using Terraria;

namespace Pylon.ModLoader.Pipelines;
/// <summary>mod 物品持久化：.plr 只认原版类型号，mod 物品与 mod 前缀走侧车 &lt;玩家文件&gt;.pyon 的 items 段按容器+槽位存取，读档时恢复或进？池占位。</summary>
internal sealed class ItemSavePipeline
{
    public static ItemSavePipeline Instance { get; } = new();

    private const string SaveSection = "items";

    private readonly Dictionary<string, TagCompound> _pending = new(StringComparer.Ordinal);

    private readonly Dictionary<string, (string Key, int Type)> _pendingPrefix = new(StringComparer.Ordinal);

    private readonly List<TagCompound>?[] _kept = new List<TagCompound>[2];

    private ItemSavePipeline() { }

    public void Install()
    {
        On.Terraria.Player.InternalSavePlayerFile += SavePlayerFile;
        On.Terraria.Player.LoadPlayer += LoadPlayerFile;
    }

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

            rows.AddRange(CarryKept(player));

            var root = PlayerSidecar.Read(file);
            if (rows.Count > 0) root.Set(SaveSection, rows);
            else root.Remove(SaveSection);

            if (root.Count > 0) PlayerSidecar.Write(file, root);
            else File.Delete(file);
        }
        catch (Exception e)
        {
            ContentErrors.Line("[item] 存档失败：" + Safe.Dump(e));
        }
    }

    private TagCompound? RowOf(string container, int slot, Item? item)
    {
        if (item is null || item.stack <= 0 || item.type <= 0) return null;

        var type = item.type;
        if (ModContent.IsCustom(ContentKind.Item, type))
        {
            _pending.Remove($"{container}#{slot}");
            return ModRow(container, slot, item);
        }

        var key = UnloadedItems.KeyFor(type);
        if (key is not null)
        {
            if (_pending.TryGetValue($"{container}#{slot}", out var saved) && saved.GetString("k") == key) return saved;
            return new TagCompound { ["c"] = container, ["s"] = slot, ["n"] = item.stack, ["k"] = key };
        }

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
            row["pv"] = item.prefix;
            _pendingPrefix.Remove(slotKey);
        }
        else if (_pendingPrefix.TryGetValue(slotKey, out var pending) && pending.Type == item.type)
        {
            row["p"] = pending.Key;
        }

        if (item.favorited) row["f"] = true;

        var mod = ItemPipeline.Instance.InstanceOf(item);
        if (mod is not null && mod.Plan.Has(HookId.SaveData))
        {
            var data = new TagCompound();
            try
            {
                mod.SaveData(data);
            }
            catch (Exception e)
            {
                ContentErrors.Line($"[item] {mod.Name}.SaveData 异常：{Safe.Dump(e)}");
            }

            if (data.Count > 0) row["d"] = data;
        }

        return row;
    }

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

    private Terraria.IO.PlayerFileData LoadPlayerFile(On.Terraria.Player.orig_LoadPlayer orig,
                                                      string playerPath, bool cloudSave)
    {
        var data = orig(playerPath, cloudSave);

        try
        {
            UnloadedItems.ResetSlots();
            _pending.Clear();
            _pendingPrefix.Clear();
            _kept[0] = null;
            _kept[1] = null;

            if (data is null || data.IsCloudSave || data.Player is not { } player) return data!;
            if (DataFile(data) is not { } file || !File.Exists(file)) return data!;

            var rows = PlayerSidecar.Read(file).GetList<TagCompound>(SaveSection);
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
                    ContentErrors.Line("[item] 恢复行失败：" + Safe.Dump(e));
                }
            }
        }
        catch (Exception e)
        {
            ContentErrors.Line("[item] 读档失败：" + Safe.Dump(e));
        }

        return data!;
    }

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

        var placedContainer = container;
        var placedSlot = slot;

        var current = items[slot];
        var replace = current is null || current.stack <= 0 || current.type <= 0 ||
                      current.type == type ||
                      UnloadedItems.IsUnloadedType(current.type) ||
                      (current.type >= VanillaCounts.Item && !ModContent.IsCustom(ContentKind.Item, current.type));

        if (!replace)
        {
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
            _pending[$"{placedContainer}#{placedSlot}"] = row;
        }
    }

    private bool Place(Item? item, int type, TagCompound row, string slotKey)
    {
        if (item is null) return false;

        item.SetDefaults(type, null);
        if (item.type != type) return false;

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
            _pendingPrefix[slotKey] = (prefixKey, type);
        }

        if (row.GetBool("f")) item.favorited = true;

        if (row.Get("d") is TagCompound data &&
            ItemPipeline.Instance.InstanceOf(item) is { } mod &&
            mod.Plan.Has(HookId.LoadData))
        {
            try
            {
                mod.LoadData(data);
            }
            catch (Exception e)
            {
                ContentErrors.Line($"[item] {mod.Name}.LoadData 异常：{Safe.Dump(e)}");
            }
        }

        return true;
    }

    private void Keep(TagCompound row, int index)
    {
        _kept[index] ??= [];
        _kept[index]!.Add(row);
    }

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
        return string.IsNullOrEmpty(path) ? null : path + ".pl";
    }
}

