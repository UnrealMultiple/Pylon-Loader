using Terraria;
using Terraria.GameContent.ItemDropRules;

namespace Pylon.ModLoader;

/// <summary>一个 NPC 类型的掉落表（ModifyNPCLoot 里发进来）；Add/Remove 直接改游戏 ItemDropsDB 里那个类型的表，规则照用游戏的 IItemDropRule。</summary>
public class NPCLoot
{
    /// <summary>抓住这个 NPC 类型（netID）的掉落；changed 初始 false（见 Changed 的图鉴刷新判定）。</summary>
    public NPCLoot(int type)
    {
        Type = type;
        Changed = false;
    }

    /// <summary>这个实例对应的 NPC netID。</summary>
    public int Type { get; }

    /// <summary>Add/Remove 之后变 true；Loader 靠它知道这张表动过（要刷新图鉴掉落）。</summary>
    public bool Changed { get; set; }

    /// <summary>给这个 NPC 加一条掉落规则（游戏 RegisterToNPC）；返回 rule 好接着用。</summary>
    public IItemDropRule Add(IItemDropRule rule)
    {
        Main.ItemDropsDB.RegisterToNPC(Type, rule);
        Changed = true;
        return rule;
    }

    /// <summary>从这 NPC 撤掉一条掉落规则（游戏 RemoveFromNPC）；返回 rule。</summary>
    public IItemDropRule Remove(IItemDropRule rule)
    {
        Main.ItemDropsDB.RemoveFromNPC(Type, rule);
        Changed = true;
        return rule;
    }

    /// <summary>这 NPC 的掉落规则快照（不算全局规则）；新 List 拷贝，外头改不影响游戏的表。</summary>
    public List<IItemDropRule> Get()
    {
        var result = new List<IItemDropRule>();
        foreach (var rule in Main.ItemDropsDB.GetRulesForNPCID(Type, false)) result.Add(rule);
        return result;
    }

    /// <summary>把满足 predicate 的规则全撤掉（对着 Get 的快照轮）；返回撤了几条。</summary>
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
