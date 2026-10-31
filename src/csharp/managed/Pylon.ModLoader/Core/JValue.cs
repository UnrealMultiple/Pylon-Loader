using System.Runtime.InteropServices;

namespace Pylon.ModLoader;

/// <summary>JNI 的 jvalue 联合（8 字节），布局与 C++ 桥的 argv 数组一致。</summary>
[StructLayout(LayoutKind.Explicit, Size = 8)]
internal struct JValue
{
    [FieldOffset(0)] public int Int;
    [FieldOffset(0)] public float Float;
    [FieldOffset(0)] public byte Byte; // jboolean
    [FieldOffset(0)] public long Long;
    [FieldOffset(0)] public IntPtr Obj; // jobject 全局引用

    public static JValue Of(int value) => new() { Int = value };

    public static JValue Of(float value) => new() { Float = value };

    public static JValue Of(bool value) => new() { Byte = (byte)(value ? 1 : 0) };

    public static JValue Of(IntPtr value) => new() { Obj = value };

    // 调用点直接传字面量。
    public static implicit operator JValue(int value) => Of(value);
    public static implicit operator JValue(float value) => Of(value);
    public static implicit operator JValue(bool value) => Of(value);
    public static implicit operator JValue(IntPtr value) => Of(value);
}
