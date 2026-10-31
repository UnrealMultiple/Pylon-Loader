using System.Reflection;
using Terraria;
using Terraria.GameContent.ItemDropRules;

namespace Pylon.ModLoader;

/// <summary>对每个 NPC 都生效的掉落规则（游戏的 _globalEntries）；ModifyGlobalLoot 里 this.Add/this.RemoveWhere 改它，也能用 RegisterToNPC 改单个 NPC 的表。</summary>
public class GlobalLoot
{
    /// <summary>游戏的掉落库（全局表、按 NPC 的表都在里面）；ModifyGlobalLoot 收到的 globalLoot 就是它。</summary>
    public ItemDropDatabase ItemDropDatabase => Main.ItemDropsDB;

    /// <summary>全部全局规则（快照，改它不影响游戏）；RemoveWhere 对着它轮。</summary>
    public List<IItemDropRule> Get()
    {
        var result = new List<IItemDropRule>();
        var entries = GlobalEntries;
        if (entries is not null) foreach (var rule in entries) result.Add(rule);
        return result;
    }

    /// <summary>加一条全局规则（游戏的 RegisterToGlobal）；返回 rule 好接着链用。</summary>
    public IItemDropRule Add(IItemDropRule rule)
    {
        Main.ItemDropsDB.RegisterToGlobal(rule);
        return rule;
    }

    /// <summary>从全局表撤掉这条（游戏的 _globalEntries.Remove）；返回 rule。</summary>
    public IItemDropRule Remove(IItemDropRule rule)
    {
        GlobalEntries?.Remove(rule);
        return rule;
    }

    /// <summary>把满足 predicate 的全局规则全撤掉；返回撤了几条。</summary>
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

    /// <summary>全部内容装完之后一次；全局掉落写在这里（this.Add、this.RegisterToNPC…）。</summary>
    public virtual void ModifyGlobalLoot() { }

    /// <summary>某个 NPC 的规则（不带全局规则；游戏的原表，不是拷贝）。</summary>
    public Il2CppSystem.Collections.Generic.List<IItemDropRule> GetRulesForNPCID(int id) =>
        Main.ItemDropsDB.GetRulesForNPCID(id, false);

    /// <summary>给某个 NPC 加一条规则（等效 new NPCLoot(id).Add(rule)）；返回 rule。</summary>
    public IItemDropRule RegisterToNPC(int id, IItemDropRule rule)
    {
        new NPCLoot(id).Add(rule);
        return rule;
    }

    /// <summary>从某个 NPC 撤掉一条规则（等效 new NPCLoot(id).Remove(rule)）；返回 rule。</summary>
    public IItemDropRule RemoveFromNPC(int id, IItemDropRule rule)
    {
        new NPCLoot(id).Remove(rule);
        return rule;
    }

    /// <summary>游戏的全局规则表（_globalEntries 是私有字段，只能反射读；同 NPCHappiness 的 _database 手法）。</summary>
    private static Il2CppSystem.Collections.Generic.List<IItemDropRule>? GlobalEntries =>
        typeof(ItemDropDatabase).GetField("_globalEntries", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(Main.ItemDropsDB) as Il2CppSystem.Collections.Generic.List<IItemDropRule>;
}

// 未移植：构造函数与 __changed 只记"改过表的 NPC"（图鉴刷新用），属装载管线；static register 里的 Templates.Adopt、
// 入 GlobalLootLoader.List、Schedule 同样是装载管线。Pylon 的注册走 ModContent 的自动发现。
