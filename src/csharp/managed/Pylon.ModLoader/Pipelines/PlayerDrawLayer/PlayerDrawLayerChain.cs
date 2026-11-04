using Terraria.DataStructures;

namespace Pylon.ModLoader;

/// <summary>
/// 把声明了 <see cref="ModPlayerDrawLayer.Position"/> 的层插进原版绘制链。
/// 做的是"锚点分桶"而不是接管整条链：原版每一层本来就会被自己的方法调用，
/// 我们的逐层钩子在调用前后各放一次槽位，作者层就落在两个原版层之间。
/// </summary>
internal static class PlayerDrawLayerChain
{
    private static readonly int SlotCount = Enum.GetValues<VanillaDrawLayer>().Length;

    private static List<ModPlayerDrawLayer>?[] _before = [];
    private static List<ModPlayerDrawLayer>?[] _after = [];
    private static int _count;

    /// <summary>当前链上层数（0 = 每帧不做任何事）。</summary>
    internal static int Count => _count;

    /// <summary>
    /// 收表：算每个层落在哪个槽位（原版层之前 / 之后），同一槽位里再按 mod 层之间的依赖排。
    /// 返回插进链里的层数。
    /// </summary>
    internal static int Build(List<ModPlayerDrawLayer> positioned)
    {
        _before = new List<ModPlayerDrawLayer>?[SlotCount];
        _after = new List<ModPlayerDrawLayer>?[SlotCount];
        _count = 0;

        if (positioned.Count == 0) return 0;

        var byName = new Dictionary<string, ModPlayerDrawLayer>(StringComparer.Ordinal);
        for (var i = 0; i < positioned.Count; i++) byName[positioned[i].Name] = positioned[i];

        for (var i = 0; i < positioned.Count; i++)
        {
            var layer = positioned[i];
            LayerPosition? position;
            try
            {
                position = layer.Position;
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[playerdrawlayer] {layer.Name}.Position 异常：{Safe.Describe(e)}");
                continue;
            }

            if (position is null) continue;

            var after = position.AfterVanilla;
            var before = position.BeforeVanilla;

            // Between(a, b)：两边都给了时以 a 为锚（b 只用于校验顺序）。
            if (after == VanillaDrawLayer.None && before != VanillaDrawLayer.None)
            {
                Add(_before, before, layer);
            }
            else if (after != VanillaDrawLayer.None)
            {
                if (before != VanillaDrawLayer.None && (int)before <= (int)after)
                {
                    ContentErrors.Report($"[playerdrawlayer] {layer.Name} 的 Between({after}, {before}) 顺序反了，按 {after} 之后处理");
                }

                Add(_after, after, layer);
            }
            else if (position.AfterModLayer is not null || position.BeforeModLayer is not null)
            {
                // 只写了 mod 层锚点、没写原版层锚点：两头都没靠，只能放在链尾。
                Add(_after, VanillaDrawLayer.EyebrellaCloud, layer);
            }
            else
            {
                ContentErrors.Report($"[playerdrawlayer] {layer.Name} 的 Position 没有任何锚点，跳过");
                continue;
            }

            _count++;
        }

        SortAll(byName);
        return _count;
    }

    private static void Add(List<ModPlayerDrawLayer>?[] slots, VanillaDrawLayer layer, ModPlayerDrawLayer value)
    {
        var index = (int)layer;
        if ((uint)index >= (uint)slots.Length) index = slots.Length - 1;

        (slots[index] ??= []).Add(value);
    }

    /// <summary>
    /// 同一槽位里按 mod 层之间的依赖排（AfterMod / BeforeMod）；依赖指向槽外的层时报一次并忽略。
    /// 拓扑排序只在"有依赖"时才跑，没有依赖的槽位保持注册序。
    /// </summary>
    private static void SortAll(Dictionary<string, ModPlayerDrawLayer> byName)
    {
        foreach (var slots in new[] { _before, _after })
        {
            for (var i = 0; i < slots.Length; i++)
            {
                var list = slots[i];
                if (list is null || list.Count <= 1) continue;

                var sorted = Sort(list, byName);
                if (!ReferenceEquals(sorted, list)) slots[i] = sorted;
            }
        }
    }

