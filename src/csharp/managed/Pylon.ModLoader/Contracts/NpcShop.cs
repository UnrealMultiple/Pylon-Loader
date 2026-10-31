using Terraria;

namespace Pylon.ModLoader;

/// <summary>城镇 NPC 的商店：每店占一个 Main.shop 的空位（从 99 往下发）；Add 填商品、Register 占号，摆进游戏由商店管线做。</summary>
public class NpcShop
{
    /// <summary>一条商品（Add 造出来的）；condition 返回 false 就不摆。</summary>
    public sealed class ShopEntry
    {
        /// <summary>物品 ID。</summary>
        public int Type;

        /// <summary>显示条件（false = 不摆）；null = 没有。</summary>
        public Func<bool> Condition = null!;

        /// <summary>价格；null = 用游戏的。</summary>
        public int? Price;

        /// <summary>自定义货币 ID；null = 用金币。</summary>
        public int? Currency;
    }

    // #nextIndex 与 byKey 查重：Register 发号与重名抛错需要；完整注册表与 hook 安装归商店管线。
    private static int _nextIndex = 99;
    private static readonly HashSet<string> Keys = new();
    private static readonly Dictionary<string, NpcShop> ByKey = new();
    private static readonly Dictionary<int, bool> Built = new();

    /// <summary>新店：npcType 是开店的 NPC 类型，name 默认 'Shop'（聊天按钮返回它来开这家店）。</summary>
    public NpcShop(int npcType, string name = "Shop")
    {
        NpcType = npcType;
        Name = name;
        Entries = [];
        Index = -1;
    }

    /// <summary>开这家店的 NPC 类型。</summary>
    public int NpcType { get; }

    /// <summary>店名（OnChatButtonClicked 返回它来开这家店）。</summary>
    public string Name { get; }

    /// <summary>商品表（Add 填；管线按顺序摆进 Main.shop 的槽）。</summary>
    public List<ShopEntry> Entries { get; }

    /// <summary>Main.shop 的槽号；Register 从 99 往下发，没注册是 -1。</summary>
    public int Index { get; set; }

    /// <summary>true：游戏自己的店（管线预置的）；mod 的店是 false。</summary>
    public bool Vanilla { get; set; }

    /// <summary>加一条商品；condition 返回 false 就不摆，price/currency 覆盖价格与货币。</summary>
    public NpcShop Add(int type, Func<bool>? condition = null, int? price = null, int? currency = null)
    {
        Entries.Add(new ShopEntry { Type = type, Condition = condition!, Price = price, Currency = currency });
        return this;
    }

    /// <summary>占号（从 99 往下）并查重：同一 NPC+店名重复注册会抛；返回 this。</summary>
    public NpcShop Register()
    {
        var key = NpcType + "/" + Name;
        if (!Keys.Add(key)) throw new InvalidOperationException("NPCShop: " + key + " ja registrada");

        Index = _nextIndex--;
        ByKey[key] = this;
        return this;
    }

    /// <summary>直接打开这家店（等价于聊天里点了商店按钮）。</summary>
    public void Open()
    {
        Build();
        Main.instance.OpenShop(Index);
        GUIInstance.Active.GUIPageIcons.OpenUI(GUIPageIcons.Category.Inventory, GUIPageIcons.Category.Shop);
    }

    /// <summary>按 NPC 类型 + 店名找已注册的店；没有返回 null。</summary>
    internal static NpcShop Find(int npcType, string name) =>
        name != null && ByKey.TryGetValue(npcType + "/" + name, out var shop) ? shop : null!;

    /// <summary>把商品表装进 Main.shop 的槽（第一次 Open 时做）。</summary>
    private void Build()
    {
        if (Index < 0 || Built.ContainsKey(Index)) return;
        var storage = new InventoryStorage();
        storage.Resize(Math.Max(1, Entries.Count));
        storage.FillWithEmptyInstances();
        foreach (var entry in Entries)
        {
            if (entry.Condition != null && !entry.Condition()) continue;
            var item = new Item();
            item.SetDefaults(entry.Type, null);
            if (entry.Price.HasValue) item.shopCustomPrice = new Il2CppSystem.Nullable<int>(entry.Price.Value);
            if (entry.Currency.HasValue) item.shopSpecialCurrency = entry.Currency.Value;
            storage.AddItemToShop(item);
        }
        Main.instance.shop[Index] = storage;
        Built[Index] = true;
    }
}

// 未移植 get()/All 反查、Install()/InstallTravel() 装 hook、原版商店预置与 hook 分发、
// 注册表（#byKey/#byIndex/#modified）都归商店管线（同 ModType 的 getTypeByName）。
