namespace Pylon.ModLoader;

/// <summary>
/// 生物群系侧表：编号 = 注册序（0 基础）。`ModBiome` 与普通场景效果各用一套编号，
/// 都写在 `ModType.Type` 上但互不混用——一个实例只会出现在其中一张表里。
/// </summary>
internal static class BiomeTable
{
    private static readonly Dictionary<string, ModBiome> ByKey = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, ModBiome?> ByName = new(StringComparer.Ordinal);

    private static ModBiome?[] _biomes = new ModBiome?[8];
    private static int _next;

    internal static int Count => _next;

    internal static ModBiome? At(int index) => (uint)index < (uint)_biomes.Length ? _biomes[index] : null;

    /// <summary>按名字取编号：先 <c>Mod名/类名</c>，再裸类名（重名当没有）。</summary>
    internal static int Find(string? name)
    {
        if (string.IsNullOrEmpty(name)) return -1;

        if (name.IndexOf('/') >= 0) return ByKey.TryGetValue(name, out var exact) ? exact.Type : -1;
        return ByName.TryGetValue(name, out var loose) && loose is not null ? loose.Type : -1;
    }

    /// <summary>发号；幂等，每次 Mod 注册后跑一次。</summary>
    internal static void Assign()
    {
        foreach (var biome in ModContent.GetContent<ModBiome>())
        {
            if (biome.Registered) continue;

            if (_next == _biomes.Length) Array.Resize(ref _biomes, _biomes.Length * 2);

            biome.Type = _next;
            biome.Registered = true;
            _biomes[_next++] = biome;

            ByKey[biome.Mod.Name + "/" + biome.Name] = biome;
            ByName[biome.Name] = ByName.ContainsKey(biome.Name) ? null : biome;
        }
    }
}
