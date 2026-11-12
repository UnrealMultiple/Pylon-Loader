using Pylon.NaviteAPI.Effects;


namespace Pylon.ModLoader.Effects;

/// <summary>屏幕效果统计快照（内核 PipelineStats）。</summary>
public sealed class ScreenStats
{
    public long Frames, Draws, BoxPixels, CpuNs;
    public long GpuNs, GpuSamples;
    public long MeshDraws, FullscreenDraws, MeshVertices;
    public long UniformBytes, BufferDataBytes, ScreenCopies, ScreenCopyPixels;
    public long BufferSubDataBytes, BufferStorageBytes, BufferDataCalls, BufferSubDataCalls;
    public long WorldFrames, ScreenFrames, MarkersIssued, MarkerHits;
    public long WorldPostRender, UiPostRender, OtherPostRender;
    public int DrawFbo, Samples;
    public int[] Viewport { get; private set; } = new int[4];
    public bool Installed, WorldLayer, Overlay, GpuTimer;

    internal static unsafe ScreenStats From(PylonFxStatsNative n) => new()
    {
        Frames = n.Frames,
        Draws = n.Draws,
        BoxPixels = n.BoxPixels,
        CpuNs = n.CpuNs,
        GpuNs = n.GpuNs,
        GpuSamples = n.GpuSamples,
        MeshDraws = n.MeshDraws,
        FullscreenDraws = n.FullscreenDraws,
        MeshVertices = n.MeshVertices,
        UniformBytes = n.UniformBytes,
        BufferDataBytes = n.BufferDataBytes,
        ScreenCopies = n.ScreenCopies,
        ScreenCopyPixels = n.ScreenCopyPixels,
        WorldFrames = n.WorldFrames,
        ScreenFrames = n.ScreenFrames,
        MarkersIssued = n.MarkersIssued,
        MarkerHits = n.MarkerHits,
        WorldPostRender = n.WorldPostRender,
        UiPostRender = n.UiPostRender,
        OtherPostRender = n.OtherPostRender,
        DrawFbo = n.DrawFbo,
        Samples = n.Samples,
        Viewport = [n.Viewport[0], n.Viewport[1], n.Viewport[2], n.Viewport[3]],
        Installed = n.Installed != 0,
        WorldLayer = n.WorldLayer != 0,
        Overlay = n.Overlay != 0,
        GpuTimer = n.GpuTimer != 0,
    };
}
