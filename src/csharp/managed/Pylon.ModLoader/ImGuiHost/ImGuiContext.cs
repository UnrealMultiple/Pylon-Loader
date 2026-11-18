using System.Reflection;
using System.Runtime.InteropServices;
using Pylon.ModLoader.UI;
using Pylon.NaviteAPI.ImGui;

namespace Pylon.ModLoader.ImGuiHost;
/// <summary>ImGui 上下文、字体、样式与整体缩放。全部托管侧持有：内核只提供画布，不碰 ImGui 状态。</summary>
internal static class ImGuiContext
{
    private const string FontResource = "Pylon.ModLoader.ImGuiHost.Fonts.ui.ttf";
    private const float BaseFontSize = 18f;

    private static IntPtr _context;
    private static GCHandle _fontPin;
    private static float _appliedScale = 1f;

    private static byte[]? _baseStyle;

    internal static bool Created => _context != IntPtr.Zero;

    internal static bool Create()
    {
        if (_context != IntPtr.Zero) return true;

        _context = ImGui.CreateContext();
        if (_context == IntPtr.Zero)
        {
            ModUI.Logger?.Error("ImGui.CreateContext 失败");
            return false;
        }
        ImGuiNativeApi.AttachContext(_context);

        if (!LoadFont()) return false;
        ImGui.StyleColorsDark();
        TerrariaTheme.Apply();
        ApplyScale(UiSettings.Scale);
        EnableErrorRecovery();
        return true;
    }

    private static void EnableErrorRecovery()
    {
        var io = ImGui.GetIO();
        io.ConfigErrorRecovery = true;
        io.ConfigErrorRecoveryEnableDebugLog = true;
    }

    internal static void Destroy()
    {
        if (_context == IntPtr.Zero) return;
        try { ImGuiNativeApi.Reset(); }
        catch (Exception e) { ModUI.Logger?.Warn("覆盖层撤销失败：" + e.Message); }
        ImGui.DestroyContext(_context);
        _context = IntPtr.Zero;
        _appliedScale = 1f;
        _baseStyle = null;
        if (_fontPin.IsAllocated) _fontPin.Free();
    }

    internal static unsafe void ApplyScale(float scale)
    {
        if (_context == IntPtr.Zero) return;
        var target = Math.Clamp(scale, UiSettings.MinScale, UiSettings.MaxScale);
        if (Math.Abs(target - _appliedScale) < 0.0001f) return;

        var style = ImGui.GetStyle();
        var size = sizeof(ImGuiStyle);
        var current = (byte*)style.NativePtr;

        if (_baseStyle is null)
        {
            _baseStyle = new byte[size];
            fixed (byte* basePtr = _baseStyle) Buffer.MemoryCopy(current, basePtr, size, size);
        }
        else
        {
            fixed (byte* basePtr = _baseStyle) Buffer.MemoryCopy(basePtr, current, size, size);
        }

        if (Math.Abs(target - 1f) > 0.0001f) style.ScaleAllSizes(target);
        ImGui.GetIO().FontGlobalScale = target;
        _appliedScale = target;
    }

    private static unsafe bool LoadFont()
    {
        byte[] ttf;
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(FontResource);
            if (stream == null)
            {
                ModUI.Logger?.Error("找不到内嵌字体：" + FontResource);
                return false;
            }
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            ttf = memory.ToArray();
        }
        catch (Exception e)
        {
            ModUI.Logger?.Error("读取字体失败：" + e.Message);
            return false;
        }

        _fontPin = GCHandle.Alloc(ttf, GCHandleType.Pinned);
        var atlas = ImGui.GetIO().Fonts;
        var ranges = atlas.GetGlyphRangesChineseFull();

        var font = ImGuiNative.ImFontAtlas_AddFontFromMemoryTTF(
            atlas.NativePtr, (void*)_fontPin.AddrOfPinnedObject(), ttf.Length,
            BaseFontSize, null, ranges);
        if (font == null)
        {
            ModUI.Logger?.Error("AddFontFromMemoryTTF 失败");
            return false;
        }
        return true;
    }
}
