namespace Pylon.ModLoader.Effects;

/// <summary>离屏目标：盖满 viewport（Scale 0.5 = 半分辨率）。临时目标按名跨帧复用，可能带旧内容。</summary>
public sealed class ScreenTarget
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public float Scale { get; set; } = 1f;

    public bool Persistent { get; set; }
}
