using Pylon.ModLoader.Textures;

namespace Pylon.ModLoader;

/// <summary>装备贴图表：按部位各一条槽位序列（槽位 = 该部位原版表长 + 序）。</summary>
internal static class EquipTable
{
    internal static readonly EquipType[] Parts =
    [
        EquipType.Head, EquipType.Body, EquipType.Legs,
        EquipType.HandOn, EquipType.HandOff, EquipType.Back, EquipType.Front,
        EquipType.Shoes, EquipType.Waist, EquipType.Wing, EquipType.Shield,
        EquipType.Neck, EquipType.Face, EquipType.Balloon, EquipType.Beard,
    ];

    private static readonly List<EquipTexture>[] ByPart = CreateBuckets();
    private static readonly HashSet<ModItem> Loaded = [];

    private static List<EquipTexture>[] CreateBuckets()
    {
        var buckets = new List<EquipTexture>[Parts.Length];
        for (var index = 0; index < buckets.Length; index++) buckets[index] = [];
        return buckets;
    }

    internal static int Count
    {
        get
        {
            var total = 0;
            for (var index = 0; index < ByPart.Length; index++) total += ByPart[index].Count;
            return total;
        }
    }

    internal static List<EquipTexture> Of(EquipType part) => ByPart[(int)part];

    internal static int Vanilla(EquipType part) => TypeTables.EquipVanilla((int)part, Table(part));

    internal static TypeTables.TableKind Kind(EquipType part) => part switch
    {
        EquipType.Head => TypeTables.TableKind.ArmorHead,
        EquipType.Body => TypeTables.TableKind.ArmorBody,
        EquipType.Legs => TypeTables.TableKind.ArmorLeg,
        EquipType.HandOn => TypeTables.TableKind.EquipHandOn,
        EquipType.HandOff => TypeTables.TableKind.EquipHandOff,
        EquipType.Back => TypeTables.TableKind.EquipBack,
        EquipType.Front => TypeTables.TableKind.EquipFront,
        EquipType.Shoes => TypeTables.TableKind.EquipShoes,
        EquipType.Waist => TypeTables.TableKind.EquipWaist,
        EquipType.Wing => TypeTables.TableKind.EquipWings,
        EquipType.Shield => TypeTables.TableKind.EquipShield,
        EquipType.Neck => TypeTables.TableKind.EquipNeck,
        EquipType.Face => TypeTables.TableKind.EquipFace,
        EquipType.Balloon => TypeTables.TableKind.EquipBalloon,
        _ => TypeTables.TableKind.EquipBeard,
    };

    internal static TextureTable Table(EquipType part) => part switch
    {
        EquipType.Head => TextureTable.ArmorHead,
        EquipType.Body => TextureTable.ArmorBody,
        EquipType.Legs => TextureTable.ArmorLeg,
        EquipType.HandOn => TextureTable.AccHandsOn,
        EquipType.HandOff => TextureTable.AccHandsOff,
        EquipType.Back => TextureTable.AccBack,
        EquipType.Front => TextureTable.AccFront,
        EquipType.Shoes => TextureTable.AccShoes,
        EquipType.Waist => TextureTable.AccWaist,
        EquipType.Wing => TextureTable.Wings,
        EquipType.Shield => TextureTable.AccShield,
        EquipType.Neck => TextureTable.AccNeck,
        EquipType.Face => TextureTable.AccFace,
        EquipType.Balloon => TextureTable.AccBalloon,
        _ => TextureTable.AccBeard,
    };

    /// <summary>这个部位还要写哪张平行表（复合层 / 手臂层）；没有就是空。</summary>
    internal static TextureTable[] Companions(EquipType part) => part switch
    {
        EquipType.Body => [TextureTable.ArmorBodyComposite, TextureTable.ArmorArm],
        EquipType.HandOn => [TextureTable.AccHandsOnComposite],
        EquipType.HandOff => [TextureTable.AccHandsOffComposite],
        _ => [],
    };

    internal static TypeTables.TableKind[] CompanionKinds(EquipType part) => part switch
    {
        EquipType.Body => [TypeTables.TableKind.ArmorBodyComposite, TypeTables.TableKind.ArmorArm],
        EquipType.HandOn => [TypeTables.TableKind.EquipHandOnComposite],
        EquipType.HandOff => [TypeTables.TableKind.EquipHandOffComposite],
        _ => [],
    };

