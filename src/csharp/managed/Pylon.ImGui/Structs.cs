using System.Runtime.InteropServices;

namespace Pylon.ImGui;

[StructLayout(LayoutKind.Sequential)]
public struct ImVec2(float x, float y)
{
    public float X = x;
    public float Y = y;
    public static readonly ImVec2 Zero = new(0, 0);
    public override readonly string ToString() => $"({X}, {Y})";
}

[StructLayout(LayoutKind.Sequential)]
public struct ImVec4(float x, float y, float z, float w)
{
    public float X = x;
    public float Y = y;
    public float Z = z;
    public float W = w;
    public static readonly ImVec4 Zero = new(0, 0, 0, 0);
    public override readonly string ToString() => $"({X}, {Y}, {Z}, {W})";
}