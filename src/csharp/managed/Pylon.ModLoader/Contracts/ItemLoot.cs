using Pylon.ModLoader.Content;
using Pylon.ModLoader.Hooks;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Pylon.ModLoader.Pipelines;
using Terraria;
using Terraria.GameContent.ItemDropRules;

namespace Pylon.ModLoader.Contracts;
/// <summary>打开一个物品（Boss 袋、箱子）吐什么，像 tModLoader 的 ItemLoot；游戏没有按物品的掉落表（ItemDropDatabase 只认识 NPC），规则按类型存在这。</summary>
public class ItemLoot : ILoot
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
        if (!ByType.ContainsKey(type)) Populate(type, Sample(type), m);

        return ByType[type];
    }

    public static List<IItemDropRule> For(Item item)
    {
        var type = item.type;
        if (!ByType.ContainsKey(type))
        {
            var mod = ItemPipeline.Instance.InstanceOf(item);
            Populate(type, item, mod);
        }

        return ByType[type];
    }

    private static void Populate(int type, Item item, ModItem? mod)
    {
        var loot = new ItemLoot(type);

        if (mod is not null && mod.Plan.Has(HookId.ModifyItemLoot))
        {
            try
            {
                mod.ModifyItemLoot(loot);
            }
            catch (Exception e)
            {
                ContentErrors.Line($"[loot] {mod.Name}.ModifyItemLoot 异常：{Safe.Dump(e)}");
            }
        }

        foreach (var global in GlobalItemRegistry.Of(item))
        {
            if (!global.Plan.Has(HookId.ModifyItemLoot)) continue;

            try
            {
                global.ModifyItemLoot(item, loot);
            }
            catch (Exception e)
            {
                ContentErrors.Line($"[loot] {global.FullKey}.ModifyItemLoot 异常：{Safe.Dump(e)}");
            }
        }
    }

    private static Item Sample(int type)
    {
        var item = new Item();
        item.SetDefaults(type, null);
        return item;
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

    public List<IItemDropRule> Get(bool includeGlobalDrops = true) => new(Rules);

    public void RemoveWhere(Func<IItemDropRule, bool> predicate, bool includeGlobalDrops = true)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        foreach (var rule in Get())
        {
            if (predicate(rule)) Remove(rule);
        }
    }
}
