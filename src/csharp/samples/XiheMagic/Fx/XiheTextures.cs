using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using Pylon.ModLoader;
using Pylon.ModLoader.Effects;
using Pylon.ModLoader.Textures;

namespace XiheMagic
{
    internal static class XiheTextures
    {
        [DllImport("pylon", EntryPoint = "PylonFx_TextureLoad")]
        private static extern int TextureLoad(string key, int width, int height,
            [In] byte[] rgba, int linear, int repeat, out int outWidth, out int outHeight);

        private static ConstructorInfo _ctor;

        internal static ScreenTexture Load(Mod mod, string path, bool repeat)
        {
            var full = PylonTexture.Resolve(mod, path);
            var png = Decode(File.ReadAllBytes(full), out var width, out var height);
            return LoadRaw(full, width, height, png, repeat);
        }

        /// <summary>把已解好的 RGBA 交给内核（行序 v=0 在底部）。</summary>
        internal static ScreenTexture LoadRaw(string key, int width, int height, byte[] rgba, bool repeat)
        {
            var id = TextureLoad(key, width, height, rgba, 1, repeat ? 1 : 0, out var ow, out var oh);
            if (id == 0) throw new InvalidOperationException($"效果纹理登记失败：{key}");

            _ctor ??= typeof(ScreenTexture).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(int), typeof(int), typeof(int) },
                null);
            if (_ctor == null) throw new MissingMethodException("ScreenTexture(int,int,int) 找不到");

            return (ScreenTexture)_ctor.Invoke(new object[] { id, ow, oh });
        }

        // ---------------- 最小 PNG 解码 ----------------

        private static byte[] Decode(byte[] data, out int width, out int height)
        {
            if (data.Length < 8 || data[0] != 137 || data[1] != 80 || data[2] != 78 || data[3] != 71)
                throw new InvalidDataException("不是 PNG");

            var pos = 8;
            var idat = new MemoryStream();
            byte[] palette = null;
            byte[] trns = null;
            int bitDepth = 8, colorType = 6;
            width = height = 0;

            while (pos + 8 <= data.Length)
            {
                var length = ReadInt32(data, pos);
                var type = System.Text.Encoding.ASCII.GetString(data, pos + 4, 4);
                var body = pos + 8;
                if (length < 0 || body + length > data.Length) throw new InvalidDataException("PNG 块越界");

                switch (type)
                {
                    case "IHDR":
                        width = ReadInt32(data, body);
                        height = ReadInt32(data, body + 4);
                        bitDepth = data[body + 8];
                        colorType = data[body + 9];
                        if (data[body + 12] != 0) throw new NotSupportedException("隔行 PNG 不支持");
                        break;
                    case "PLTE":
                        palette = new byte[length];
                        Buffer.BlockCopy(data, body, palette, 0, length);
                        break;
                    case "tRNS":
                        trns = new byte[length];
                        Buffer.BlockCopy(data, body, trns, 0, length);
                        break;
                    case "IDAT":
                        idat.Write(data, body, length);
                        break;
                }

                pos = body + length + 4;   // 跳过 CRC
                if (type == "IEND") break;
            }

            if (width <= 0 || height <= 0) throw new InvalidDataException("PNG 尺寸无效");
            if (bitDepth != 8 && bitDepth != 16) throw new NotSupportedException($"PNG 位深 {bitDepth} 不支持");

            idat.Position = 0;
            byte[] raw;
            using (var z = new ZLibStream(idat, CompressionMode.Decompress))
            using (var outStream = new MemoryStream())
            {
                z.CopyTo(outStream);
                raw = outStream.ToArray();
            }

            var channels = colorType switch
            {
                0 => 1,
                2 => 3,
                3 => 1,
                4 => 2,
                6 => 4,
                _ => throw new NotSupportedException($"PNG 颜色类型 {colorType} 不支持"),
            };
            var sampleBytes = bitDepth / 8;
            var bpp = channels * sampleBytes;
            var stride = width * bpp;
            if (raw.Length < height * (stride + 1)) throw new InvalidDataException("PNG 数据不完整");

            var rgba = new byte[width * height * 4];
            var cur = new byte[stride];
            var prev = new byte[stride];
            var input = 0;

            for (var y = 0; y < height; y++)
            {
                var filter = raw[input++];
                for (var x = 0; x < stride; x++)
                {
                    var value = raw[input++];
                    var a = x >= bpp ? cur[x - bpp] : 0;
                    var b = prev[x];
                    var c = x >= bpp ? prev[x - bpp] : 0;

                    var filtered = filter switch
                    {
                        0 => value,
                        1 => value + a,
                        2 => value + b,
                        3 => value + (a + b) / 2,
                        4 => value + Paeth(a, b, c),
                        _ => throw new InvalidDataException($"PNG 过滤类型 {filter} 不支持"),
                    };
                    cur[x] = (byte)filtered;
                }

                // PNG 行序是上→下；GL 纹理要 v=0 在底部，所以写到翻转后的行。
                var dst = (height - 1 - y) * width * 4;
                for (var x = 0; x < width; x++)
                {
                    var s = x * bpp;
                    byte r, g, bl, al;
                    switch (colorType)
                    {
                        case 6:
                            r = cur[s]; g = cur[s + 1]; bl = cur[s + 2]; al = cur[s + 3];
                            break;
                        case 2:
                            r = cur[s]; g = cur[s + 1]; bl = cur[s + 2]; al = 255;
                            break;
                        case 0:
                            r = g = bl = cur[s]; al = 255;
                            break;
                        case 4:
                            r = g = bl = cur[s]; al = cur[s + 1];
                            break;
                        default:
                        {
                            var index = cur[s];
                            var p = index * 3;
                            r = palette != null && p + 2 < palette.Length ? palette[p] : (byte)0;
                            g = palette != null && p + 2 < palette.Length ? palette[p + 1] : (byte)0;
                            bl = palette != null && p + 2 < palette.Length ? palette[p + 2] : (byte)0;
                            al = trns != null && index < trns.Length ? trns[index] : (byte)255;
                            break;
                        }
                    }

                    var o = dst + x * 4;
                    rgba[o] = r;
                    rgba[o + 1] = g;
                    rgba[o + 2] = bl;
                    rgba[o + 3] = al;
                }

                var swap = prev;
                prev = cur;
                cur = swap;
            }

            return rgba;
        }

        private static int ReadInt32(byte[] data, int offset) =>
            (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];

        private static int Paeth(int a, int b, int c)
        {
            var p = a + b - c;
            var pa = Math.Abs(p - a);
            var pb = Math.Abs(p - b);
            var pc = Math.Abs(p - c);
            if (pa <= pb && pa <= pc) return a;
            return pb <= pc ? b : c;
        }
    }
}
