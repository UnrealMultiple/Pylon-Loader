using System;
using System.Runtime.InteropServices;

namespace Pylon.ModLoader.UI
{
    /// <summary>
    /// libpylon 侧覆盖层渲染面的 P/Invoke（7 个导出，见 core/src/imgui/host/RenderHost.h）。
    /// 只做"表面 + Vulkan 取帧/呈现"；ImGui 上下文与输入全在托管侧。
    /// </summary>
    internal static class ImGuiNativeApi
    {
        private const string Lib = "pylon";

        [DllImport(Lib, EntryPoint = "PylonImgui_AttachSurface")]
        internal static extern int AttachSurface(IntPtr surface);

        [DllImport(Lib, EntryPoint = "PylonImgui_DetachSurface")]
        internal static extern void DetachSurface();

        [DllImport(Lib, EntryPoint = "PylonImgui_IsReady")]
        internal static extern int IsReady();

        [DllImport(Lib, EntryPoint = "PylonImgui_PixelSize")]
        internal static extern void PixelSize(out int width, out int height);

        [DllImport(Lib, EntryPoint = "PylonImgui_BeginFrame")]
        internal static extern int BeginFrame();

        [DllImport(Lib, EntryPoint = "PylonImgui_EndFrame")]
        internal static extern void EndFrame();

        [DllImport(Lib, EntryPoint = "PylonImgui_SetKeyboardGuardMs")]
        internal static extern void SetKeyboardGuardMs(int ms);

        internal static (float Width, float Height) DisplaySize
        {
            get
            {
                PixelSize(out var w, out var h);
                return (w, h);
            }
        }
    }
}
