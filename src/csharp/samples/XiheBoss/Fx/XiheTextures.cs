using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Color = Il2CppMicrosoft.Xna.Framework.Graphics.Color;
using Texture2D = Il2CppMicrosoft.Xna.Framework.Graphics.Texture2D;
using Pylon.ModLoader;
using Pylon.ModLoader.Effects;
using Pylon.ModLoader.Textures;
using Pylon.ModLoader.Core;
using Pylon.ModLoader.Logging;

namespace XiheBoss
{

    internal enum Tex
    {
        Ball = 0,
        Ball2,
        Ball3,
        Tex0,
        Tex1,
        Tex2,
        Tex3,
        Tex4,
        Laser,
        Laser0,
        Trail,
        Trail209,
        TrailShape,
        Wave,
        Wave1,
        Wave2,
        Extra89,
        Arc,
        WarpRing,
        WarpRing2,
        WarpBall,
        DustLight,
        LightDust,
        Flame,
        Explosion,
        Perlin,
        MagicRing,
        KnifeExtra1,
        EolProj,
        EolProjColor,
        Elip,

        ProjTex,
        Proj919,
        ImgColor,
        MagicPixel,
        Transparent,
        SlimeBall,
        Extra196,
        ColorBlood,
        Count,
    }

    internal struct AtlasCell
    {
        public float U0, V0, DU, DV;
    }

    internal static class XiheTextures
    {

        internal static ScreenGraphics.Texture Atlas { get; private set; }

        internal static ScreenGraphics.Texture Noise { get; private set; }

        internal static Texture2D WavePerlin1200 { get; private set; }

        internal static ScreenGraphics.Texture BigOuter { get; private set; }
        internal static ScreenGraphics.Texture BigCore { get; private set; }
        internal static ScreenGraphics.Texture BigArc { get; private set; }
        internal static ScreenGraphics.Texture BigStar { get; private set; }
        internal static ScreenGraphics.Texture BigRing { get; private set; }

        internal static AtlasCell[] Cells { get; private set; }

        private static byte[] _atlasRgba;
        private static int _atlasWidth;
        private static int _atlasHeight;

        internal static int[] Widths { get; private set; }
        internal static int[] Heights { get; private set; }

        internal static int WidthOf(Tex tex) =>
            Widths == null || (int)tex < 0 || (int)tex >= Widths.Length ? 1 : Widths[(int)tex];
        internal static int HeightOf(Tex tex) =>
            Heights == null || (int)tex < 0 || (int)tex >= Heights.Length ? 1 : Heights[(int)tex];

        private static readonly (Tex id, string file)[] Files =
        {
            (Tex.Ball, "assets/textures/Ball.png"),
            (Tex.Ball2, "assets/textures/Ball2.png"),
            (Tex.Ball3, "assets/textures/Ball3.png"),
            (Tex.Tex0, "assets/textures/tex0.png"),
            (Tex.Tex1, "assets/textures/tex1.png"),
            (Tex.Tex2, "assets/textures/tex2.png"),
            (Tex.Tex3, "assets/textures/tex3.png"),
            (Tex.Tex4, "assets/textures/tex4.png"),
            (Tex.Laser, "assets/textures/Laser.png"),
            (Tex.Laser0, "assets/textures/Laser0.png"),
            (Tex.Trail, "assets/textures/Trail.png"),
            (Tex.Trail209, "assets/textures/Trail209.png"),
            (Tex.TrailShape, "assets/textures/TrailShape.png"),
            (Tex.Wave, "assets/textures/Wave.png"),
            (Tex.Wave1, "assets/textures/Wave1.png"),
            (Tex.Wave2, "assets/textures/Wave2.png"),
            (Tex.Extra89, "assets/textures/Extra89.png"),

            (Tex.Arc, "assets/textures/Arc.png"),
            (Tex.WarpRing, "assets/textures/WarpRing.png"),
            (Tex.WarpRing2, "assets/textures/WarpRing2.png"),
            (Tex.WarpBall, "assets/textures/WarpBall.png"),
            (Tex.DustLight, "assets/textures/DustLight.png"),
            (Tex.LightDust, "assets/textures/LightDust.png"),
            (Tex.Flame, "assets/textures/Flame.png"),
            (Tex.Explosion, "assets/textures/Explosion.png"),
            (Tex.Perlin, "assets/textures/Perlin.png"),
            (Tex.MagicRing, "assets/textures/MagicRing.png"),
            (Tex.KnifeExtra1, "assets/textures/KnifeExtra1.png"),
            (Tex.EolProj, "assets/textures/EolProj.png"),
            (Tex.EolProjColor, "assets/textures/EolProjColor.png"),
            (Tex.Elip, "assets/textures/Elip.png"),
            (Tex.ProjTex, "assets/textures/ProjTex.png"),
            (Tex.Proj919, "assets/textures/Projectile_919.png"),
            (Tex.ImgColor, "assets/textures/ImgColor.png"),
            (Tex.SlimeBall, "assets/textures/SlimeBall.png"),
            (Tex.Extra196, "assets/textures/Extra_196.png"),
            (Tex.ColorBlood, "assets/textures/Color_Blood.png"),
        };