    /// <summary>让每个物品的 <c>Load</c> 跑一次并收表；幂等（同一个物品只跑一次）。</summary>
    internal static void Collect()
    {
        for (var index = 0; index < ByPart.Length; index++) ByPart[index].Clear();

        IReadOnlyList<ModItem> items = ModContent.GetContent<ModItem>();
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];

            if (Loaded.Add(item))
            {
                try
                {
                    item.Load();
                }
                catch (Exception e)
                {
                    ContentErrors.Report($"[equip] {item.Name}.Load 异常：{Safe.Describe(e)}");
                }
            }

            var equips = item.EquipTextures;
            for (var slot = 0; slot < equips.Count; slot++)
            {
                var equip = equips[slot];

                if (equip.Type < (int)EquipType.Head || equip.Type > (int)EquipType.Beard)
                {
                    ContentErrors.Report($"[equip] {item.Name}.{equip.Name} 的部位 {equip.Type} 不存在（0..14）");
                    continue;
                }

                ByPart[equip.Type].Add(equip);
            }
        }
    }

    /// <summary>发号：槽位接在该部位原版表长之后。</summary>
    internal static void Assign()
    {
        for (var part = 0; part < Parts.Length; part++)
        {
            var list = ByPart[(int)Parts[part]];
            var vanilla = Vanilla(Parts[part]);

            for (var index = 0; index < list.Count; index++)
            {
                var equip = list[index];
                equip.Slot = vanilla + index;

                if (!BySlot.TryGetValue(Parts[part], out var map)) BySlot[Parts[part]] = map = [];
                map[equip.Slot] = equip;
            }
        }
    }

    private static readonly Dictionary<EquipType, Dictionary<int, EquipTexture>> BySlot = [];

    /// <summary>物品编号 → （部位, 槽位）；从各部位的槽位表反查得到。</summary>
    private static readonly Dictionary<int, List<(EquipType Part, int Slot)>> ByItem = [];

    /// <summary>这件物品带着哪些 mod 装备槽（没带就是 null）。</summary>
    internal static List<(EquipType Part, int Slot)>? SlotsOfItem(int itemType)
    {
        RebuildItemMap();
        return ByItem.TryGetValue(itemType, out var slots) ? slots : null;
    }

    /// <summary>按注册名查槽位（名字是 AddEquipTexture 传的那个）；没有就是 -1。</summary>
    internal static int SlotOf(EquipType part, string? name)
    {
        if (string.IsNullOrEmpty(name)) return -1;

        var list = Of(part);
        for (var index = 0; index < list.Count; index++)
        {
            if (string.Equals(list[index].Name, name, StringComparison.Ordinal)) return list[index].Slot;
        }

        return -1;
    }

    private static bool _itemMapBuilt;

    private static void RebuildItemMap()
    {
        if (_itemMapBuilt) return;
        _itemMapBuilt = true;

        for (var part = 0; part < Parts.Length; part++)
        {
            var type = Parts[part];
            var list = Of(type);

            for (var index = 0; index < list.Count; index++)
            {
                var equip = list[index];
                var owner = equip.Item;
                if (owner is null || owner.Type <= 0) continue;

                if (!ByItem.TryGetValue(owner.Type, out var slots))
                    ByItem[owner.Type] = slots = [];

                slots.Add((type, equip.Slot));
            }
        }
    }

    /// <summary>每个部位在 Item 上对应的槽位字段名（供物品声明时写入）。</summary>
    internal static readonly Dictionary<EquipType, string> ItemSlotField = new()
    {
        [EquipType.Head] = "headSlot",
        [EquipType.Body] = "bodySlot",
        [EquipType.Legs] = "legSlot",
        [EquipType.HandOn] = "handOnSlot",
        [EquipType.HandOff] = "handOffSlot",
        [EquipType.Back] = "backSlot",
        [EquipType.Front] = "frontSlot",
        [EquipType.Shoes] = "shoeSlot",
        [EquipType.Waist] = "waistSlot",
        [EquipType.Wing] = "wingSlot",
        [EquipType.Shield] = "shieldSlot",
        [EquipType.Neck] = "neckSlot",
        [EquipType.Face] = "faceSlot",
        [EquipType.Balloon] = "balloonSlot",
        [EquipType.Beard] = "beardSlot",
    };

    internal static EquipTexture? OfSlot(EquipType part, int slot)
    {
        if (slot < 0) return null;
        return BySlot.TryGetValue(part, out var map) && map.TryGetValue(slot, out var equip) ? equip : null;
    }
}
