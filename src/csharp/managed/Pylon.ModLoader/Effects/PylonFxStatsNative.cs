using System.Runtime.InteropServices;

namespace Pylon.ModLoader.Effects;

/// <summary>与内核 PylonFxStats 顺序对齐的封送结构。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PylonFxStatsNative
{
    public long Frames, Draws, BoxPixels, CpuNs;
    public long GpuNs, GpuSamples;
    public long WorldFrames, ScreenFrames, MarkersIssued, MarkerHits;
    public long WorldPostRender, UiPostRender, OtherPostRender;
    public int DrawFbo;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public int[] Viewport;
    public int Samples;
    public int Installed, WorldLayer, Overlay, GpuTimer;
}
