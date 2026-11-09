using Terraria.DataStructures;
using static Pylon.ModLoader.PlayerDrawLayer;

namespace Pylon.ModLoader;

/// <summary>
/// 玩家绘制层排程：把原版层当锚点、作者的层按 <see cref="PlayerDrawLayer.GetDefaultPosition"/> 插进去，
/// 最后落成"每个原版层之前/之后各一串"的槽位。原版层的调用顺序一个字节都不改，
/// 只是把作者层夹在两次原版调用之间。
/// </summary>
public static class PlayerDrawLayerLoader
{
    private static readonly List<PlayerDrawLayer> _layers = [.. PlayerDrawLayers.VanillaLayers];

    private static List<PlayerDrawLayer>?[] _before = [];
    private static List<PlayerDrawLayer>?[] _after = [];

    private static PlayerDrawLayer[] _order = [];

    private static int _anchorCount;

    /// <summary>作者层数量（0 = 全程不介入）。</summary>
    public static int ModCount => _layers.Count - PlayerDrawLayers.VanillaLayers.Count;

    /// <summary>排好的绘制顺序（原版层 + 作者层，含父子层）。</summary>
    public static IReadOnlyList<PlayerDrawLayer> DrawOrder => _order;

    /// <summary>参与排程的全部层（含锚点）。</summary>
    public static IReadOnlyList<PlayerDrawLayer> Layers => _layers;

    /// <summary>把某个 Mod 的层加进来（注册期调用）。</summary>
    internal static void Add(PlayerDrawLayer layer) => _layers.Add(layer);

    /// <summary>清掉作者层，只留锚点（换 Mod 集合时用）。</summary>
    internal static void ResetModLayers()
    {
        _layers.Clear();
        _layers.AddRange(PlayerDrawLayers.VanillaLayers);
        _order = [];
    }

    // ===================== 排程 =====================

    internal static void Rebuild()
    {
        _anchorCount = PlayerDrawLayers.VanillaLayers.Count;
        _before = new List<PlayerDrawLayer>?[_anchorCount];
        _after = new List<PlayerDrawLayer>?[_anchorCount];

        if (_layers.Count == _anchorCount)
        {
            _order = [];
            return;
        }

        foreach (var layer in _layers) layer.ClearChildren();

        // 1. 收默认层位；作者可以在 ModifyDrawLayerOrdering 里改。
        var positions = new Dictionary<PlayerDrawLayer, PlayerDrawLayer.Position>(_layers.Count);
        foreach (var layer in _layers)
        {
            PlayerDrawLayer.Position? position;
            try
            {
                position = layer.GetDefaultPosition();
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[playerdrawlayer] {layer.Name}.GetDefaultPosition 异常：{Safe.Describe(e)}");
                position = null;
            }

            if (position is not null) positions[layer] = position;
        }

        try
        {
            PlayerPipeline.Instance.ModifyDrawLayerOrdering(positions);
        }
        catch (Exception e)
        {
            ContentErrors.Report("[playerdrawlayer] ModifyDrawLayerOrdering 异常：" + Safe.Describe(e));
        }

        // 2. 父子层与多槽先摊平成 Between。
        var betweens = new Dictionary<PlayerDrawLayer, PlayerDrawLayer.Between>();
        var extra = new List<PlayerDrawLayer>();

        foreach (var pair in positions)
        {
            switch (pair.Value)
            {
                case PlayerDrawLayer.Between between:
                    betweens[pair.Key] = between;
                    break;

                case PlayerDrawLayer.BeforeParent before:
                    Place(betweens, pair.Key, parent: before.Parent, asChild: true, beforeParent: true, extra);
                    break;

                case PlayerDrawLayer.AfterParent after:
                    Place(betweens, pair.Key, parent: after.Parent, asChild: true, beforeParent: false, extra);
                    break;

                case PlayerDrawLayer.Multiple multiple:
                {
                    var slot = 0;
                    foreach (var entry in multiple.Positions)
                    {
                        var wrapper = new PlayerDrawLayerSlot(pair.Key, entry.Item2, slot++);
                        wrapper.AddChildAfter(pair.Key);
                        extra.Add(wrapper);
                        betweens[wrapper] = entry.Item1;
                    }

                    break;
                }
            }
        }

        foreach (var node in extra) _layers.Add(node);

        // 3. 按 Between 的先后做拓扑排序。
        _order = Sort([.. betweens.Keys], betweens);

        // 4. 每个作者层落在"下一个原版层之前"的槽位；没有下一个就落在最后一个原版层之后。
        var anchors = PlayerDrawLayers.VanillaLayers;
        var anchorIndex = new Dictionary<PlayerDrawLayer, int>(anchors.Count);
        for (var i = 0; i < anchors.Count; i++) anchorIndex[anchors[i]] = i;

        foreach (var layer in _order)
        {
            if (anchorIndex.ContainsKey(layer)) continue;

            var next = -1;
            for (var i = 0; i < _order.Length; i++)
            {
                if (!ReferenceEquals(_order[i], layer)) continue;

                for (var j = i + 1; j < _order.Length; j++)
                {
                    if (anchorIndex.TryGetValue(_order[j], out var index))
                    {
                        next = index;
                        break;
                    }
                }

                break;
            }

            if (next >= 0) (_before[next] ??= []).Add(layer);
            else (_after[_anchorCount - 1] ??= []).Add(layer);
        }
    }

