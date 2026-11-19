using Pylon.NaviteAPI.Effects;

namespace Pylon.ModLoader.Effects;

/// <summary>屏幕效果统计快照（内核 PipelineStats 的作者面）。</summary>
public readonly struct ScreenStats
{
    public long WorldFrames { get; init; }

    public long InterfaceFrames { get; init; }

    /// <summary>一帧里执行的绘制步数。</summary>
    public long Passes { get; init; }

    public long Frames { get; init; }

    public long MarkersIssued { get; init; }

    public long MarkerHits { get; init; }

    public long ScreenFrames { get; init; }

    public long RenderCpuNs { get; init; }

    public long MarkerCpuNs { get; init; }

    public long GpuNs { get; init; }

    public long GpuSamples { get; init; }

    public long BoxPixels { get; init; }

    public long CopyFallbacks { get; init; }

    public bool HookInstalled { get; init; }

    public bool HasContext { get; init; }

    public bool MarkerReady { get; init; }

    public double GpuAverageMs => GpuSamples > 0 ? GpuNs / (double)GpuSamples / 1e6 : 0;

    internal static ScreenStats Read()
    {
        ScreenEffectsNative.Stats(out var n);
        return new ScreenStats
        {
            WorldFrames = n.WorldFrames,
            InterfaceFrames = n.UiPostRender,
            Passes = n.Draws,
            Frames = n.Frames,
            MarkersIssued = n.MarkersIssued,
            MarkerHits = n.MarkerHits,
            ScreenFrames = n.ScreenFrames,
            RenderCpuNs = n.CpuNs,
            GpuNs = n.GpuNs,
            GpuSamples = n.GpuSamples,
            BoxPixels = n.BoxPixels,
            CopyFallbacks = n.ScreenCopies,
            HookInstalled = n.Installed != 0,
            HasContext = n.Installed != 0,
            MarkerReady = n.WorldLayer != 0,
        };
    }

    public override string ToString() =>
        $"world {WorldFrames}, passes {Passes} in {Frames} frames, marker {(MarkerReady ? "ready" : "not ready")} ({MarkerHits}/{MarkersIssued}), gpu {GpuAverageMs:0.00} ms";
}