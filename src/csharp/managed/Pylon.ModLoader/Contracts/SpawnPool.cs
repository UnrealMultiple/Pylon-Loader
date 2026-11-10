using Terraria;

namespace Pylon.ModLoader;

/// <summary>自然生成抽选（EditSpawnPool 的池子）：NPC 类型 -> 权重；0 是游戏自己的生成（权重 1），mod 的经 SpawnChance 进来。</summary>
public class SpawnPool
{
    /// <summary>条目本体：类型 -> 权重。</summary>
    private readonly Dictionary<int, float> _entries = [];

    /// <summary>抽选里有几条（Object.keys 的长度）。</summary>
    public int Count => _entries.Count;

    /// <summary>直接设置或替换权重,可将类型 0 的权重设为 0 禁用原版抽选。</summary>
    public float this[int type] { get => _entries[type]; set => _entries[type] = value; }

    public bool TryGetValue(int type, out float weight) => _entries.TryGetValue(type, out weight);

    /// <summary>抽选里的类型；每次访问新建一份（Object.keys 的结果）。</summary>
    public List<int> Keys => [.. _entries.Keys];

    public void Add(int type, float weight)
    {
        if (_entries.ContainsKey(type)) throw new ArgumentException($"SpawnPool.Add: 类型 {type} 已经在抽选里");
        _entries[type] = weight;
    }

    /// <summary>类型在不在抽选里（hasOwnProperty）。</summary>
    public bool ContainsKey(int type) => _entries.ContainsKey(type);

    /// <summary>删掉一条；返回删没删到（本来就没有也返回 false）。</summary>
    public bool Remove(int type) => _entries.Remove(type);

    /// <summary>清空全部条目。</summary>
    public void Clear() => _entries.Clear();

    /// <summary>用 Main.rand 抽一个类型；总权重 0 返回 null（什么都不生成），负权重按 0 算。</summary>
    public int? Choose()
    {
        var count = ModContent.NextType(ContentKind.Npc);
        return Choose(Main.rand.NextDouble(), count);
    }

    /// <summary>只让有限正权重和合法类型参与,避免 NaN/Infinity 污染整个池。</summary>
    internal int? Choose(double sample, int typeCount)
    {
        double total = 0;
        foreach (var entry in _entries)
            if (Valid(entry, typeCount)) total += entry.Value;
        if (total <= 0) return null;

        var remaining = sample * total;
        foreach (var entry in _entries)
        {
            if (!Valid(entry, typeCount)) continue;
            if (remaining < entry.Value) return entry.Key;
            remaining -= entry.Value;
        }
        return null;
    }

    private static bool Valid(KeyValuePair<int, float> entry, int typeCount) =>
        entry.Key >= -65 && entry.Key < typeCount && float.IsFinite(entry.Value) && entry.Value > 0;
}