        private struct Image
        {
            public int W, H;
            public byte[] Rgba;
        }

        internal static void Bind(Mod mod, ModLogger log)
        {
            var images = new Image[(int)Tex.Count];
            var totalW = 0;
            foreach (var (id, file) in Files)
            {
                try
                {
                    var full = PylonTexture.Resolve(mod, file);
                    images[(int)id] = new Image
                    {
                        Rgba = Decode(File.ReadAllBytes(full), out var w, out var h),
                        W = w,
                        H = h,
                    };
                    if (w > totalW) totalW = w;
                }
                catch (Exception e)
                {
                    log?.Warn($"[tex] {file} 读取失败：{e.Message}");
                }
            }

            images[(int)Tex.MagicPixel] = new Image
            {
                W = 1,
                H = 1,
                Rgba = new byte[] { 255, 255, 255, 255 },
            };
            images[(int)Tex.Transparent] = new Image
            {
                W = 1,
                H = 1,
                Rgba = new byte[4],
            };

            const int atlasW = 1024;
            const int pad = 2;
            var cells = new AtlasCell[(int)Tex.Count];
            var widths = new int[(int)Tex.Count];
            var heights = new int[(int)Tex.Count];
            var placements = new (int x, int y, Image img)[(int)Tex.Count];
            var x = pad;
            var y = pad;
            var rowH = 0;
            var maxY = 0;
            for (var i = 0; i < images.Length; i++)
            {
                var img = images[i];
                if (img.Rgba == null) continue;
                if (x + img.W + pad > atlasW)
                {
                    x = pad;
                    y += rowH + pad;
                    rowH = 0;
                }
                placements[i] = (x, y, img);
                widths[i] = img.W;
                heights[i] = img.H;
                x += img.W + pad;
                if (img.H > rowH) rowH = img.H;
                if (y + rowH > maxY) maxY = y + rowH;
            }

            var atlasH = 1;
            while (atlasH < maxY + pad) atlasH <<= 1;

            var rgba = new byte[atlasW * atlasH * 4];
            for (var i = 0; i < placements.Length; i++)
            {
                var (px, py, img) = placements[i];
                if (img.Rgba == null) continue;
                for (var row = 0; row < img.H; row++)
                {
                    Buffer.BlockCopy(img.Rgba, row * img.W * 4,
                        rgba, ((py + row) * atlasW + px) * 4, img.W * 4);
                }
                cells[i] = new AtlasCell
                {
                    U0 = (float)px / atlasW,
                    V0 = (float)py / atlasH,
                    DU = (float)img.W / atlasW,
                    DV = (float)img.H / atlasH,
                };
            }

            Atlas = Upload("XiheBoss/atlas", atlasW, atlasH, rgba, repeat: false, linear: false);
            _atlasRgba = rgba;
            _atlasWidth = atlasW;
            _atlasHeight = atlasH;
            Cells = cells;
            Widths = widths;
            Heights = heights;

            try { Noise = Load(mod, "assets/textures/Perlin.png", repeat: true); }
            catch (Exception e) { log?.Warn("[tex] Perlin 读取失败：" + e.Message); }
            try { WavePerlin1200 = PylonTexture.LoadTexture(mod, "assets/textures/WavePerlin1200.png"); }
            catch (Exception e) { log?.Warn("[tex] WavePerlin1200 读取失败：" + e.Message); }

            try { BigOuter = Load(mod, "assets/textures/tex3.png", repeat: true); }
            catch (Exception e) { log?.Warn("[tex] 大光柱 tex3 读取失败：" + e.Message); }
            try { BigCore = Load(mod, "assets/textures/tex0.png", repeat: true); }
            catch (Exception e) { log?.Warn("[tex] 大光柱 tex0 读取失败：" + e.Message); }
            try { BigArc = Load(mod, "assets/textures/Arc.png", repeat: false); }
            catch (Exception e) { log?.Warn("[tex] 大光柱 Arc 读取失败：" + e.Message); }
            try { BigStar = Load(mod, "assets/textures/Extra89.png", repeat: false); }
            catch (Exception e) { log?.Warn("[tex] 大光柱 Star 读取失败：" + e.Message); }
            try { BigRing = Load(mod, "assets/textures/MagicRing.png", repeat: true); }
            catch (Exception e) { log?.Warn("[tex] 大光柱 MagicRing 读取失败：" + e.Message); }

            log?.Info($"[tex] 图集 {atlasW}x{atlasH}（{CountLoaded(images)} 张）、Noise={(Noise != null ? "ok" : "无")}、BigLaser={(BigOuter != null && BigCore != null && BigRing != null ? "ok" : "无")}");
        }

