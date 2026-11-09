using System.Collections;
using Terraria.DataStructures;

namespace Pylon.ModLoader;

/// <summary>
/// 玩家绘制层：每个玩家画一次。给了 <see cref="GetDefaultPosition"/> 就插在原版层之间，
/// 也可以挂成别的层的子层（BeforeParent / AfterParent）跟着父层一起画。
/// 名字、位置类型与 tModLoader 的形状一致，方便按同一套写法移植。
/// </summary>
public abstract class PlayerDrawLayer : ModType
{
    /// <summary>套在层外面的变换：画之前推入、画之后还原（可链式继承父变换）。</summary>
    public abstract class Transformation
    {
        public virtual Transformation Parent => null!;

        protected abstract void PreDraw(ref PlayerDrawSet drawInfo);

        protected abstract void PostDraw(ref PlayerDrawSet drawInfo);

        public void PreDrawRecursive(ref PlayerDrawSet drawInfo)
        {
            Parent?.PreDrawRecursive(ref drawInfo);
            PreDraw(ref drawInfo);
        }

        public void PostDrawRecursive(ref PlayerDrawSet drawInfo)
        {
            PostDraw(ref drawInfo);
            Parent?.PostDrawRecursive(ref drawInfo);
        }
    }

    /// <summary>层位。四种：两层之间 / 多槽按条件 / 父层之前 / 父层之后。</summary>
    public abstract class Position { }

    /// <summary>画在 layer1 之后、layer2 之前（任一端给 null 表示链首/链尾）。</summary>
    public sealed class Between : Position
    {
        public PlayerDrawLayer Layer1 { get; } = null!;

        public PlayerDrawLayer Layer2 { get; } = null!;

        public Between(PlayerDrawLayer layer1, PlayerDrawLayer layer2)
        {
            Layer1 = layer1;
            Layer2 = layer2;
        }

        public Between() { }
    }

    /// <summary>多槽：同一层按每帧条件落在不同的 Between 位置上。</summary>
    public class Multiple : Position, IEnumerable
    {
        public delegate bool Condition(PlayerDrawSet drawInfo);

        public IList<(Between, Condition)> Positions { get; } = new List<(Between, Condition)>();

        public void Add(Between position, Condition condition) => Positions.Add((position, condition));

        public IEnumerator GetEnumerator() => Positions.GetEnumerator();
    }

    /// <summary>画在父层之前（并进父层的子层序列）。</summary>
    public class BeforeParent : Position
    {
        public PlayerDrawLayer Parent { get; }

        public BeforeParent(PlayerDrawLayer parent) => Parent = parent;
    }

    /// <summary>画在父层之后（并进父层的子层序列）。</summary>
    public class AfterParent : Position
    {
        public PlayerDrawLayer Parent { get; }

        public AfterParent(PlayerDrawLayer parent) => Parent = parent;
    }

    private readonly List<PlayerDrawLayer> _childrenBefore = [];
    private readonly List<PlayerDrawLayer> _childrenAfter = [];

    /// <summary>这一帧画不画；由 <see cref="GetDefaultVisibility"/> 与 <see cref="Hide"/> 共同决定，每帧重置。</summary>
    public bool Visible { get; private set; } = true;

    /// <summary>套哪种变换；null = 不套。</summary>
    public virtual Transformation Transform => null!;

    public IReadOnlyList<PlayerDrawLayer> ChildrenBefore => _childrenBefore;

    public IReadOnlyList<PlayerDrawLayer> ChildrenAfter => _childrenAfter;

    /// <summary>是不是"头部层"（会被头盔/发型那套特殊处理）。</summary>
    public virtual bool IsHeadLayer => false;

    /// <summary>这一帧别画（只在 <see cref="ModPlayer.HideDrawLayers"/> 与 <see cref="GetDefaultVisibility"/> 之外用）。</summary>
    public void Hide() => Visible = false;

    internal void AddChildBefore(PlayerDrawLayer child) => _childrenBefore.Add(child);

    internal void AddChildAfter(PlayerDrawLayer child) => _childrenAfter.Add(child);

    internal void ClearChildren()
    {
        _childrenBefore.Clear();
        _childrenAfter.Clear();
    }

    /// <summary>每帧的默认可见性。</summary>
    public virtual bool GetDefaultVisibility(PlayerDrawSet drawInfo) => true;

    /// <summary>默认层位（会在每帧/每次改顺序后重新求一次）。</summary>
    public abstract Position GetDefaultPosition();

    internal void ResetVisibility(PlayerDrawSet drawInfo)
    {
        foreach (var child in _childrenBefore) child.ResetVisibility(drawInfo);

        Visible = GetDefaultVisibility(drawInfo);

        foreach (var child in _childrenAfter) child.ResetVisibility(drawInfo);
    }

    /// <summary>真正画这一层。</summary>
    protected abstract void Draw(ref PlayerDrawSet drawInfo);

    /// <summary>按"变换 → 前子层 → 自己 → 后子层 → 还原变换"的顺序画。</summary>
    public void DrawWithTransformationAndChildren(ref PlayerDrawSet drawInfo)
    {
        if (!Visible) return;

        Transform?.PreDrawRecursive(ref drawInfo);

        foreach (var child in _childrenBefore) child.DrawWithTransformationAndChildren(ref drawInfo);

        Draw(ref drawInfo);

        foreach (var child in _childrenAfter) child.DrawWithTransformationAndChildren(ref drawInfo);

        Transform?.PostDrawRecursive(ref drawInfo);
    }

    public override string ToString() => Name;
}
