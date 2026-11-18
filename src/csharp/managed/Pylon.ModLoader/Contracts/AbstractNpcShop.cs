using Terraria;

namespace Pylon.ModLoader;

/// <summary>城镇商店的公共面：店名、条目集合与装货架的形状；普通店用 NpcShop（手机端注册只收它），条目的 Item 会被钉住防回收。</summary>
public abstract class AbstractNpcShop
{
    public class Entry
    {
        private readonly List<Condition> _conditions = [];

        internal readonly System.IntPtr Pin;

        public Entry(Item item, params Condition[] conditions)
        {
            ArgumentNullException.ThrowIfNull(item);
            Item = item;
            Pin = ObjPin.Hold(item);
            if (conditions is not null) _conditions.AddRange(conditions);
        }

        public Item Item { get; }

        public IEnumerable<Condition> Conditions => _conditions;

        public bool Disabled { get; private set; }

        public void Disable() => Disabled = true;

        public void AddCondition(Condition condition)
        {
            ArgumentNullException.ThrowIfNull(condition);
            _conditions.Add(condition);
        }

        public bool ConditionsMet()
        {
            foreach (var condition in _conditions)
            {
                if (!condition.IsMet()) return false;
            }
            return true;
        }
    }

    public int NpcType { get; }

    public string Name { get; }

    public string FullName => NpcShopDatabase.GetShopName(NpcType, Name);

    protected abstract IEnumerable<Entry> AllEntries { get; }

    public IEnumerable<Entry> ActiveEntries => AllEntries.Where(e => !e.Disabled);

    protected AbstractNpcShop(int npcType, string name = "Shop")
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        NpcType = npcType;
        Name = name;
    }

    public void Register() => NpcShopDatabase.AddShop(this);

    public abstract void FillShop(ICollection<Item> items, NPC npc);

    public abstract void FillShop(Item[] items, NPC npc, out bool overflow);

    public virtual void FinishSetup() { }

    public virtual void RefreshItems(bool onlyIfVariantChanged = true)
    {
        foreach (var entry in AllEntries) entry.Item.Refresh(onlyIfVariantChanged);
    }
}
