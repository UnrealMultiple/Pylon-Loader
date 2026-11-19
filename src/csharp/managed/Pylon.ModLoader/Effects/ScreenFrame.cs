namespace Pylon.ModLoader.Effects;

/// <summary>一帧的效果：按顺序的步骤 + 用到的目标 + 是否要 History（上一帧世界）。</summary>
public sealed class ScreenFrame
{
    private static int _nextTargetId = 1;

    private readonly List<ScreenEffect> _draws = [];
    private readonly List<ScreenGraphics.Target> _targets = [];
    private readonly Dictionary<string, ScreenGraphics.Target> _byKey = new(StringComparer.Ordinal);

    public bool History { get; set; }

    public IReadOnlyList<ScreenEffect> Draws => _draws;

    public IReadOnlyList<ScreenGraphics.Target> Targets => _targets;

    public ScreenFrame Add(ScreenEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        _draws.Add(effect);
        return this;
    }

    /// <summary>插到指定位置（扭曲 pass 要排在产生它的效果之前）。</summary>
    public ScreenFrame AddAt(int index, ScreenEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        _draws.Insert(Math.Clamp(index, 0, _draws.Count), effect);
        return this;
    }

    /// <summary>
    /// 申请一个离屏目标。同帧同名同 scale 复用；id 全局递增，多个 Mod 的帧合并后也不会冲突。
    /// persistent 的目标按名跨帧保留内容——名字要自带命名空间（<see cref="ScreenCommands"/> 会加类型名前缀）。
    /// </summary>
    public ScreenGraphics.Target Target(string name, float scale = 1f, bool persistent = false)
    {
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("目标名不能为空", nameof(name));
        if (!(scale > 0f && scale <= 1f)) throw new ArgumentException($"目标 Scale 必须在 (0,1]（{scale}）", nameof(scale));

        var key = name + "@" + scale.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (_byKey.TryGetValue(key, out var target)) return target;

        target = new ScreenGraphics.Target(Interlocked.Increment(ref _nextTargetId), name, scale, persistent);
        _byKey[key] = target;
        _targets.Add(target);
        return target;
    }

    public void Submit() => ScreenGraphics.Submit(this);
}