using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pylon.NaviteAPI.ImGui;

/// <summary>
/// 纹理上传闸门。名字池和像素上传都在 native 侧（overlay/Textures），这里只把
/// 托管侧的 RGBA 字节递过去；图片绘制直接用生成的 ImGui.Image(ulong ...)。
/// </summary>
public static unsafe partial class ImGui
{
    [LibraryImport("pylon")] [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial ulong igCreateTexture(byte* rgba, int width, int height);

    [LibraryImport("pylon")] [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void igDestroyTexture(ulong id);

    [LibraryImport("pylon")] [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr PylonDecodeImage(byte* data, int length, out int width, out int height);

    [LibraryImport("pylon")] [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void PylonFreeImage(IntPtr pixels);

    /// <summary>
    /// 用内核的 stb_image 解码 PNG/JPG 字节为 RGBA（首行为图像顶部）。失败返回 null。
    /// 不走 UnityEngine：IL2CPP 没有为 Texture2D 的泛型读回生成 AOT 代码（调用即抛
    /// ExecutionEngineException），非泛型读回接口在代理里也没有暴露。
    /// </summary>
    public static byte[] DecodeImage(byte[] file, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (file == null || file.Length == 0) return null;
        fixed (byte* source = file)
        {
            var pixels = PylonDecodeImage(source, file.Length, out width, out height);
            if (pixels == IntPtr.Zero || width <= 0 || height <= 0) return null;
            try
            {
                var rgba = new byte[(int)((long)width * height * 4)];
                Marshal.Copy(pixels, rgba, 0, rgba.Length);
                return rgba;
            }
            finally
            {
                PylonFreeImage(pixels);
            }
        }
    }

    /// <summary>
    /// RGBA（行序：首行是图像顶部）→ GL 纹理名。游戏线程调；渲染线程下一次 Apply 时上传。
    /// 首帧/池空返回 0，调用方要允许重试。
    /// </summary>
    public static ulong UploadTexture(byte[] rgba, int width, int height)
    {
        if (rgba == null || width <= 0 || height <= 0) return 0;
        if (rgba.LongLength < (long)width * height * 4) return 0;
        fixed (byte* pixels = rgba)
        {
            return igCreateTexture(pixels, width, height);
        }
    }

    public static void DestroyTexture(ulong texture) => igDestroyTexture(texture);
}
