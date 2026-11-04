namespace Pylon.ModLoader;

/// <summary>一个方块实体类型的数据。</summary>
internal struct TileEntityMeta
{
    public HookMask Masks;
    public bool NeedsUpdate;
}

/// <summary>
/// 方块实体侧表：模板按类型号密集索引（类型号 = 注册序，0 起）。
/// 实例绑定在 <see cref="TileEntityBinding"/>；这里只放"这个类型是什么"。
/// </summary>
internal static class TileEntityTable
{
    private static ModTileEntity?[] _templates = new ModTileEntity?[8];
    private static TileEntityMeta[] _meta = new TileEntityMeta[8];
    private static readonly Dictionary<string, ModTileEntity> ByKey = new(StringComparer.Ordinal);
    private static int _next;

    /// <summary>已发号的类型数（类型号 0..Count-1）。</summary>
    internal static int Count => _next;

    internal static ModTileEntity? Template(int type) =>
        (uint)type < (uint)_templates.Length ? _templates[type] : null;

    internal static ref TileEntityMeta Meta(int type) => ref _meta[type];

    /// <summary>这个类型覆写了哪些钩子；越界或未发号给空位集。</summary>
    internal static HookMask MasksOf(int type) =>
        (uint)type < (uint)_meta.Length ? _meta[type].Masks : HookMask.Empty;

    /// <summary>要不要进每一帧的更新循环（只看有没有覆写 Update）。</summary>
    internal static bool NeedsUpdate(int type) =>
        (uint)type < (uint)_meta.Length && _meta[type].NeedsUpdate;

    internal static ModTileEntity? ByName(string key) =>
        ByKey.TryGetValue(key, out var template) ? template : null;

    /// <summary>侧车存档与网络用的键：&lt;mod&gt;/&lt;类名&gt;。</summary>
    internal static string KeyOf(int type) => Template(type) is { } template ? KeyOf(template) : "";

    internal static string KeyOf(ModTileEntity template) => template.Mod.Name + "/" + template.Name;

    /// <summary>给还没发号的模板发号并算位集；幂等，每次 Mod 注册后跑一次。</summary>
    internal static void Assign()
    {
        foreach (var template in ModContent.GetContent<ModTileEntity>())
        {
            if (template.Registered) continue;

            var type = _next;
            Ensure(type + 1);
            _templates[type] = template;

            // 注册管线算的是相对 ModType 的位集：继承来的空实现会被算成覆写，这里按
            // ModTileEntity 重算一遍，RequiresUpdates 才跟着对。
            ref var meta = ref _meta[type];
            meta.Masks = HookScan.Of(template.GetType(), typeof(ModTileEntity));
            meta.NeedsUpdate = meta.Masks.Has(HookId.Update);

            template.Type = type;
            template.Plan = meta.Masks;
            template.Registered = true;
            ByKey[KeyOf(template)] = template;
            _next = type + 1;
        }
    }

    private static void Ensure(int size)
    {
        if (size <= _templates.Length) return;

        var capacity = Math.Max(size, _templates.Length * 2);

        var grownTemplates = new ModTileEntity?[capacity];
        Array.Copy(_templates, grownTemplates, _templates.Length);
        _templates = grownTemplates;

        var grownMeta = new TileEntityMeta[capacity];
        Array.Copy(_meta, grownMeta, _meta.Length);
        _meta = grownMeta;
    }
}
