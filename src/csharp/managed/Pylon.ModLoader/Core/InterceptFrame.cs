using System;
using System.Runtime.InteropServices;

namespace Pylon.ModLoader;

[StructLayout(LayoutKind.Sequential, Size = FrameBytes)]
public unsafe struct InterceptFrame
{
    public const int GprCount = 31;
    public const int VectorCount = 32;
    public const int FrameBytes = 0x320;

    public const int NzcvOffset = 0xF8;
    public const int FpcrOffset = 0x100;
    public const int FpsrOffset = 0x108;
    public const int VectorOffset = 0x110;
    public const int DecisionOffset = 0x310;

    public fixed ulong General[GprCount];
    public ulong Nzcv;
    public ulong Fpcr;
    public ulong Fpsr;
    public fixed byte Vectors[VectorCount * 16];
    public uint Decision;
    public uint Reserved;

    public static ulong GetGpr(IntPtr frame, int index) => *(ulong*)((byte*)frame + index * 8);

    public static void SetGpr(IntPtr frame, int index, ulong value) =>
        *(ulong*)((byte*)frame + index * 8) = value;

    public static Span<byte> GetVector(IntPtr frame, int index) =>
        new((byte*)frame + VectorOffset + index * 16, 16);

    public static double GetDouble(IntPtr frame, int index) =>
        BitConverter.Int64BitsToDouble((long)GetVectorUInt64(frame, index, 0));

    public static void SetDouble(IntPtr frame, int index, double value) =>
        SetVectorUInt64(frame, index, 0, (ulong)BitConverter.DoubleToInt64Bits(value));

    public static ulong GetVectorUInt64(IntPtr frame, int index, int lane) =>
        *(ulong*)((byte*)frame + VectorOffset + index * 16 + lane * 8);

    public static void SetVectorUInt64(IntPtr frame, int index, int lane, ulong value) =>
        *(ulong*)((byte*)frame + VectorOffset + index * 16 + lane * 8) = value;
}
