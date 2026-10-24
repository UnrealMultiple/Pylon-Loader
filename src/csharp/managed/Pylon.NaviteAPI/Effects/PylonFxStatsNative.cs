namespace Pylon.NaviteAPI.Effects;

/// <summary>与内核 PylonFxStats 顺序对齐的直传结构（运行时封送已关，字段必须全 blittable）。</summary>
internal unsafe struct PylonFxStatsNative
{
    public long Frames, Draws, BoxPixels, CpuNs;
    public long GpuNs, GpuSamples;
    public long MeshDraws, FullscreenDraws, MeshVertices;
    public long UniformBytes, BufferDataBytes, ScreenCopies, ScreenCopyPixels;
    public long WorldFrames, ScreenFrames, MarkersIssued, MarkerHits;
    public long WorldPostRender, UiPostRender, OtherPostRender;
    public int DrawFbo;
    public fixed int Viewport[4];
    public int Samples;
    public int Installed, WorldLayer, Overlay, GpuTimer;
}
