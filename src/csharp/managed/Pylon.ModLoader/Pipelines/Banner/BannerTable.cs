using Terraria;
using Terraria.GameContent;
using Terraria.ID;

namespace Pylon.ModLoader;

/// <summary>
/// 旗帜侧表：三张映射 + 击杀侧表。
/// mod 旗子的编号 = 代表 NPC 的类型号（都大于原版 NPC 数），所以游戏那三张原版长度的平行表
/// 永远不会被 mod 编号访问到——本表替它们回答 mod 编号的查询。
/// </summary>
internal static class BannerTable
{
    /// <summary>原版 NPC 数：mod 旗子编号的下界。</summary>
    private const int Base = VanillaCounts.Npc;

    private static readonly Dictionary<int, int> BannerOfNpc = [];
    private static readonly Dictionary<int, int> ItemOfBanner = [];
    private static readonly Dictionary<int, int> BannerOfItem = [];
    private static readonly Dictionary<int, string> KeyOfBanner = [];

    private static int[] _kills = [];
    private static bool[] _fresh = [];
    private static int[] _claimable = [];

    /// <summary>已登记的旗子数量。</summary>
    internal static int Count => ItemOfBanner.Count;

    /// <summary>这个编号是不是 mod 旗子（本表有登记）。</summary>
    internal static bool IsMod(int banner) => ItemOfBanner.ContainsKey(banner);

    internal static int BannerOfNpcType(int npcType) => BannerOfNpc.TryGetValue(npcType, out var banner) ? banner : 0;

    internal static int ItemForBanner(int banner) => ItemOfBanner.TryGetValue(banner, out var item) ? item : 0;

    internal static int BannerForItem(int item) => BannerOfItem.TryGetValue(item, out var banner) ? banner : 0;

    /// <summary>所有登记的旗子号（点亮 buff 时遍历用）。</summary>
    internal static IEnumerable<int> Banners => ItemOfBanner.Keys;

    internal static int Kills(int banner) => Slot(banner) is var index && index >= 0 ? _kills[index] : 0;

    /// <summary>这面旗子每多少杀给一面可领取（读 <c>ItemID.Sets.KillsToBanner</c>，默认 50）。</summary>
    private const int DefaultClaimStep = 50;

    internal static int ThresholdOf(int banner)
    {
        var item = ItemForBanner(banner);
        var table = ItemID.Sets.KillsToBanner;
        if (item > 0 && table is not null && item < table.Length && table[item] > 0) return table[item];
        return DefaultClaimStep;
    }

    internal static void AddKill(int banner, int amount = 1)
    {
        var index = Slot(banner);
        if (index < 0) return;

        var step = ThresholdOf(banner);
        var before = _kills[index] / step;
        _kills[index] += amount;

        // 跨过阈值的整数倍就给一面可领取，和原版"每 KillsToBanner 杀一面"的节奏对齐。
        var after = _kills[index] / step;
        if (after <= before) return;

        _claimable[index] += after - before;
        _fresh[index] = true;
        BannerSystem.AnyNewClaimableBanners = true;
    }

    /// <summary>兼容旧调用：默认阈值常量。</summary>
    internal const int ClaimStep = DefaultClaimStep;

    /// <summary>这面 mod 旗子还能领几面。</summary>
    internal static int Claimable(int banner) => Slot(banner) is var index && index >= 0 ? _claimable[index] : 0;

    /// <summary>领走 amount 面；返回真领到了几面（不超过现有的）。</summary>
    internal static int Claim(int banner, int amount)
    {
        var index = Slot(banner);
        if (index < 0 || amount <= 0) return 0;

        var granted = Math.Min(amount, _claimable[index]);
        _claimable[index] -= granted;
        return granted;
    }

    /// <summary>最大登记旗子号；没登记任何旗子时返回 0。</summary>
    internal static int MaxBanner
    {
        get
        {
            var max = 0;
            foreach (var (banner, _) in ItemOfBanner)
            {
                if (banner > max) max = banner;
            }

            return max;
        }
    }

    internal static bool Fresh(int banner) => Slot(banner) is var index && index >= 0 && _fresh[index];

    internal static void SetFresh(int banner, bool value)
    {
        var index = Slot(banner);
        if (index >= 0) _fresh[index] = value;
    }

    internal static void ClearState()
    {
        Array.Clear(_kills);
        Array.Clear(_fresh);
        Array.Clear(_claimable);
    }

    /// <summary>网络同步用：直接把侧表里这面旗子的击杀数写成 <paramref name="kills"/>（服务器说了算）。</summary>
    internal static void SetKills(int banner, int kills)
    {
        var index = Slot(banner);
        if (index < 0) return;

        _kills[index] = kills < 0 ? 0 : kills;
    }

    /// <summary>网络同步用：直接把可领取面数写成服务器给的值（客户端的本地增量不作数）。</summary>
    internal static void SetClaimable(int banner, int count)
    {
        var index = Slot(banner);
        if (index < 0) return;

        _claimable[index] = count < 0 ? 0 : count;
        if (_claimable[index] > 0) BannerSystem.AnyNewClaimableBanners = true;
    }

    /// <summary>网络同步用：所有登记过的旗子的击杀数与可领取面数（服务器补给新客户端时用）。</summary>
    internal static IEnumerable<(int Banner, int Kills, int Claimable)> NetworkState()
    {
        foreach (var (banner, _) in ItemOfBanner)
        {
            var index = Slot(banner);
            if (index < 0) continue;

            yield return (banner, _kills[index], _claimable[index]);
        }
    }

    /// <summary>(旗子方块类型, 样式) → 旗子号：附近点亮 buff 时按方块反查。</summary>
    private static readonly Dictionary<(int Tile, int Style), int> BannerOfTile = [];

