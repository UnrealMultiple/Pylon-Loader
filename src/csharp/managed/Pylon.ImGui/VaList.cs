using System.Runtime.InteropServices;

namespace Pylon.ImGui
{
    // Variadic C va_list is not supported through P/Invoke. Text-formatting methods
    // that take va_list are excluded from the generated surface; this stub exists
    // only to satisfy any remaining layout-only references.
    public unsafe struct va_list
    {
    }
}
