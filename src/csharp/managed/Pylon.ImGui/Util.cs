using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Pylon.ImGui;

internal static unsafe class Util
{
    /// <summary>string → UTF-8 零结尾缓冲（必须配 <see cref="Free"/>）；null → null。</summary>
    public static byte* AllocUtf8(string text)
    {
        if (text == null) return null;
        var bytes = Encoding.UTF8.GetBytes(text);
        var ptr = Marshal.AllocCoTaskMem(bytes.Length + 1);
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        Marshal.WriteByte(ptr, bytes.Length, 0);
        return (byte*)ptr;
    }

    /// <summary>读回原生 UTF-8 缓冲 → string（缓冲区可能被原生改写，如 InputText）。</summary>
    public static string ReadUtf8(byte* ptr)
    {
        if (ptr == null) return string.Empty;
        var len = 0;
        while (ptr[len] != 0) len++;
        var bytes = new byte[len];
        Marshal.Copy((IntPtr)ptr, bytes, 0, len);
        return Encoding.UTF8.GetString(bytes);
    }

    public static void Free(byte* ptr)
    {
        if (ptr != null) Marshal.FreeCoTaskMem((IntPtr)ptr);
    }

    /// <summary>按下标取 UTF-8 指针表：items 各分配一份（调用方负责 FreeAll）。</summary>
    public static byte** AllocItems(string[] items, out IntPtr[] allocs)
    {
        if (items == null || items.Length == 0) { allocs = Array.Empty<IntPtr>(); return null; }
        allocs = new IntPtr[items.Length];
        var ptrs = (byte**)System.Runtime.InteropServices.Marshal.AllocCoTaskMem(items.Length * IntPtr.Size);
        for (int i = 0; i < items.Length; i++)
        {
            allocs[i] = (IntPtr)AllocUtf8(items[i]);
            ptrs[i] = (byte*)allocs[i];
        }
        return ptrs;
    }

    public static void FreeAll(byte** ptrs, IntPtr[] allocs)
    {
        foreach (var a in allocs) if (a != IntPtr.Zero) Marshal.FreeCoTaskMem(a);
        if (ptrs != null) Marshal.FreeCoTaskMem((IntPtr)ptrs);
    }
}