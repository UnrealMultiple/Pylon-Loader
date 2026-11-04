namespace Pylon.ModLoader;

/// <summary>
/// 内容注册表与发号器。注册只做三件事：分配 ID、写槽表、建索引；不碰游戏对象。
/// 发号顺序固定为「Mod 加载顺序 → 种类序 → 类型全名序数」，跨设备一致。
/// </summary>
internal sealed class ContentRegistry
{
    private readonly List<ModType> _instances = [];
    private readonly Dictionary<Type, ModType> _byClass = [];
    private readonly Dictionary<string, ModType> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<ContentKind, ModType?[]> _slots = [];
    private readonly Dictionary<ContentKind, int> _next = [];
    private readonly Dictionary<Type, object> _byTypeCache = [];
    private ContentDescriptor[] _descriptors = new ContentDescriptor[64];

    public IReadOnlyList<ModType> Instances => _instances;

    public int Count => _instances.Count;

    /// <summary>已登记、等待注入游戏的内容数量。</summary>
    public int Pending { get; private set; }

    /// <summary>该种类的下一个可用 ID。</summary>
    public int NextType(ContentKind kind) =>
        _next.TryGetValue(kind, out var next) ? next : ContentKindTable.BaseCount(kind);

    /// <summary>该种类已注册的数量（每帧门控用，不分配）。</summary>
    public int RegisteredCount(ContentKind kind) => NextType(kind) - ContentKindTable.BaseCount(kind);

    public ModType InstanceAt(int index) => _instances[index];

    public ref readonly ContentDescriptor DescriptorAt(int index) => ref _descriptors[index];

    /// <summary>按种类与 ID 取内容；槽外返回 null。</summary>
    public ModType? ById(ContentKind kind, int type)
    {
        if (!_slots.TryGetValue(kind, out var slots)) return null;
        var index = type - ContentKindTable.BaseCount(kind);
        return (uint)index < (uint)slots.Length ? slots[index] : null;
    }

    public T? InstanceOf<T>() where T : ModType =>
        _byClass.TryGetValue(typeof(T), out var instance) ? (T)instance : null;

    public bool TryFind<T>(string fullName, out T? value) where T : ModType
    {
        if (_byName.TryGetValue(fullName, out var found) && found is T typed)
        {
            value = typed;
            return true;
        }
        value = null;
        return false;
    }

    /// <summary>可赋给 T 的全部实例（按注册顺序；结果按类型缓存，注册变化时失效）。</summary>
    public List<T> ContentOf<T>() where T : ModType
    {
        if (_byTypeCache.TryGetValue(typeof(T), out var cached)) return (List<T>)cached;

        var result = new List<T>();
        for (var i = 0; i < _instances.Count; i++)
        {
            if (_instances[i] is T typed) result.Add(typed);
        }

        _byTypeCache[typeof(T)] = result;
        return result;
    }

    /// <summary>注册一个 Mod 的全部内容：排序后逐个发号。</summary>
    public void Register(Mod mod, List<ModType> types)
    {
        types.Sort(CompareForRegistration);
        for (var i = 0; i < types.Count; i++) RegisterOne(mod, types[i]);
        _byTypeCache.Clear();
    }

    private static int CompareForRegistration(ModType left, ModType right)
    {
        var byKind = KindOf(left).CompareTo(KindOf(right));
        return byKind != 0 ? byKind : string.CompareOrdinal(left.GetType().FullName, right.GetType().FullName);
    }

    private void RegisterOne(Mod mod, ModType type)
    {
        var kind = KindOf(type);

        if (!ContentKindTable.HasTypeId(kind))
        {
            AddIndexes(mod, type, kind, 0, false);
            return;
        }

        var next = NextType(kind);
        if (next >= short.MaxValue)
        {
            mod.Logger.Error($"{type.GetType().FullName} 拿不到 ID（{kind} 已到 {next}）");
            return;
        }

        type.Type = next;
        type.Registered = true;
        _next[kind] = next + 1;

        var slots = _slots.TryGetValue(kind, out var existing) ? existing : new ModType?[16];
        var slot = next - ContentKindTable.BaseCount(kind);
        if (slot >= slots.Length) Array.Resize(ref slots, Math.Max(slot + 1, slots.Length * 2));
        slots[slot] = type;
        _slots[kind] = slots;

        AddIndexes(mod, type, kind, next, true);
        ContentKindHooks.OnRegistered(type);
    }

    private void AddIndexes(Mod mod, ModType type, ContentKind kind, int id, bool hasId)
    {
        var index = _instances.Count;
        _instances.Add(type);
        _byClass[type.GetType()] = type;
        _byName[(mod.Name.Length == 0 ? "" : mod.Name + "/") + type.Name] = type;

        if (_descriptors.Length == index) Array.Resize(ref _descriptors, index * 2);
        _descriptors[index] = new ContentDescriptor(kind, id, mod.Index, index);

        if (hasId) Pending++;
    }

    public static ContentKind KindOf(ModType type) => type switch
    {
        ModItem => ContentKind.Item,
        ModTile => ContentKind.Tile,
        ModWall => ContentKind.Wall,
        ModProjectile => ContentKind.Projectile,
        ModNpc => ContentKind.Npc,
        ModBuff => ContentKind.Buff,
        ModMount => ContentKind.Mount,
        ModPrefix => ContentKind.Prefix,
        ModRarity => ContentKind.Rarity,
        _ => ContentKind.Global,
    };
}