    private static void Place(Dictionary<PlayerDrawLayer, PlayerDrawLayer.Between> betweens, PlayerDrawLayer layer,
                              PlayerDrawLayer parent, bool asChild, bool beforeParent, List<PlayerDrawLayer> extra)
    {
        _ = asChild;
        var anchors = PlayerDrawLayers.VanillaLayers;
        var index = -1;
        for (var i = 0; i < anchors.Count; i++)
        {
            if (!ReferenceEquals(anchors[i], parent)) continue;
            index = i;
            break;
        }

        if (index >= 0)
        {
            // 父层是原版层：子层落进它的前/后槽位；同时登记成子层，让 ChildrenBefore/After 与 tModLoader 语义一致
            //（原版层由游戏自己画，登记只用于查询，不会重复绘制）。
            if (beforeParent) parent.AddChildBefore(layer);
            else parent.AddChildAfter(layer);

            var prev = index > 0 ? anchors[index - 1] : null!;
            var next = index + 1 < anchors.Count ? anchors[index + 1] : null!;
            betweens[layer] = beforeParent ? new PlayerDrawLayer.Between(prev, parent) : new PlayerDrawLayer.Between(parent, next);
            return;
        }

        // 父层是作者层：真的挂成子层，跟父层一起画。
        if (beforeParent) parent.AddChildBefore(layer);
        else parent.AddChildAfter(layer);
        extra.Add(layer);
    }

    private static PlayerDrawLayer[] Sort(List<PlayerDrawLayer> nodes,
                                          Dictionary<PlayerDrawLayer, PlayerDrawLayer.Between> betweens)
    {
        var after = nodes.ToDictionary(n => n, _ => new HashSet<PlayerDrawLayer>());

        foreach (var node in nodes)
        {
            if (!betweens.TryGetValue(node, out var between)) continue;
            if (between.Layer1 is { } first && after.ContainsKey(first)) after[node].Add(first);
            if (between.Layer2 is { } second && after.ContainsKey(second)) after[second].Add(node);
        }

        var result = new List<PlayerDrawLayer>(nodes.Count);
        var placed = new HashSet<PlayerDrawLayer>();
        var left = new List<PlayerDrawLayer>(nodes);

        while (left.Count > 0)
        {
            var index = left.FindIndex(n => after[n].All(placed.Contains));
            if (index < 0)
            {
                ContentErrors.Report("[playerdrawlayer] 层位排序成环，剩下按注册序排");
                result.AddRange(left);
                break;
            }

            placed.Add(left[index]);
            result.Add(left[index]);
            left.RemoveAt(index);
        }

        return [.. result];
    }

    // ===================== 每帧 =====================

    /// <summary>第一个原版层之前：重置可见性并问一次作者要不要藏层。</summary>
    internal static void BeginFrame(PlayerDrawSet drawInfo)
    {
        for (var i = 0; i < _order.Length; i++) _order[i].ResetVisibility(drawInfo);

        PlayerPipeline.Instance.HideDrawLayers(drawInfo);
    }

    internal static PlayerDrawLayer? AnchorAt(int index) =>
        (uint)index < (uint)PlayerDrawLayers.VanillaLayers.Count ? PlayerDrawLayers.VanillaLayers[index] : null;

    internal static void Before(PlayerDrawSet drawInfo, int anchor)
    {
        if (_order.Length == 0) return;
        if (anchor == 0) BeginFrame(drawInfo);

        var list = _before[anchor];
        if (list is null) return;

        for (var i = 0; i < list.Count; i++) Run(list[i], ref drawInfo);
    }

    internal static void After(PlayerDrawSet drawInfo, int anchor)
    {
        if (_order.Length == 0) return;

        var list = _after[anchor];
        if (list is null) return;

        for (var i = 0; i < list.Count; i++) Run(list[i], ref drawInfo);
    }

    /// <summary>这个原版层这一帧要不要跳过（作者在 HideDrawLayers 里 Hide 过）。</summary>
    internal static bool IsHidden(int anchor) => AnchorAt(anchor) is { Visible: false };

    private static void Run(PlayerDrawLayer layer, ref PlayerDrawSet drawInfo)
    {
        if (!layer.Visible) return;

        try
        {
            layer.DrawWithTransformationAndChildren(ref drawInfo);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[playerdrawlayer] {layer.Name}.Draw 异常：{Safe.Describe(e)}");
        }
    }
}

/// <summary>多槽层的一个槽：本身不画，只按条件决定要不要画它挂的那个层。</summary>
internal sealed class PlayerDrawLayerSlot : PlayerDrawLayer
{
    private readonly PlayerDrawLayer _layer;
    private readonly Multiple.Condition _condition;
    private readonly string _name;

    internal PlayerDrawLayerSlot(PlayerDrawLayer layer, Multiple.Condition condition, int slot)
    {
        _layer = layer;
        _condition = condition;
        _name = $"{layer.Name}_slot{slot}";
    }

    public override string Name => _name;

    public override bool GetDefaultVisibility(PlayerDrawSet drawInfo) => _condition(drawInfo);

    public override Position GetDefaultPosition() => new Between();

    protected override void Draw(ref PlayerDrawSet drawInfo) { }
}
