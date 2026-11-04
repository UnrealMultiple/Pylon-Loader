namespace Pylon.ModLoader;

/// <summary>钩子订阅表：每个钩子一条稠密数组，按 Mod 加载顺序排列；注册期构建，分派期只读。</summary>
internal sealed class HookBus
{
    private static readonly int HookCount = Enum.GetValues<HookId>().Length;

    static HookBus()
    {
        if (HookCount > HookMask.Capacity)
            throw new InvalidOperationException($"HookId 超出位集容量：{HookCount} > {HookMask.Capacity}");
    }

    private Mod[][] _channels = CreateEmpty();

    /// <summary>这个钩子的订阅者（按加载顺序）；没有订阅者时返回空 span。</summary>
    public ReadOnlySpan<Mod> Channel(HookId id) => _channels[(int)id];

    /// <summary>按每个 Mod 的覆写位集重建全部通道。</summary>
    public void Rebuild(IReadOnlyList<Mod> mods)
    {
        var counts = new int[HookCount];
        Span<HookId> hooks = stackalloc HookId[HookCount];

        for (var i = 0; i < mods.Count; i++)
        {
            var written = mods[i].Plan.Collect(hooks);
            for (var h = 0; h < written; h++) counts[(int)hooks[h]]++;
        }

        var channels = CreateEmpty();
        for (var id = 0; id < HookCount; id++)
        {
            if (counts[id] > 0) channels[id] = new Mod[counts[id]];
        }

        var cursors = new int[HookCount];
        for (var i = 0; i < mods.Count; i++)
        {
            var mod = mods[i];
            var written = mod.Plan.Collect(hooks);
            for (var h = 0; h < written; h++)
            {
                var id = (int)hooks[h];
                channels[id][cursors[id]++] = mod;
            }
        }

        _channels = channels;
    }

    private static Mod[][] CreateEmpty()
    {
        var channels = new Mod[HookCount][];
        Array.Fill(channels, Array.Empty<Mod>());
        return channels;
    }
}