    private static List<ModPlayerDrawLayer> Sort(List<ModPlayerDrawLayer> list, Dictionary<string, ModPlayerDrawLayer> byName)
    {
        var deps = new List<string>?[list.Count];
        var hasDeps = false;

        for (var i = 0; i < list.Count; i++)
        {
            LayerPosition? position;
            try
            {
                position = list[i].Position;
            }
            catch
            {
                continue;
            }

            if (position is null) continue;

            if (position.AfterModLayer is { } afterName) (deps[i] ??= []).Add(afterName);
            if (position.BeforeModLayer is { } beforeName) (deps[i] ??= []).Add("!" + beforeName);

            if (deps[i] is not null) hasDeps = true;
        }

        if (!hasDeps) return list;

        // 简单拓扑：按注册序反复挑"依赖已满足"的那个；有环就保持剩余注册序并报一次。
        var placed = new bool[list.Count];
        var result = new List<ModPlayerDrawLayer>(list.Count);

        for (var step = 0; step < list.Count; step++)
        {
            var pick = -1;
            for (var i = 0; i < list.Count; i++)
            {
                if (placed[i]) continue;
                if (Ready(i, list, deps, placed, byName)) { pick = i; break; }
            }

            if (pick < 0)
            {
                ContentErrors.Report("[playerdrawlayer] 层位依赖成环，尾部按注册序排列");
                for (var i = 0; i < list.Count; i++)
                {
                    if (!placed[i]) result.Add(list[i]);
                }
                return result;
            }

            placed[pick] = true;
            result.Add(list[pick]);
        }

        return result;
    }

    private static bool Ready(int index, List<ModPlayerDrawLayer> list, List<string>?[] deps, bool[] placed,
                              Dictionary<string, ModPlayerDrawLayer> byName)
    {
        var mine = deps[index];
        if (mine is null) return true;

        for (var i = 0; i < mine.Count; i++)
        {
            var dep = mine[i];
            var negate = dep.Length > 0 && dep[0] == '!';
            var name = negate ? dep[1..] : dep;

            if (!byName.TryGetValue(name, out var other)) continue; // 指向不存在的层：忽略

            var at = list.IndexOf(other);
            if (at < 0) continue; // 在别的槽位：忽略

            // AfterMod：对方必须已经排过；BeforeMod：对方必须还没排。
            var otherPlaced = placed[at];
            if (!negate && !otherPlaced) return false;
            if (negate && otherPlaced) return false;
        }

        return true;
    }

    /// <summary>原版某层画之前：跑这个槽位的作者层。</summary>
    internal static void Before(PlayerDrawSet drawInfo, VanillaDrawLayer layer)
    {
        if (_count == 0) return;

        var list = _before[(int)layer];
        if (list is null) return;

        Run(list, ref drawInfo);
    }

    /// <summary>原版某层画之后：跑这个槽位的作者层。</summary>
    internal static void After(PlayerDrawSet drawInfo, VanillaDrawLayer layer)
    {
        if (_count == 0) return;

        var list = _after[(int)layer];
        if (list is null) return;

        Run(list, ref drawInfo);
    }

    private static void Run(List<ModPlayerDrawLayer> list, ref PlayerDrawSet drawInfo)
    {
        for (var i = 0; i < list.Count; i++)
        {
            var layer = list[i];

            try
            {
                if (!layer.Visible(drawInfo)) continue;

                var transform = layer.Transform;
                var torso = (transform & LayerTransform.Torso) != 0;
                var mount = (transform & LayerTransform.Mount) != 0;

                if (torso) PlayerDrawLayers.DrawPlayer_extra_TorsoPlus(drawInfo);
                if (mount) PlayerDrawLayers.DrawPlayer_extra_MountPlus(drawInfo);

                try
                {
                    layer.Draw(ref drawInfo);
                }
                finally
                {
                    if (mount) PlayerDrawLayers.DrawPlayer_extra_MountMinus(drawInfo);
                    if (torso) PlayerDrawLayers.DrawPlayer_extra_TorsoMinus(drawInfo);
                }
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[playerdrawlayer] {layer.Name}.Draw 异常：{Safe.Describe(e)}");
            }
        }
    }
}
