using System;
using System.Runtime.InteropServices;

namespace Pylon.ImGui
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate int ImGuiInputTextCallback(ImGuiInputTextCallbackData* data);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void ImGuiSizeCallback(ImGuiSizeCallbackData* data);
}