        private static int CountLoaded(Image[] images)
        {
            var n = 0;
            foreach (var i in images) if (i.Rgba != null) n++;
            return n;
        }

        internal static AtlasCell Cell(Tex tex) =>
            Cells == null || (int)tex < 0 || (int)tex >= Cells.Length ? default : Cells[(int)tex];

        internal static Color Sample(Tex tex, float u, float v)
        {
            if (_atlasRgba == null) return Color.White;
            var cell = Cell(tex);
            if (cell.DU <= 0f || cell.DV <= 0f) return Color.White;
            var x = (int)Math.Clamp((cell.U0 + Math.Clamp(u, 0f, 1f) * cell.DU) * _atlasWidth, 0, _atlasWidth - 1);

            var y = (int)Math.Clamp((cell.V0 + (1f - Math.Clamp(v, 0f, 1f)) * cell.DV) * _atlasHeight, 0, _atlasHeight - 1);
            var i = (y * _atlasWidth + x) * 4;
            return new Color(_atlasRgba[i], _atlasRgba[i + 1], _atlasRgba[i + 2], _atlasRgba[i + 3]);
        }

        private static ScreenGraphics.Texture Load(Mod mod, string path, bool repeat)
        {
            var full = PylonTexture.Resolve(mod, path);
            var png = Decode(File.ReadAllBytes(full), out var width, out var height);
            return Upload(full, width, height, png, repeat);
        }

        private static ScreenGraphics.Texture Upload(string key, int width, int height, byte[] rgba, bool repeat,
            bool linear = true) => ScreenGraphics.Texture.FromRaw(key, width, height, rgba, linear, repeat);

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

                pos = body + length + 4;
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
                for (var xi = 0; xi < stride; xi++)
                {
                    var value = raw[input++];
                    var a = xi >= bpp ? cur[xi - bpp] : 0;
                    var b = prev[xi];
                    var c = xi >= bpp ? prev[xi - bpp] : 0;

                    var filtered = filter switch
                    {
                        0 => value,
                        1 => value + a,
                        2 => value + b,
                        3 => value + (a + b) / 2,
                        4 => value + Paeth(a, b, c),
                        _ => throw new InvalidDataException($"PNG 过滤类型 {filter} 不支持"),
                    };
                    cur[xi] = (byte)filtered;
                }

                var dst = (height - 1 - y) * width * 4;
                for (var xi = 0; xi < width; xi++)
                {
                    var s = xi * bpp;
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

                    var o = dst + xi * 4;
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
