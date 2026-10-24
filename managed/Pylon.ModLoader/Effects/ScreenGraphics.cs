using System;
using System.IO;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Pylon.ModLoader.Textures;
using Pylon.ModLoader.UI;
using UnityEngine;

namespace Pylon.ModLoader.Effects
{
    /// <summary>屏幕效果操作失败。</summary>
    public sealed class ScreenEffectsException : Exception
    {
        public ScreenEffectsException(string message) : base(message) { }
    }

    /// <summary>屏幕效果统计快照（内核 RenderStats）。</summary>
    public sealed class ScreenStats
    {
        public long WorldPostRender, UiPostRender, OtherPostRender;
        public long Draws, Frames, MarkersIssued, MarkerHits, ScreenFrames;
        public long RenderCpuNs, MarkerCpuNs, GpuNs, GpuSamples, GpuFrameNs, GpuFrameSamples;
        public long ShadowChecks, ShadowMismatches, BoxPixels;
        public int DrawFbo, ReadFbo, Samples, BlitError, CopyFallbacks;
        public int[] Viewport { get; private set; } = new int[4];
        public bool HookInstalled, HasContext, MarkerReady, GpuTimer;

        internal static ScreenStats From(PylonFxStatsNative n)
        {
            return new ScreenStats
            {
                WorldPostRender = n.WorldPostRender,
                UiPostRender = n.UiPostRender,
                OtherPostRender = n.OtherPostRender,
                Draws = n.Draws,
                Frames = n.Frames,
                MarkersIssued = n.MarkersIssued,
                MarkerHits = n.MarkerHits,
                ScreenFrames = n.ScreenFrames,
                RenderCpuNs = n.RenderCpuNs,
                MarkerCpuNs = n.MarkerCpuNs,
                GpuNs = n.GpuNs,
                GpuSamples = n.GpuSamples,
                GpuFrameNs = n.GpuFrameNs,
                GpuFrameSamples = n.GpuFrameSamples,
                ShadowChecks = n.ShadowChecks,
                ShadowMismatches = n.ShadowMismatches,
                BoxPixels = n.BoxPixels,
                DrawFbo = n.DrawFbo,
                ReadFbo = n.ReadFbo,
                Samples = n.Samples,
                BlitError = n.BlitError,
                CopyFallbacks = n.CopyFallbacks,
                HookInstalled = n.HookInstalled != 0,
                HasContext = n.HasContext != 0,
                MarkerReady = n.MarkerReady != 0,
                GpuTimer = n.GpuTimer != 0,
            };
        }
    }

    /// <summary>屏幕效果（全屏后处理 GLSL）。Mod 每帧构造 ScreenFrame 提交。</summary>
    public static class ScreenGraphics
    {
        private static bool _installTried;
        private static readonly float[] BoxBuf = new float[4];
        private static readonly float[] CenterBuf = new float[2];
        private static readonly float[] ColorBuf = new float[4];

        /// <summary>内核 hooks 是否已装（PylonFx_Install 结果）。</summary>
        public static bool Installed
        {
            get
            {
                try { return ScreenEffectsNative.Available() != 0; }
                catch { return false; }
            }
        }

        /// <summary>
        /// 装渲染 hooks（GL hooks + Camera.FireOnPostRender）。幂等；框架在内容就绪时自动调一次，
        /// 这里再暴露给需要更早/更晚的 Mod。只能在游戏线程（UnityMain）调。
        /// </summary>
        public static void Install()
        {
            if (_installTried) return;
            _installTried = true;
            if (!PylonTexture.IsGameThread)
            {
                _installTried = false;  // 让换对线程后还能再试
                throw new ScreenEffectsException("ScreenGraphics.Install 只能在游戏线程(UnityMain)上调用");
            }
            try
            {
                var ok = ScreenEffectsNative.Install() != 0;
                ModUI.Logger?.Info($"屏幕效果：hooks {(ok ? "已装" : "安装失败（见日志）")}");
            }
            catch (Exception e)
            {
                _installTried = false;
                ModUI.Logger?.Warn($"屏幕效果安装异常：{e.Message}");
            }
        }

        /// <summary>注册一个片元着色器（GLSL ES 3.00，可带 #include &lt;bl/...&gt;）。同名字同源码复用同一 id。</summary>
        public static ScreenShader Shader(string name, string source) => new(name, source);

