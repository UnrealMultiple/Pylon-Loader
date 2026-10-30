using System;

namespace Pylon.ImGui
{
    public unsafe partial struct ImFontLoader
    {
    }

    public unsafe partial struct ImFontLoaderPtr
    {
        public ImFontLoader* NativePtr { get; }
        public ImFontLoaderPtr(ImFontLoader* nativePtr) => NativePtr = nativePtr;
        public ImFontLoaderPtr(IntPtr nativePtr) => NativePtr = (ImFontLoader*)nativePtr;
        public static implicit operator ImFontLoaderPtr(ImFontLoader* nativePtr) => new ImFontLoaderPtr(nativePtr);
        public static implicit operator ImFontLoader*(ImFontLoaderPtr wrappedPtr) => wrappedPtr.NativePtr;
        public static implicit operator ImFontLoaderPtr(IntPtr nativePtr) => new ImFontLoaderPtr(nativePtr);
    }

    public unsafe partial struct ImFontAtlasBuilder
    {
    }

    public unsafe partial struct ImFontAtlasBuilderPtr
    {
        public ImFontAtlasBuilder* NativePtr { get; }
        public ImFontAtlasBuilderPtr(ImFontAtlasBuilder* nativePtr) => NativePtr = nativePtr;
        public ImFontAtlasBuilderPtr(IntPtr nativePtr) => NativePtr = (ImFontAtlasBuilder*)nativePtr;
        public static implicit operator ImFontAtlasBuilderPtr(ImFontAtlasBuilder* nativePtr) => new ImFontAtlasBuilderPtr(nativePtr);
        public static implicit operator ImFontAtlasBuilder*(ImFontAtlasBuilderPtr wrappedPtr) => wrappedPtr.NativePtr;
        public static implicit operator ImFontAtlasBuilderPtr(IntPtr nativePtr) => new ImFontAtlasBuilderPtr(nativePtr);
    }
}
