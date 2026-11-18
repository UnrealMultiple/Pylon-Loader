using Terraria;
using Terraria.GameContent.ItemDropRules;

namespace Pylon.ModLoader.Contracts;
/// <summary>对全部 NPC 生效的全局掉落表（游戏 ItemDropsDB 的 _globalEntries）；ModifyGlobalLoot 里 Add/Remove/RemoveWhere 改它。</summary>
public readonly struct GlobalLoot : ILoot
{
    public List<IItemDropRule> Get(bool includeGlobalDrops = true)
    {
        var result = new List<IItemDropRule>();
        var entries = GlobalEntries;
        if (entries is not null)
        {
            for (var i = 0; i < entries.Count; i++)
            {
                var rule = entries[i];
                if (rule is not null) result.Add(rule);
            }
        }
        return result;
    }

    public IItemDropRule Add(IItemDropRule entry)
    {
        Main.ItemDropsDB.RegisterToGlobal(entry);
        return entry;
    }

    public IItemDropRule Remove(IItemDropRule entry)
    {
        var entries = GlobalEntries;
        if (entries is null) return entry;

        var target = entry.Pointer;
        var removed = false;
        var kept = new List<IItemDropRule>();

        for (var i = 0; i < entries.Count; i++)
        {
            var rule = entries[i];
            if (rule is null) continue;
            if (!removed && rule.Pointer == target)
            {
                removed = true;
                continue;
            }
            kept.Add(rule);
        }

        if (!removed) return entry;

        entries.Clear();
        foreach (var rule in kept) entries.Add(rule);
        return entry;
    }

    public void RemoveWhere(Func<IItemDropRule, bool> predicate, bool includeGlobalDrops = true)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        foreach (var entry in Get())
        {
            if (predicate(entry)) Remove(entry);
        }
    }

    private static Il2CppSystem.Collections.Generic.List<IItemDropRule>? GlobalEntries => Main.ItemDropsDB._globalEntries;
}