        /// <summary>
        /// 从 Mod 资产加载一张效果纹理（PNG/JPG，相对路径按 Mod 目录解析）。游戏线程调。
        /// 行序已翻成 v=0 在图像底部，shader 里 vUV 和图像方向一致。
        /// </summary>
        public static ScreenTexture Texture(Mod owner, string path, bool linear = true, bool repeat = false)
        {
            var full = PylonTexture.Resolve(owner, path);
            byte[] fileBytes;
            try { fileBytes = File.ReadAllBytes(full); }
            catch (Exception e) { throw new ScreenEffectsException($"读取效果纹理失败：{full}（{e.Message}）"); }

            var bytes = new Il2CppStructArray<byte>(fileBytes);
            var unity = new UnityEngine.Texture2D(2, 2);
            bool decoded;
            try { decoded = ImageConversion.LoadImage(unity, bytes, false); }
            catch (Exception e) { UnityEngine.Object.Destroy(unity); throw new ScreenEffectsException($"效果纹理解码抛异常：{full}（{e.Message}）"); }
            if (!decoded) { UnityEngine.Object.Destroy(unity); throw new ScreenEffectsException($"'{full}' 不是能解码的 PNG/JPG"); }

            var width = unity.width;
            var height = unity.height;
            // GetRawTextureData<T>() 返回 NativeArray<byte>（Unity 2021.3）；拷成托管数组再翻行
            var rgba = FlipRows(unity.GetRawTextureData<byte>().ToArray(), width, height);
            UnityEngine.Object.Destroy(unity);

            int id;
            try
            {
                id = ScreenEffectsNative.TextureLoad(full, width, height, rgba, linear ? 1 : 0, repeat ? 1 : 0, out _, out _);
            }
            catch (Exception e) { throw new ScreenEffectsException($"效果纹理登记失败：{full}（{e.Message}）"); }
            if (id == 0) throw new ScreenEffectsException($"效果纹理登记失败：{full}（参数无效）");
            return new ScreenTexture(id, width, height);
        }

        /// <summary>提交一帧（游戏线程）。没步骤的帧会被忽略。</summary>
        public static void Submit(ScreenFrame frame)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (frame.Draws.Count == 0) return;
            Install();

            ScreenEffectsNative.SubmitBegin(frame.History ? 1 : 0);
            foreach (var target in frame.Targets)
            {
                ScreenEffectsNative.SubmitAddTarget(target.Id, target.Name, target.Scale, target.Persistent ? 1 : 0);
            }
            foreach (var draw in frame.Draws)
            {
                // Box 只接受 4 个数（x0,y0,x1,y1）；长度不对按 null（整屏默认）处理。
                var hasBox = draw.Box != null && draw.Box.Length == 4;
                if (hasBox) Array.Copy(draw.Box, BoxBuf, 4);
                CenterBuf[0] = draw.Center.x;
                CenterBuf[1] = draw.Center.y;
                ColorBuf[0] = draw.Color.r;
                ColorBuf[1] = draw.Color.g;
                ColorBuf[2] = draw.Color.b;
                ColorBuf[3] = draw.Color.a;
                var handle = ScreenEffectsNative.SubmitAddDraw(
                    draw.Shader?.Id ?? 0,
                    (int)draw.Layer,
                    draw.Dest,
                    draw.Source?.Id ?? ScreenTexture.kTexNone,
                    hasBox ? BoxBuf : null,
                    CenterBuf,
                    draw.Radius,
                    draw.Intensity,
                    draw.Progress,
                    draw.Age,
                    ColorBuf);
                draw.Submit(handle);
            }
            ScreenEffectsNative.SubmitEnd();
        }

        /// <summary>
        /// 写一张中性 LUT（size² × size，RGB 恒等映射）到 Mod 目录，供 Modder 编辑后再用。
        /// 纯 C# 生成 PNG（游戏的 ImageConversion.EncodeToPNG / Texture2D.SetPixels 在代理里不可用，
        /// 且这里不需要真建一张 Unity 纹理）。
        /// </summary>
        public static void SaveNeutralLut(string path, int size = 16)
        {
            if (size < 2) throw new ArgumentException("LUT 尺寸必须 >= 2", nameof(size));
            var inv = 1f / (size - 1f);
            var w = size * size;
            var rgba = new byte[w * size * 4];
            var idx = 0;
            for (var s = 0; s < size; s++)
            {
                var b = (byte)Mathf.RoundToInt(s * inv * 255f);
                for (var y = 0; y < size; y++)
                {
                    var g = (byte)Mathf.RoundToInt(y * inv * 255f);
                    for (var x = 0; x < size; x++)
                    {
                        rgba[idx++] = (byte)Mathf.RoundToInt(x * inv * 255f);  // R
                        rgba[idx++] = g;
                        rgba[idx++] = b;
                        rgba[idx++] = 255;
                    }
                }
            }
            File.WriteAllBytes(path, PngEncoder.Encode(w, size, rgba));
        }

        /// <summary>统计快照（任意线程）。</summary>
        public static ScreenStats Stats()
        {
            ScreenEffectsNative.Stats(out var native);
            return ScreenStats.From(native);
        }

        /// <summary>请内核录下一整帧 GL 调用进日志（排帧点问题用）。</summary>
        public static void RequestTrace() => ScreenEffectsNative.RequestTrace();

        // Unity 原始数据第一行是顶部；GL 的 v=0 在底部（和 vUV 一致），翻行。
        private static byte[] FlipRows(byte[] raw, int width, int height)
        {
            var row = width * 4;
            var outBytes = new byte[row * height];
            for (var y = 0; y < height; y++)
            {
                var src = y * row;
                var dst = (height - 1 - y) * row;
                for (var i = 0; i < row; i++) outBytes[dst + i] = raw[src + i];
            }
            return outBytes;
        }
    }
}
