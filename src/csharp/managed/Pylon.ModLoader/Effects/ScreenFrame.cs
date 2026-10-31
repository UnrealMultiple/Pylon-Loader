namespace Pylon.ModLoader.Effects;

/// <summary>一帧的效果：按顺序的步骤 + 用到的目标 + 是否要 History（上一帧世界）。</summary>
public sealed class ScreenFrame
{
    private readonly List<ScreenEffect> _draws = [];
    private readonly List<ScreenTarget> _targets = [];

    /// <summary>这帧要读上一帧世界（ScreenTexture.History）时开。</summary>
    public bool History { get; set; }

    /// <summary>按顺序执行的步骤（同一帧里后画的盖先画的）。</summary>
    public IReadOnlyList<ScreenEffect> Draws => _draws;

    /// <summary>这帧的离屏目标。</summary>
    public IReadOnlyList<ScreenTarget> Targets => _targets;

    public ScreenFrame Add(ScreenEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        _draws.Add(effect);
        return this;
    }

    public ScreenFrame AddTarget(ScreenTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Id <= 0) throw new ArgumentException("目标 id 必须 > 0", nameof(target));
        if (string.IsNullOrEmpty(target.Name)) throw new ArgumentException("目标名不能为空", nameof(target));
        if (target.Scale <= 0f || target.Scale > 1f) throw new ArgumentException("目标 Scale 必须在 (0,1]", nameof(target));
        _targets.Add(target);
        return this;
    }

    /// <summary>把这一帧交给渲染 hooks（游戏线程）。没步骤的帧会被忽略。</summary>
    public void Submit() => ScreenGraphics.Submit(this);
}
