using Pylon.ModLoader.Contracts;
using Pylon.ModLoader.Pipeline;
using Pylon.ModLoader.Pipelines;
using Terraria;
using Terraria.ID;

namespace Pylon.ModLoader.Content;
/// <summary>商店注册表：店名 ↔ 店、货架槽号 ↔ 店（99 往下发）；原版商店在这里预置占号，开店走 OpenShop。</summary>
public static class NpcShopDatabase
{
    private static readonly Dictionary<string, AbstractNpcShop> Shops = new(StringComparer.Ordinal);

    private static readonly Dictionary<int, List<NpcShop>> ByNpc = [];

    private static int _nextIndex = 99;

    internal static readonly Dictionary<int, NpcShop> ByIndex = [];

    public static IEnumerable<AbstractNpcShop> AllShops => Shops.Values;

    public static readonly ISet<string> NoPylons = new HashSet<string>
    {
        GetShopName(NPCID.TravellingMerchant),
        GetShopName(NPCID.SkeletonMerchant),
        GetShopName(NPCID.DD2Bartender),
        GetShopName(NPCID.SantaClaus),
    };

    public static string GetShopName(int npcType, string shopName = "Shop") =>
        $"{(ModContent.Find(ContentKind.Npc, npcType) is ModNpc own ? own.FullKey : "Terraria/" + NPCID.Search.GetName(npcType))}/{shopName}";

    public static AbstractNpcShop GetShop(string fullName)
    {
        Initialize();
        return Shops[fullName];
    }

    public static bool TryGetNpcShop(string fullName, out NpcShop shop)
    {
        Initialize();
        shop = Shops.TryGetValue(fullName, out var found) ? found as NpcShop ?? null! : null!;
        return shop is not null;
    }

    public static void AddShop(AbstractNpcShop shop)
    {
        ArgumentNullException.ThrowIfNull(shop);
        if (Shops.ContainsKey(shop.FullName)) throw new ArgumentException($"Shop '{shop.FullName}' is already registered");
        if (shop is not NpcShop ordinary) throw new NotSupportedException("Mobile shop registration requires NpcShop");
        if (_nextIndex <= 25) throw new InvalidOperationException("Mobile NPC shop slots are exhausted");

        ordinary.Index = _nextIndex--;
        Shops.Add(shop.FullName, shop);
        ByIndex.Add(ordinary.Index, ordinary);

        if (!ByNpc.TryGetValue(shop.NpcType, out var list)) ByNpc[shop.NpcType] = list = [];
        list.Add(ordinary);

        if (_initialized && !ordinary.Vanilla)
        {
            ShopPipeline.ModifyShop(ordinary);
            ShopPipeline.AddShopEntries(ordinary);
            ordinary.Modified = true;
            ordinary.FinishSetup();
        }
    }

    private static bool _initialized;

    internal static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        int[] types = [17, 19, 20, 38, 54, 107, 108, 124, 142, 160, 178, 207, 208, 209, 227, 228, 229, 353, 368, 453, 550, 588, 633, 663, 227];
        for (var i = 0; i < types.Length; i++)
        {
            var shop = new NpcShop(types[i], i == 24 ? "Decor" : "Shop") { Index = i + 1, Vanilla = true };
            Shops.Add(shop.FullName, shop);
            ByIndex.Add(shop.Index, shop);

            if (!ByNpc.TryGetValue(shop.NpcType, out var list)) ByNpc[shop.NpcType] = list = [];
            list.Add(shop);
        }

        foreach (var shop in ByIndex.Values)
        {
            if (shop.Vanilla) continue;

            ShopPipeline.ModifyShop(shop);
            ShopPipeline.AddShopEntries(shop);
            shop.Modified = true;
            shop.FinishSetup();
        }

        LoaderHost.Current.Log.Info($"[shop] 安装：{Shops.Count} 家店（原版预置 + mod 注册）");
    }

    internal static void EnsureInitialized() => Initialize();

    public static void OpenShop(string fullName)
    {
        if (!TryGetNpcShop(fullName, out var shop) || shop.Index < 0)
            throw new ArgumentException($"Shop '{fullName}' is not registered", nameof(fullName));

        Main.instance.OpenShop(shop.Index);
        if (GUIInstance.Active is not null)
            GUIInstance.Active.GUIPageIcons.OpenUI((GUIPageIcons.Category)2, (GUIPageIcons.Category)4);
    }

    internal static NpcShop? Find(int npcType, string name) =>
        ByNpc.TryGetValue(npcType, out var shops)
            ? shops.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal))
            : null;
}
