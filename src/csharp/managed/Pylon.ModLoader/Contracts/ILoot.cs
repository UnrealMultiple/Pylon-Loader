using Terraria.GameContent.ItemDropRules;

namespace Pylon.ModLoader.Contracts;
/// <summary>一份掉落规则表的统一入口（ItemLoot / NPCLoot / GlobalLoot 都实现它）。</summary>
public interface ILoot
{
    List<IItemDropRule> Get(bool includeGlobalDrops = true);

    IItemDropRule Add(IItemDropRule entry);

    IItemDropRule Remove(IItemDropRule entry);

    void RemoveWhere(Func<IItemDropRule, bool> predicate, bool includeGlobalDrops = true);
}
