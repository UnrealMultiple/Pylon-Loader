using System.Runtime.InteropServices;
using JArg = UnityEngine.jvalue;

namespace Pylon.ModLoader.Jni;

/// <summary>
/// JNI 的 jvalue 联合
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 8)]
internal struct JValue
{
    [FieldOffset(0)] public int Int;
    [FieldOffset(0)] public float Float;
    [FieldOffset(0)] public byte Byte;
    [FieldOffset(0)] public long Long;
    [FieldOffset(0)] public IntPtr Obj;

    public static JValue Of(int value) => new() { Int = value };

    public static JValue Of(float value) => new() { Float = value };

    public static JValue Of(bool value) => new() { Byte = (byte)(value ? 1 : 0) };

    public static JValue Of(IntPtr value) => new() { Obj = value };

    public static implicit operator JValue(int value) => Of(value);
    public static implicit operator JValue(float value) => Of(value);
    public static implicit operator JValue(bool value) => Of(value);
    public static implicit operator JValue(IntPtr value) => Of(value);

    internal JArg ToArg() => new() { l = (IntPtr)Long };
}
