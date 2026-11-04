namespace Pylon.ModLoader;

/// <summary>商店侧表：槽号（`Main.instance.shop` 的下标）↔ 店；槽号由 <see cref="NpcShop.Register"/> 发。</summary>
internal static class ShopTable
{
    private static readonly Dictionary<int, NpcShop> ByIndex = [];
    private static readonly Dictionary<int, List<NpcShop>> ByNpc = [];
    private static readonly List<NpcShop> Shops = [];

    internal static int Count => Shops.Count;

    internal static IReadOnlyList<NpcShop> All => Shops;

    internal static NpcShop? Of(int index) => ByIndex.TryGetValue(index, out var shop) ? shop : null;

    /// <summary>这家 NPC 有哪些店（聊天按钮将来要用）；没有就是空表。</summary>
    internal static IReadOnlyList<NpcShop> OfNpc(int npcType) =>
        ByNpc.TryGetValue(npcType, out var shops) ? shops : [];

    /// <summary>收表；幂等，每次 Mod 注册后跑一次。没注册（槽号 < 0）的店不收。</summary>
    internal static void Rebuild()
    {
        Shops.Clear();
        ByIndex.Clear();
        ByNpc.Clear();

        foreach (var shop in NpcShop.All)
        {
            if (shop.Index < 0) continue;
            if (ByIndex.ContainsKey(shop.Index)) continue;

            Shops.Add(shop);
            ByIndex[shop.Index] = shop;

            if (!ByNpc.TryGetValue(shop.NpcType, out var shops)) ByNpc[shop.NpcType] = shops = [];
            shops.Add(shop);
        }

        Shops.Sort((a, b) => a.Index.CompareTo(b.Index));
    }
}
