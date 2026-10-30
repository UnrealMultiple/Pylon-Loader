using System;
using System.Runtime.CompilerServices;

namespace Pylon.ImGui
{
    public unsafe partial struct ImVector
    {
        public int Size;
        public int Capacity;
        public void* Data;

        public ImVector(ImVector* nativePtr) { Size = nativePtr->Size; Capacity = nativePtr->Capacity; Data = nativePtr->Data; }

        public static implicit operator ImVector(ImVector* nativePtr) => new ImVector(nativePtr);
        public static implicit operator ImVector*(ImVector wrappedPtr) => (ImVector*)Unsafe.AsPointer(ref wrappedPtr);
    }

    public unsafe partial struct ImVector<T> where T : unmanaged
    {
        public ImVector* NativePtr { get; }

        public ImVector(ImVector* nativePtr) => NativePtr = nativePtr;
        public ImVector(ImVector wrappedPtr) => NativePtr = (ImVector*)Unsafe.AsPointer(ref wrappedPtr);

        public static implicit operator ImVector<T>(ImVector* nativePtr) => new ImVector<T>(nativePtr);
        public static implicit operator ImVector*(ImVector<T> wrappedPtr) => wrappedPtr.NativePtr;

        public int Size => NativePtr->Size;
        public int Capacity => NativePtr->Capacity;

        public T this[int index]
        {
            get => Unsafe.Read<T>((T*)NativePtr->Data + index);
            set => Unsafe.Write((T*)NativePtr->Data + index, value);
        }
    }

    public unsafe partial struct ImPtrVector<T> where T : unmanaged
    {
        public ImVector* NativePtr { get; }

        public ImPtrVector(ImVector* nativePtr, int byteSize) => NativePtr = nativePtr;
        public ImPtrVector(ImVector wrappedPtr, int byteSize) => NativePtr = (ImVector*)Unsafe.AsPointer(ref wrappedPtr);

        public static implicit operator ImPtrVector<T>(ImVector* nativePtr) => new ImPtrVector<T>(nativePtr, Unsafe.SizeOf<T>());
        public static implicit operator ImPtrVector<T>(ImVector wrappedPtr) => new ImPtrVector<T>(wrappedPtr, Unsafe.SizeOf<T>());
        public static implicit operator ImVector*(ImPtrVector<T> wrappedPtr) => wrappedPtr.NativePtr;

        public int Size => NativePtr->Size;
        public int Capacity => NativePtr->Capacity;

        public T this[int index]
        {
            get => Unsafe.Read<T>((T*)NativePtr->Data + index);
            set => Unsafe.Write((T*)NativePtr->Data + index, value);
        }
    }
}
