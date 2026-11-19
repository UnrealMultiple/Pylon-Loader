using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Pylon.ModLoader.Textures;
using Pylon.NaviteAPI.Effects;
using UnityEngine;

namespace Pylon.ModLoader.Effects;

public static partial class ScreenGraphics
{
    /// <summary>
    /// 渲染目标/纹理的公共基类。<c>Texture</c> 是 native 纹理 id：负数固定源，
    /// 正数由 <see cref="ScreenFrame.Target"/> 分配的离屏目标或 <see cref="Texture"/> 上传的贴图。
    /// </summary>
    public class Target
    {
        public int Texture { get; }

        public string Name { get; }

        public float Scale { get; }

        public bool Persistent { get; }

        internal Target(int texture, string name, float scale = 1f, bool persistent = false)
        {
            Texture = texture;
            Name = name;
            Scale = scale;
            Persistent = persistent;
        }

        public override string ToString() => Name;
    }

    /// <summary>供效果 shader 采样的 RGBA 纹理。</summary>
    public sealed class Texture : Target
    {
        public int Width { get; }

        public int Height { get; }

        public string Path { get; }

        internal Texture(int id, string path, int width, int height)
            : base(id, path)
        {
            Path = path;
            Width = width;
            Height = height;
        }

        /// <summary>按 Mod 相对路径加载 PNG/JPG；<paramref name="nearest"/> = false 即线性过滤。</summary>
        public static Texture Load(Mod mod, string path, bool nearest = false, bool repeat = false)
        {
            ArgumentNullException.ThrowIfNull(mod);
            var file = HasImageExtension(path) ? path : path + ".png";
            var full = PylonTexture.Resolve(mod, file);
            byte[] fileBytes;
            try { fileBytes = File.ReadAllBytes(full); }
            catch (Exception e) { throw new ScreenEffectsException($"读取效果纹理失败：{full}（{e.Message}）"); }

            var bytes = new Il2CppStructArray<byte>(fileBytes);
            var unity = new Texture2D(2, 2);
            bool decoded;
            try { decoded = ImageConversion.LoadImage(unity, bytes, false); }
            catch (Exception e)
            {
                UnityEngine.Object.Destroy(unity);
                throw new ScreenEffectsException($"效果纹理解码抛异常：{full}（{e.Message}）");
            }
            if (!decoded)
            {
                UnityEngine.Object.Destroy(unity);
                throw new ScreenEffectsException($"'{full}' 不是能解码的 PNG/JPG");
            }

            var width = unity.width;
            var height = unity.height;
            var rgba = FlipRows(unity.GetRawTextureData<byte>().ToArray(), width, height);
            UnityEngine.Object.Destroy(unity);

            int id;
            try
            {
                id = ScreenEffectsNative.TextureLoad(full, width, height, rgba,
                    nearest ? 0 : 1, repeat ? 1 : 0, out _, out _);
            }
            catch (Exception e) { throw new ScreenEffectsException($"效果纹理登记失败：{full}（{e.Message}）"); }
            if (id == 0) throw new ScreenEffectsException($"效果纹理登记失败：{full}（参数无效）");
            return new Texture(id, Shader.FileKey(mod, file), width, height);
        }

        /// <summary>直接上传 RGBA 字节（C# 侧已解码/生成的纹理）。</summary>
        public static Texture FromRaw(string key, int width, int height, byte[] rgba,
            bool linear = true, bool repeat = false)
        {
            ArgumentException.ThrowIfNullOrEmpty(key);
            ArgumentNullException.ThrowIfNull(rgba);
            if (width <= 0 || height <= 0)
                throw new ScreenEffectsException($"效果纹理 '{key}' 尺寸无效：{width}x{height}");
            if (rgba.LongLength < (long)width * height * 4)
                throw new ScreenEffectsException($"效果纹理 '{key}' 的 RGBA 数据不足 {width}x{height}");

            int id, outWidth, outHeight;
            try
            {
                id = ScreenEffectsNative.TextureLoad(key, width, height, rgba, linear ? 1 : 0, repeat ? 1 : 0,
                    out outWidth, out outHeight);
            }
            catch (Exception e) { throw new ScreenEffectsException($"效果纹理登记失败：{key}（{e.Message}）"); }
            if (id == 0) throw new ScreenEffectsException($"效果纹理登记失败：{key}（参数无效）");
            return new Texture(id, key, outWidth > 0 ? outWidth : width, outHeight > 0 ? outHeight : height);
        }

        /// <summary>写一张中性 LUT（Size² x Size 的条带），给 ColorGrade 用。</summary>
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
                        rgba[idx++] = (byte)Mathf.RoundToInt(x * inv * 255f);
                        rgba[idx++] = g;
                        rgba[idx++] = b;
                        rgba[idx++] = 255;
                    }
                }
            }
            File.WriteAllBytes(path, PngEncoder.Encode(w, size, rgba));
        }

        private static bool HasImageExtension(string path) =>
            path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);

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

    /// <summary>固定采样源。</summary>
    public static class Sources
    {
        /// <summary>已画好的画面拷贝（<c>uScreen</c>）。</summary>
        public static readonly Target Screen = new(kTexScreen, "Screen");

        /// <summary>上一帧世界（已含效果，<c>uHistory</c>）。</summary>
        public static readonly Target History = new(kTexHistory, "History");
    }

    internal const int kTexNone = 0;
    internal const int kTexScreen = -1;
    internal const int kTexHistory = -2;
}