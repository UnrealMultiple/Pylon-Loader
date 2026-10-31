using System.Runtime.InteropServices;

namespace Pylon.ModLoader.Effects;

/// <summary>与内核 PylonFxStats 顺序对齐的封送结构。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PylonFxStatsNative
{
    public long WorldPostRender, UiPostRender, OtherPostRender;
    public long Draws, Frames, MarkersIssued, MarkerHits, ScreenFrames;
    public long RenderCpuNs, MarkerCpuNs, GpuNs, GpuSamples, GpuFrameNs, GpuFrameSamples;
    public long ShadowChecks, ShadowMismatches, BoxPixels;
    public int DrawFbo, ReadFbo;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public int[] Viewport;
    public int Samples, BlitError, CopyFallbacks;
    public int HookInstalled, HasContext, MarkerReady, GpuTimer;
}
