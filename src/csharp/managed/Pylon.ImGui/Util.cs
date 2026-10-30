using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Pylon.ImGui
{
    internal static unsafe class Util
    {
        public const int StackAllocationSizeLimit = 2048;

        public static byte* Allocate(int byteCount) => (byte*)Marshal.AllocHGlobal(byteCount);

        public static void Free(byte* ptr) => Marshal.FreeHGlobal((IntPtr)ptr);

        public static int GetUtf8(string s, byte* utf8Bytes, int utf8ByteCount)
        {
            fixed (char* utf16Ptr = s)
            {
                return Encoding.UTF8.GetBytes(utf16Ptr, s.Length, utf8Bytes, utf8ByteCount);
            }
        }

        public static int GetUtf8(ReadOnlySpan<char> s, byte* utf8Bytes, int utf8ByteCount)
        {
            fixed (char* utf16Ptr = s)
            {
                return Encoding.UTF8.GetBytes(utf16Ptr, s.Length, utf8Bytes, utf8ByteCount);
            }
        }

        public static int CalcSizeInBytes(ReadOnlySpan<char> s)
        {
            return Encoding.UTF8.GetByteCount(s);
        }

        public static string StringFromPtr(byte* ptr)
        {
            if (ptr == null) return null;
            int byteCount = 0;
            while (ptr[byteCount] != 0) byteCount++;
            return Encoding.UTF8.GetString(ptr, byteCount);
        }
    }
}
