using Terraria;
using Terraria.GameContent.ItemDropRules;

namespace Pylon.ModLoader.Contracts;
/// <summary>一个 NPC 类型的掉落表（ModifyNPCLoot 里发进来）；Add/Remove 直接改游戏 ItemDropsDB 里那个类型的表，规则照用游戏的 IItemDropRule。</summary>
public class NPCLoot : ILoot
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

    /// <summary>
    /// 给这个 NPC 加一条掉落规则。走 <c>RegisterToNPCNetId</c>：
    /// <c>RegisterToNPC(type, …)</c> 要靠游戏内部那张 type→netID 映射表，mod NPC 不在表里，
    /// 规则会注册到空处（能读到规则数，但击杀时永远命中不到）。
    /// </summary>
    public IItemDropRule Add(IItemDropRule rule)
    {
        Main.ItemDropsDB.RegisterToNPCNetId(Type, rule);
        Changed = true;
        return rule;
    }

    /// <summary>从这 NPC 撤掉一条掉落规则（同样按 netID）；返回 rule。</summary>
    public IItemDropRule Remove(IItemDropRule rule)
    {
        Main.ItemDropsDB.RemoveFromNPCNetId(Type, rule);
        Changed = true;
        return rule;
    }

    public List<IItemDropRule> Get(bool includeGlobalDrops = true)
    {
        var result = new List<IItemDropRule>();
        foreach (var rule in Main.ItemDropsDB.GetRulesForNPCID(Type, includeGlobalDrops)) result.Add(rule);
        return result;
    }

    public void RemoveWhere(Func<IItemDropRule, bool> predicate, bool includeGlobalDrops = true)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        foreach (var rule in Get(includeGlobalDrops))
        {
            if (predicate(rule)) Remove(rule);
        }
    }
}
