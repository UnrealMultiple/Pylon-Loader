using System;
using System.Text;

namespace Pylon.ImGui
{
    public unsafe struct NullTerminatedString : IEquatable<NullTerminatedString>
    {
        public byte* Data;

        public NullTerminatedString(byte* data) => Data = data;

        public override int GetHashCode()
        {
            var str = ToString();
            return str == null ? 0 : str.GetHashCode();
        }

        public override bool Equals(object obj) => obj is NullTerminatedString rhs && Equals(rhs);

        public bool Equals(NullTerminatedString other)
        {
            if (Data == other.Data) return true;
            if (Data == null || other.Data == null) return false;
            byte* a = Data, b = other.Data;
            while (*a != 0 && *b != 0)
            {
                if (*a != *b) return false;
                a++; b++;
            }
            return *a == *b;
        }

        public static bool operator ==(NullTerminatedString lhs, NullTerminatedString rhs) => lhs.Equals(rhs);
        public static bool operator !=(NullTerminatedString lhs, NullTerminatedString rhs) => !lhs.Equals(rhs);
        public static implicit operator NullTerminatedString(byte* ptr) => new NullTerminatedString(ptr);
        public static implicit operator byte*(NullTerminatedString str) => str.Data;

        public override string ToString()
        {
            if (Data == null) return null;
            int len = 0;
            while (Data[len] != 0) len++;
            return Encoding.UTF8.GetString(Data, len);
        }
    }
}
