using Terraria.GameContent.ItemDropRules;

namespace Pylon.ModLoader;

/// <summary>打开一个物品（Boss 袋、箱子）吐什么，像 tModLoader 的 ItemLoot；游戏没有按物品的掉落表（ItemDropDatabase 只认识 NPC），规则按类型存在这。</summary>
public class ItemLoot
{
    // ByType 是跨实例共享的规则表：For 的"第一次"判定与构造函数拿到的是同一份。
    private static readonly Dictionary<int, List<IItemDropRule>> ByType = [];

    /// <summary>抓住一个物品类型；规则表是这份类型共享的那一份（没有就先建空表）。</summary>
    public ItemLoot(int type)
    {
        Type = type;
        if (!ByType.TryGetValue(type, out var rules)) ByType[type] = rules = [];
        Rules = rules;
    }

    /// <summary>这个实例对应的物品类型。</summary>
    public int Type { get; }

    /// <summary>这份类型共享的规则表（同一类型的实例拿同一份；Add/Remove 直接改它）。</summary>
    public List<IItemDropRule> Rules { get; }

    /// <summary>这份类型的所有规则；第一次问时跑一次 ModifyItemLoot。</summary>
    public static List<IItemDropRule> For(ModItem m)
    {
        var type = m.Type;
        if (!ByType.ContainsKey(type)) m.ModifyItemLoot(new ItemLoot(type));

        return ByType[type];
    }

    /// <summary>加一条规则；返回 rule 好接着链用。</summary>
    public IItemDropRule Add(IItemDropRule rule)
    {
        Rules.Add(rule);
        return rule;
    }

    /// <summary>撤掉第一条相等的规则；没有就是 no-op，返回 rule。</summary>
    public IItemDropRule Remove(IItemDropRule rule)
    {
        Rules.Remove(rule);
        return rule;
    }

    /// <summary>规则表的快照（外头随便改，不影响表）。</summary>
    public List<IItemDropRule> Get() => new(Rules);

    /// <summary>把满足 predicate 的规则全撤掉（对着快照轮）；返回撤了几条。</summary>
    public int RemoveWhere(Func<IItemDropRule, bool> predicate)
    {
        var removed = 0;
        foreach (var rule in Get())
        {
            if (!predicate(rule)) continue;

            Remove(rule);
            removed++;
        }
        return removed;
    }
}
