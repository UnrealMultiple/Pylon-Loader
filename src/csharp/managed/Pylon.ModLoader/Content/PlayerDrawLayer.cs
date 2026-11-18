using System.Collections;
using Terraria.DataStructures;

namespace Pylon.ModLoader.Content;
/// <summary>
/// 玩家绘制层：每个玩家画一次。给了 <see cref="GetDefaultPosition"/> 就插在原版层之间，
/// 也可以挂成别的层的子层（BeforeParent / AfterParent）跟着父层一起画。
/// 名字、位置类型与 tModLoader 的形状一致，方便按同一套写法移植。
/// </summary>
public abstract class PlayerDrawLayer : ModType
{
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

    public abstract class Position { }

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

    public class Multiple : Position, IEnumerable
    {
        public delegate bool Condition(PlayerDrawSet drawInfo);

        public IList<(Between, Condition)> Positions { get; } = new List<(Between, Condition)>();

        public void Add(Between position, Condition condition) => Positions.Add((position, condition));

        public IEnumerator GetEnumerator() => Positions.GetEnumerator();
    }

    public class BeforeParent : Position
    {
        public PlayerDrawLayer Parent { get; }

        public BeforeParent(PlayerDrawLayer parent) => Parent = parent;
    }

    public class AfterParent : Position
    {
        public PlayerDrawLayer Parent { get; }

        public AfterParent(PlayerDrawLayer parent) => Parent = parent;
    }

    private readonly List<PlayerDrawLayer> _childrenBefore = [];
    private readonly List<PlayerDrawLayer> _childrenAfter = [];
    public bool Visible { get; private set; } = true;

    public virtual Transformation Transform => null!;

    public IReadOnlyList<PlayerDrawLayer> ChildrenBefore => _childrenBefore;

    public IReadOnlyList<PlayerDrawLayer> ChildrenAfter => _childrenAfter;

    public virtual bool IsHeadLayer => false;

    public void Hide() => Visible = false;

    internal void AddChildBefore(PlayerDrawLayer child) => _childrenBefore.Add(child);

    internal void AddChildAfter(PlayerDrawLayer child) => _childrenAfter.Add(child);

    internal void ClearChildren()
    {
        _childrenBefore.Clear();
        _childrenAfter.Clear();
    }

    public virtual bool GetDefaultVisibility(PlayerDrawSet drawInfo) => true;

    public abstract Position GetDefaultPosition();

    internal void ResetVisibility(PlayerDrawSet drawInfo)
    {
        foreach (var child in _childrenBefore) child.ResetVisibility(drawInfo);

        Visible = GetDefaultVisibility(drawInfo);

        foreach (var child in _childrenAfter) child.ResetVisibility(drawInfo);
    }

    protected abstract void Draw(ref PlayerDrawSet drawInfo);

    public void DrawWithTransformationAndChildren(ref PlayerDrawSet drawInfo)
    {
        if (!Visible) return;

        Transform?.PreDrawRecursive(ref drawInfo);
        try
        {
            foreach (var child in _childrenBefore) child.DrawWithTransformationAndChildren(ref drawInfo);

            Draw(ref drawInfo);

            foreach (var child in _childrenAfter) child.DrawWithTransformationAndChildren(ref drawInfo);
        }
        finally
        {
            Transform?.PostDrawRecursive(ref drawInfo);
        }
    }

    public override string ToString() => Name;
}
