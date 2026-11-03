using System.Text;

namespace Pylon.NaviteAPI.ImGui;

public static unsafe class RangeAccessorExtensions
{
    public static string GetStringASCII(this RangeAccessor<byte> stringAccessor)
    {
        return Encoding.ASCII.GetString((byte*)stringAccessor.Data, stringAccessor.Count);
    }
}
