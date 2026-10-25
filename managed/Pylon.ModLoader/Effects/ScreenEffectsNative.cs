// 层：Mod 框架 / Effects —— 屏幕效果的内核 P/Invoke 声明（目标层：ScreenEffectsApi.h）
using System.Runtime.InteropServices;
using System.Text;

namespace Pylon.ModLoader.Effects
{
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

    /// <summary>内核 PylonFx_* 导出（字符串按 UTF-8，bool 用 0/1 int）。</summary>
    internal static class ScreenEffectsNative
    {
        [DllImport("pylon", EntryPoint = "PylonFx_Install")] internal static extern int Install();
        [DllImport("pylon", EntryPoint = "PylonFx_Available")] internal static extern int Available();
        [DllImport("pylon", EntryPoint = "PylonFx_ShaderRegister")] internal static extern int ShaderRegister(string name, string source);
        [DllImport("pylon", EntryPoint = "PylonFx_ShaderRegisterMesh")] internal static extern int ShaderRegisterMesh(string name, string source);
        [DllImport("pylon", EntryPoint = "PylonFx_ShaderStatus")] internal static extern int ShaderStatus(int id, out int state, StringBuilder error, int errorCap);
        [DllImport("pylon", EntryPoint = "PylonFx_TextureLoad")] internal static extern int TextureLoad(string key, int width, int height,
            [In] byte[] rgba, int linear, int repeat, out int outWidth, out int outHeight);
        [DllImport("pylon", EntryPoint = "PylonFx_SubmitBegin")] internal static extern int SubmitBegin(int history);
        [DllImport("pylon", EntryPoint = "PylonFx_SubmitAddTarget")] internal static extern int SubmitAddTarget(int id, string name, float scale, int persistent);
        [DllImport("pylon", EntryPoint = "PylonFx_SubmitAddDraw")] internal static extern int SubmitAddDraw(int shader, int layer, int dest, int source,
            [In] float[] box, [In] float[] center, float radius, float intensity, float progress, float age, [In] float[] color);
        [DllImport("pylon", EntryPoint = "PylonFx_SubmitAddMesh")] internal static extern int SubmitAddMesh(int shader, int layer, int dest, int blend,
            int count, [In] float[] verts);
        [DllImport("pylon", EntryPoint = "PylonFx_SubmitAddUniform")] internal static extern int SubmitAddUniform(int draw, string name, int components, [In] float[] values, int count);
        [DllImport("pylon", EntryPoint = "PylonFx_SubmitAddTexture")] internal static extern int SubmitAddTexture(int draw, string name, int texture);
        [DllImport("pylon", EntryPoint = "PylonFx_SubmitEnd")] internal static extern void SubmitEnd();
        [DllImport("pylon", EntryPoint = "PylonFx_Stats")] internal static extern void Stats(out PylonFxStatsNative stats);
        [DllImport("pylon", EntryPoint = "PylonFx_RequestTrace")] internal static extern void RequestTrace();
    }
}
