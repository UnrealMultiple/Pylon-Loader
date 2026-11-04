namespace Pylon.ModLoader;

/// <summary>
/// 场景效果的有序表（含群落）。不按 `Type` 索引：群落已经用了 `ModType.Type` 做自己的编号，
/// 而普通场景效果不需要编号——每帧线性扫几十个比维护一套索引简单。
/// </summary>
internal static class SceneEffectTable
{
    private static readonly List<ModSceneEffect> Effects = [];
    private static readonly Dictionary<string, ModSceneEffect> ByKey = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, ModSceneEffect?> ByName = new(StringComparer.Ordinal);

    internal static int Count => Effects.Count;

    internal static ModSceneEffect At(int index) => Effects[index];

    /// <summary>按名字取下标（注册序）；先 <c>Mod名/类名</c>，再裸类名（重名当没有）。找不到 = -1。</summary>
    internal static int Find(string? name)
    {
        if (string.IsNullOrEmpty(name)) return -1;

        var found = -1;
        if (name.IndexOf('/') >= 0)
        {
            if (!ByKey.TryGetValue(name, out var exact)) return -1;
            for (var index = 0; index < Effects.Count; index++)
            {
                if (!ReferenceEquals(Effects[index], exact)) continue;
                found = index;
                break;
            }

            return found;
        }

        if (!ByName.TryGetValue(name, out var loose) || loose is null) return -1;
        for (var index = 0; index < Effects.Count; index++)
        {
            if (!ReferenceEquals(Effects[index], loose)) continue;
            found = index;
            break;
        }

        return found;
    }

    /// <summary>重建表；幂等，每次 Mod 注册后跑一次。</summary>
    internal static void Rebuild()
    {
        Effects.Clear();
        ByKey.Clear();
        ByName.Clear();

        foreach (var effect in ModContent.GetContent<ModSceneEffect>())
        {
            Effects.Add(effect);
            ByKey[effect.Mod.Name + "/" + effect.Name] = effect;
            ByName[effect.Name] = ByName.ContainsKey(effect.Name) ? null : effect;
        }
    }
}