    /// <summary>方块 → 旗子号；没登记返回 0。</summary>
    internal static int BannerOfTileAt(int tileType, int style) =>
        BannerOfTile.TryGetValue((tileType, style), out var banner) ? banner : 0;

    /// <summary>
    /// 给 mod 旗子物品补上默认值：<c>BannerStrength = Reduced</c>（Enabled）、
    /// <c>KillsToBanner = 50</c>。作者在 SetStaticDefaults 里改过的值不覆盖。
    /// </summary>
    private static void EnsureItemTables(int item)
    {
        var strength = ItemID.Sets.BannerStrength;
        if (strength is not null)
        {
            if (item >= strength.Length) strength = Grow(strength, item + 1, ItemID.BannerEffect.Reduced);
            if (item < strength.Length && !strength[item].Enabled) strength[item] = ItemID.BannerEffect.Reduced;
        }

        var kills = ItemID.Sets.KillsToBanner;
        if (kills is not null)
        {
            if (item >= kills.Length) kills = Grow(kills, item + 1, DefaultClaimStep);
            if (item < kills.Length && kills[item] <= 0) kills[item] = DefaultClaimStep;
        }
    }

    private static T[] Grow<T>(T[] table, int need, T fill)
    {
        var longer = new T[need];
        Array.Copy(table, longer, table.Length);
        for (var index = table.Length; index < need; index++) longer[index] = fill;
        return longer;
    }

    /// <summary>存档键（Mod/类名）；未登记返回空串。</summary>
    internal static string KeyOf(int banner) => KeyOfBanner.TryGetValue(banner, out var key) ? key : "";

    /// <summary>把存档里的计数写回去；键对不上就跳过。</summary>
    internal static void Restore(string key, int kills, bool fresh)
    {
        foreach (var (banner, stored) in KeyOfBanner)
        {
            if (!string.Equals(stored, key, StringComparison.Ordinal)) continue;

            var index = Slot(banner);
            if (index < 0) return;

            _kills[index] = kills;
            _fresh[index] = fresh;
            return;
        }
    }

    /// <summary>存档用：遍历所有非零的旗子。</summary>
    internal static IEnumerable<int> Dirty()
    {
        foreach (var (banner, _) in ItemOfBanner)
        {
            var index = Slot(banner);
            if (index >= 0 && (_kills[index] != 0 || _fresh[index])) yield return banner;
        }
    }

    /// <summary>
    /// 建表：每个 ModNpc 的 Banner / BannerItem 必须成对出现：
    /// 代表 NPC 两个都填；同族 NPC 只填 Banner（指向代表的类型号），BannerItem 留空。
    /// 冲突与漏填直接抛（作者声明错误不降级成警告）。
    /// </summary>
    internal static void Build()
    {
        BannerOfNpc.Clear();
        ItemOfBanner.Clear();
        BannerOfItem.Clear();
        KeyOfBanner.Clear();
        BannerOfTile.Clear();
        _kills = [];
        _fresh = [];
        _claimable = [];

        var npcs = ModContent.GetContent<ModNpc>();
        var size = 0;

        // 第一遍：代表 NPC（Banner + BannerItem 都有）。
        foreach (var npc in npcs)
        {
            var banner = npc.Banner;
            var item = npc.BannerItem;
            var name = npc.Mod.Name + "/" + npc.Name;

            if (banner <= 0)
            {
                if (item > 0)
                    throw new InvalidOperationException($"{name}: 填了 BannerItem（{item}）但没填 Banner");
                continue;
            }

            // 同族 NPC：Banner 指向代表，BannerItem 留空；第二遍再校验代表存在。
            if (item <= 0) continue;

            if (banner <= Base)
                throw new InvalidOperationException($"{name}: Banner 必须是本 Mod 的 NPC 类型号，不能指向原版 NPC（{banner}）");

            if (ItemOfBanner.TryGetValue(banner, out var previous) && previous != item)
                throw new InvalidOperationException($"{name}: 旗子 {banner} 已经对应物品 {previous}，不能再对应 {item}");

            if (BannerOfItem.TryGetValue(item, out var other) && other != banner)
                throw new InvalidOperationException($"{name}: 物品 {item} 已经属于旗子 {other}，不能再属于 {banner}");

            BannerOfNpc[npc.Type] = banner;
            ItemOfBanner[banner] = item;
            BannerOfItem[item] = banner;
            KeyOfBanner[banner] = name;

            var slot = banner - Base + 1;
            if (slot > size) size = slot;

            // 旗子方块反查：物品的 createTile + placeStyle 就是"这一面旗"的方块外观。
            var sample = ContentSamples.ItemsByType[item];
            if (sample is not null && sample.createTile > 0) BannerOfTile[(sample.createTile, sample.placeStyle)] = banner;

            EnsureItemTables(item);
        }

        // 第二遍：同族 NPC（只填 Banner）。代表必须已经登记，否则计数会落空。
        foreach (var npc in npcs)
        {
            var banner = npc.Banner;
            if (banner <= 0 || npc.BannerItem > 0) continue;

            var name = npc.Mod.Name + "/" + npc.Name;
            if (!ItemOfBanner.ContainsKey(banner))
                throw new InvalidOperationException($"{name}: 同族 NPC 指向的旗子 {banner} 没有代表 NPC 声明 BannerItem");

            BannerOfNpc[npc.Type] = banner;
        }

        _kills = new int[size];
        _fresh = new bool[size];
        _claimable = new int[size];
    }

    private static int Slot(int banner)
    {
        var index = banner - Base;
        return index >= 0 && index < _kills.Length ? index : -1;
    }
}
