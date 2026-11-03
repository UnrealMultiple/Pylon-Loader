using System;
using System.Runtime.InteropServices;

namespace Pylon.NaviteAPI.ImGui;

/// <summary>
/// libpylon 侧覆盖层画布的 P/Invoke（见 core/src/imgui/host/RenderHost.h）。
/// 覆盖层画在**游戏自己的 GLES 上下文**里：没有 SurfaceView、没有 Surface、没有交换链，
/// 所以这里也没有 AttachSurface / DetachSurface。
/// </summary>
internal static partial class ImGuiNativeApi
{
    [LibraryImport("pylon", EntryPoint = "PylonImgui_IsReady")]
    internal static partial int IsReady();

    [LibraryImport("pylon", EntryPoint = "PylonImgui_AttachContext")]
    internal static partial void AttachContext(IntPtr context);

    [LibraryImport("pylon", EntryPoint = "PylonImgui_PixelSize")]
    internal static partial void PixelSize(out int width, out int height);

    [LibraryImport("pylon", EntryPoint = "PylonImgui_BeginFrame")]
    internal static partial int BeginFrame();

    [LibraryImport("pylon", EntryPoint = "PylonImgui_EndFrame")]
    internal static partial void EndFrame();

    [LibraryImport("pylon", EntryPoint = "PylonImgui_Reset")]
    internal static partial void Reset();

    internal static (float Width, float Height) DisplaySize
    {
        get
        {
            PixelSize(out var w, out var h);
            return (w, h);
        }
    }
}
