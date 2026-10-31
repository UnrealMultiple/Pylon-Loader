namespace Pylon.ModLoader.Effects;

/// <summary>屏幕效果统计快照（内核 RenderStats）。</summary>
public sealed class ScreenStats
{
    public long WorldPostRender, UiPostRender, OtherPostRender;
    public long Draws, Frames, MarkersIssued, MarkerHits, ScreenFrames;
    public long RenderCpuNs, MarkerCpuNs, GpuNs, GpuSamples, GpuFrameNs, GpuFrameSamples;
    public long ShadowChecks, ShadowMismatches, BoxPixels;
    public int DrawFbo, ReadFbo, Samples, BlitError, CopyFallbacks;
    public int[] Viewport { get; private set; } = new int[4];
    public bool HookInstalled, HasContext, MarkerReady, GpuTimer;

    internal static ScreenStats From(PylonFxStatsNative n) => new()
    {
        WorldPostRender = n.WorldPostRender,
        UiPostRender = n.UiPostRender,
        OtherPostRender = n.OtherPostRender,
        Draws = n.Draws,
        Frames = n.Frames,
        MarkersIssued = n.MarkersIssued,
        MarkerHits = n.MarkerHits,
        ScreenFrames = n.ScreenFrames,
        RenderCpuNs = n.RenderCpuNs,
        MarkerCpuNs = n.MarkerCpuNs,
        GpuNs = n.GpuNs,
        GpuSamples = n.GpuSamples,
        GpuFrameNs = n.GpuFrameNs,
        GpuFrameSamples = n.GpuFrameSamples,
        ShadowChecks = n.ShadowChecks,
        ShadowMismatches = n.ShadowMismatches,
        BoxPixels = n.BoxPixels,
        DrawFbo = n.DrawFbo,
        ReadFbo = n.ReadFbo,
        Samples = n.Samples,
        BlitError = n.BlitError,
        CopyFallbacks = n.CopyFallbacks,
        HookInstalled = n.HookInstalled != 0,
        HasContext = n.HasContext != 0,
        MarkerReady = n.MarkerReady != 0,
        GpuTimer = n.GpuTimer != 0,
    };
}
