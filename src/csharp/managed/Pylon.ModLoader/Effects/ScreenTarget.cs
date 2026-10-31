namespace Pylon.ModLoader.Effects;

/// <summary>离屏目标：盖满 viewport（Scale 0.5 = 半分辨率）。临时目标按名跨帧复用，可能带旧内容。</summary>
public sealed class ScreenTarget
{
    /// <summary>帧内唯一 id（供 ScreenEffect.Dest / Source 引用）。</summary>
    public int Id { get; set; }

    /// <summary>跨帧复用的键。</summary>
    public string Name { get; set; } = null!;

    /// <summary>分辨率系数 0&lt;..&lt;=1。</summary>
    public float Scale { get; set; } = 1f;

    /// <summary>临时目标按名复用，Persistent 只描述语义。</summary>
    public bool Persistent { get; set; }
}
