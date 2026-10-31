using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Pylon.ImGui;
using ImGuiApi = Pylon.ImGui.ImGui;

namespace Pylon.ModLoader.UI
{
    /// <summary>
    /// ImGui 上下文、字体、样式与整体缩放。全部托管侧持有：
    /// 内核只提供画布，不碰 ImGui 状态。
    /// </summary>
    internal static class ImGuiContext
    {
        private const string FontResource = "Pylon.ModLoader.ImGui.Fonts.ui.ttf";
        private const float BaseFontSize = 18f;

        private static IntPtr _context;
        private static GCHandle _fontPin;      // 动态字体图集按需取字，TTF 必须常驻
        private static float _appliedScale = 1f;

        internal static bool Created => _context != IntPtr.Zero;

        internal static bool Create()
        {
            if (_context != IntPtr.Zero) return true;

            _context = ImGuiApi.CreateContext();
            if (_context == IntPtr.Zero)
            {
                ModUI.Logger?.Error("ImGui.CreateContext 失败");
                return false;
            }

            if (!LoadFont()) return false;
            ImGuiApi.StyleColorsDark();
            TerrariaTheme.Apply();
            ApplyScale(UiSettings.Scale);
            EnableErrorRecovery();
            return true;
        }

        /// <summary>让 EndFrame 能恢复第三方 Tab 留下的未平衡窗口栈。</summary>
        private static void EnableErrorRecovery()
        {
            var io = ImGuiApi.GetIO();
            io.ConfigErrorRecovery = true;
            io.ConfigErrorRecoveryEnableDebugLog = true;
        }

        internal static void Destroy()
        {
            if (_context == IntPtr.Zero) return;
            ImGuiApi.DestroyContext(_context);
            _context = IntPtr.Zero;
            _appliedScale = 1f;   // 新上下文的样式是默认值，下次 ApplyScale 必须重新施加
            if (_fontPin.IsAllocated) { _fontPin.Free(); }
        }

        /// <summary>整体缩放：字体与控件度量一起变。按比例从当前状态增量施加，可重复调用。</summary>
        internal static void ApplyScale(float scale)
        {
            if (_context == IntPtr.Zero) return;
            var target = Math.Clamp(scale, UiSettings.MinScale, UiSettings.MaxScale);
            if (Math.Abs(target - _appliedScale) < 0.0001f) return;

            ImGuiApi.GetStyle().ScaleAllSizes(target / _appliedScale);
            ImGuiApi.GetIO().FontGlobalScale = target;
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
            var io = ImGuiApi.GetIO();
            var atlas = io.Fonts;
            // 中文字形范围（含全角符号），一次给全，避免运行时缺字
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
}
