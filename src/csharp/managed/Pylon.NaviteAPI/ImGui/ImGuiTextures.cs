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
