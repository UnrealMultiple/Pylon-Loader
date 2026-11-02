using System.Runtime.InteropServices;

namespace Pylon.ModLoader.UI;

/// <summary>
/// libpylon 侧覆盖层画布的 P/Invoke（见 core/src/imgui/host/RenderHost.h）。
/// 覆盖层画在**游戏自己的 GLES 上下文**里：没有 SurfaceView、没有 Surface、没有交换链，
/// 所以这里也没有 AttachSurface / DetachSurface。
/// </summary>
internal static class ImGuiNativeApi
{
    private const string Lib = "pylon";

    [DllImport(Lib, EntryPoint = "PylonImgui_IsReady")]
    internal static extern int IsReady();

    [DllImport(Lib, EntryPoint = "PylonImgui_AttachContext")]
    internal static extern void AttachContext(IntPtr context);

    [DllImport(Lib, EntryPoint = "PylonImgui_PixelSize")]
    internal static extern void PixelSize(out int width, out int height);

    [DllImport(Lib, EntryPoint = "PylonImgui_BeginFrame")]
    internal static extern int BeginFrame();

    [DllImport(Lib, EntryPoint = "PylonImgui_EndFrame")]
    internal static extern void EndFrame();

    [DllImport(Lib, EntryPoint = "PylonImgui_Reset")]
    internal static extern void Reset();

    internal static (float Width, float Height) DisplaySize
    {
        get
        {
            PixelSize(out var w, out var h);
            return (w, h);
        }
    }
}
