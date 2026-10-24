using Pylon.ModLoader.Content;
using Terraria;

namespace Pylon.ModLoader.Contracts;
/// <summary>城镇 NPC 的普通商店：Add 填商品、Register 占号（从 99 往下发）；手机端货架由商店管线在 SetupShop 时装，FinishSetup 后条目按排序落位。</summary>
public class NpcShop : AbstractNpcShop
{
    private readonly List<Entry> _entries = [];

    public NpcShop(int npcType, string name = "Shop")
        : base(npcType, name)
    {
        Index = -1;
    }

    public IReadOnlyList<Entry> Entries => _entries;

    protected override IEnumerable<AbstractNpcShop.Entry> AllEntries => _entries;

    public bool FillLastSlot { get; private set; }

    internal int Index;

    internal bool Vanilla, Modified;

    public Entry GetEntry(int item) => _entries.First(e => e.Item.type == item);

    public bool TryGetEntry(int item, out Entry entry)
    {
        entry = _entries.FirstOrDefault(e => e.Item.type == item)!;
        return entry is not null;
    }

    public NpcShop AllowFillingLastSlot()
    {
        FillLastSlot = true;
        return this;
    }

    public NpcShop Add(params Entry[] added)
    {
        _entries.AddRange(added);
        return this;
    }

    public NpcShop Add(Item item, params Condition[] conditions) => Add(new Entry(item, conditions));

    public NpcShop Add(int item, params Condition[] conditions) => Add(new Entry(item, conditions));

    public NpcShop Add<T>(params Condition[] conditions) where T : ModItem => Add(ModContent.ItemType<T>(), conditions);

    public NpcShop InsertBefore(int target, int item, params Condition[] conditions) =>
        InsertBefore(GetEntry(target), Entry.Create(item), conditions);

    public NpcShop InsertAfter(int target, int item, params Condition[] conditions) =>
        InsertAfter(GetEntry(target), Entry.Create(item), conditions);

    public NpcShop InsertBefore(int target, Item item, params Condition[] conditions) =>
        InsertBefore(GetEntry(target), item, conditions);

    public NpcShop InsertAfter(int target, Item item, params Condition[] conditions) =>
        InsertAfter(GetEntry(target), item, conditions);

    public NpcShop InsertBefore(Entry target, Item item, params Condition[] conditions) =>
        Add(new Entry(item, conditions).SortBefore(target));

    public NpcShop InsertAfter(Entry target, Item item, params Condition[] conditions) =>
        Add(new Entry(item, conditions).SortAfter(target));

    public override void FinishSetup()
    {
        var ordered = new List<Entry>();
        var visiting = new HashSet<Entry>();

        void Visit(Entry entry)
        {
            if (ordered.Contains(entry)) return;
            if (!visiting.Add(entry)) throw new InvalidOperationException($"{FullName}: shop entry ordering loop");
            foreach (var before in _entries.Where(e => e.Target == entry && !e.After)) Visit(before);
            ordered.Add(entry);
            foreach (var after in _entries.Where(e => e.Target == entry && e.After)) Visit(after);
            visiting.Remove(entry);
        }

        foreach (var entry in _entries.Where(e => e.Target is null && !e.OrdersLast)) Visit(entry);
        foreach (var entry in _entries.Where(e => e.Target is null && e.OrdersLast)) Visit(entry);

        if (ordered.Count != _entries.Count) throw new InvalidOperationException($"{FullName}: shop ordering target is not registered in this shop");

        _entries.Clear();
        _entries.AddRange(ordered);
    }

    public override void FillShop(ICollection<Item> items, NPC npc)
    {
        foreach (var entry in _entries)
        {
            if (entry.Disabled) continue;

            var available = entry.ConditionsMet();
            if (!available && !entry.SlotReserved) continue;

            var item = available ? entry.Item.Clone() : Entry.Create(0);
            item.isAShopItem = true;
            if (available) entry.OnShopOpen(item, npc);
            items.Add(item);
        }
    }

    public override void FillShop(Item[] items, NPC npc, out bool overflow)
    {
        Array.Clear(items);
        var filled = new List<Item>();
        FillShop(filled, npc);

        var limit = Math.Max(0, items.Length - (FillLastSlot ? 0 : 1));
        var count = Math.Min(limit, filled.Count);
        for (var i = 0; i < count; i++) items[i] = filled[i];
        overflow = filled.Count > limit;
    }

    public void Open() => NpcShopDatabase.OpenShop(FullName);

    public new class Entry : AbstractNpcShop.Entry
    {
        internal Entry? Target;

        internal bool After, Native;

        private Action<Item, NPC>? _opened;

        public bool OrdersLast { get; private set; }

        public bool SlotReserved { get; private set; }

        public Entry(int item, params Condition[] conditions)
            : this(Create(item), conditions)
        {
        }

        public Entry(Item item, params Condition[] conditions)
            : base(item, conditions)
        {
        }

        internal static Item Create(int type)
        {
            var item = new Item();
            item.SetDefaults(type, null);
            return item;
        }

        private Entry Ordering(Entry target, bool after)
        {
            ArgumentNullException.ThrowIfNull(target);
            for (Entry? e = target; e is not null; e = e.Target)
            {
                if (e == this) throw new InvalidOperationException("Shop entry ordering loop");
            }
            Target = target;
            After = after;
            return this;
        }

        public Entry SortBefore(Entry target) => Ordering(target, false);

        public Entry SortAfter(Entry target) => Ordering(target, true);

        public new Entry AddCondition(Condition condition)
        {
            base.AddCondition(condition);
            return this;
        }

        public Entry OrderLast()
        {
            OrdersLast = true;
            return this;
        }

        public Entry ReserveSlot()
        {
            SlotReserved = true;
            return this;
        }

        public Entry AddShopOpenedCallback(Action<Item, NPC> callback)
        {
            _opened += callback;
            return this;
        }

        public void OnShopOpen(Item item, NPC npc) => _opened?.Invoke(item, npc);
    }
}
