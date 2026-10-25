// 贴图：PNG 解码（照 XiheMagic 的实现，绕开 GetRawTextureData<T> 的 AOT 坑）→ 一张图集（atlas）。
//
// 为什么要图集：屏幕效果 shader 只能在 uniform 里传数据，sampler 数组又不能动态下标。把 Boss 用到的
// 贴图（MEAC 原素材）打进一张 RGBA 图集，每个图元只带自己的 uv 矩形，shader 用一次 texture() 取样。
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using Color = Il2CppMicrosoft.Xna.Framework.Graphics.Color;
using Pylon.ModLoader;
using Pylon.ModLoader.Effects;
using Pylon.ModLoader.Textures;

namespace XiheBoss
{
    /// <summary>图集里的贴图槽（顺序 = 打包顺序，Shaders 里也按这个顺序算 uv）。</summary>
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
        // MEAC 原素材/原版贴图（弹幕绘制用）
        ProjTex,       // MEAC/Projectiles/projTex：EoLLight / CrystalArrow 的本体长条
        Proj919,       // Terraria/Images/Projectile_919：EoLLance 的枪身
        ImgColor,      // MEAC/Images/KnifeLight/img_color：EoLFire 的 Trail 颜色条
        MagicPixel,    // 1x1 白：EoLLance 的顶点光矛 / Projectile_0 的等价物
        Transparent,   // 1x1 全透明：Trail 的 uImage2（Projectile_0 在 MEAC 里是空白叠加）
        Count,
    }

    internal struct AtlasCell
    {
        public float U0, V0, DU, DV;
    }

    internal static class XiheTextures
    {
        [DllImport("pylon", EntryPoint = "PylonFx_TextureLoad")]
        private static extern int TextureLoad(string key, int width, int height,
            [In] byte[] rgba, int linear, int repeat, out int outWidth, out int outHeight);

        private static ConstructorInfo _ctor;

        /// <summary>图集在 shader 里的 sampler（uAtlas）。</summary>
        internal static ScreenTexture Atlas { get; private set; }

        /// <summary>单独一张：天空渐变（uSkyTex，8x1024 竖条）。</summary>
        internal static ScreenTexture Sky { get; private set; }

        /// <summary>单独一张：Perlin 噪声（uNoise，屏幕效果里做抖动/扭曲）。</summary>
        internal static ScreenTexture Noise { get; private set; }

        // EoLBigLaser 专用 shader（复用 XiheMagic 的 XiheBeam.frag）要单独绑 5 张 sampler。
        internal static ScreenTexture BigOuter { get; private set; }
        internal static ScreenTexture BigCore { get; private set; }
        internal static ScreenTexture BigArc { get; private set; }
        internal static ScreenTexture BigStar { get; private set; }
        internal static ScreenTexture BigRing { get; private set; }

        internal static AtlasCell[] Cells { get; private set; }

        private static byte[] _atlasRgba;
        private static int _atlasWidth;
        private static int _atlasHeight;

        /// <summary>图集里每张贴图的原始像素尺寸（精灵绘制要按纹理像素算 origin/uv）。</summary>
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
        };

        private struct Image
        {
            public int W, H;
            public byte[] Rgba;   // v=0 在底部
        }

        /// <summary>游戏线程（SetupContent）一次：解码所有 PNG → 打包 → 上传内核。</summary>
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

            // 1x1 白（MEAC 的 MagicPixel / Projectile_0 等价物）。代码生成，不占素材文件。
            images[(int)Tex.MagicPixel] = new Image
            {
                W = 2,
                H = 2,
                Rgba = new byte[] { 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 },
            };
            images[(int)Tex.Transparent] = new Image
            {
                W = 2,
                H = 2,
                Rgba = new byte[16],
            };

            // 行式打包：宽度 1024，每张之间留 2px 透明边，避免过滤串色。
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
            while (atlasH < maxY + pad) atlasH <<= 1;   // POT，兼容性最好

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

            try { Sky = Load(mod, "assets/textures/TSSky1.png", repeat: false); }
            catch (Exception e) { log?.Warn("[tex] TSSky1 读取失败：" + e.Message); }
            try { Noise = Load(mod, "assets/textures/Perlin.png", repeat: true); }
            catch (Exception e) { log?.Warn("[tex] Perlin 读取失败：" + e.Message); }

            try { BigOuter = Load(mod, "assets/textures/tex3.png", repeat: true); }
            catch (Exception e) { log?.Warn("[tex] 大光柱 tex3 读取失败：" + e.Message); }
            try { BigCore = Load(mod, "assets/textures/tex0.png", repeat: true); }
            catch (Exception e) { log?.Warn("[tex] 大光柱 tex0 读取失败：" + e.Message); }
            try { BigArc = Load(mod, "assets/textures/Extra89.png", repeat: false); }
            catch (Exception e) { log?.Warn("[tex] 大光柱 Arc 读取失败：" + e.Message); }
            try { BigStar = Load(mod, "assets/textures/Extra89.png", repeat: false); }
            catch (Exception e) { log?.Warn("[tex] 大光柱 Star 读取失败：" + e.Message); }
            try { BigRing = Load(mod, "assets/textures/MagicRing.png", repeat: true); }
            catch (Exception e) { log?.Warn("[tex] 大光柱 MagicRing 读取失败：" + e.Message); }

            log?.Info($"[tex] 图集 {atlasW}x{atlasH}（{CountLoaded(images)} 张）、Sky={(Sky != null ? "ok" : "无")}、Noise={(Noise != null ? "ok" : "无")}、BigLaser={(BigOuter != null && BigCore != null && BigRing != null ? "ok" : "无")}");
        }

        private static int CountLoaded(Image[] images)
        {
            var n = 0;
            foreach (var i in images) if (i.Rgba != null) n++;
            return n;
        }

        internal static AtlasCell Cell(Tex tex) =>
            Cells == null || (int)tex < 0 || (int)tex >= Cells.Length ? default : Cells[(int)tex];

        /// <summary>
        /// CPU 端按局部 uv 采样图集（v=0 顶边，与 XNA 一致）。用于把 MEAC 的颜色图
        /// （EoLProj_Color / img_color）直接接到三角图元的顶点色上。
        /// </summary>
        internal static Color Sample(Tex tex, float u, float v)
        {
            if (_atlasRgba == null) return Color.White;
            var cell = Cell(tex);
            if (cell.DU <= 0f || cell.DV <= 0f) return Color.White;
            var x = (int)Math.Clamp((cell.U0 + Math.Clamp(u, 0f, 1f) * cell.DU) * _atlasWidth, 0, _atlasWidth - 1);
            // 图集第 0 行在 GL v=0（底部）；局部 v=0 是纹理顶边，所以反一下。
            var y = (int)Math.Clamp((cell.V0 + (1f - Math.Clamp(v, 0f, 1f)) * cell.DV) * _atlasHeight, 0, _atlasHeight - 1);
            var i = (y * _atlasWidth + x) * 4;
            return new Color(_atlasRgba[i], _atlasRgba[i + 1], _atlasRgba[i + 2], _atlasRgba[i + 3]);
        }

        private static ScreenTexture Load(Mod mod, string path, bool repeat)
        {
            var full = PylonTexture.Resolve(mod, path);
            var png = Decode(File.ReadAllBytes(full), out var width, out var height);
            return Upload(full, width, height, png, repeat);
        }

        private static ScreenTexture Upload(string key, int width, int height, byte[] rgba, bool repeat,
            bool linear = true)
        {
            var id = TextureLoad(key, width, height, rgba, linear ? 1 : 0, repeat ? 1 : 0, out var ow, out var oh);
            if (id == 0) throw new InvalidOperationException($"效果纹理登记失败：{key}");

            _ctor ??= typeof(ScreenTexture).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(int), typeof(int), typeof(int) },
                null);
            if (_ctor == null) throw new MissingMethodException("ScreenTexture(int,int,int) 找不到");

            return (ScreenTexture)_ctor.Invoke(new object[] { id, ow, oh });
        }

        // ---------------- 最小 PNG 解码（RGBA，v=0 在底部） ----------------

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

                // PNG 行序上→下；GL 要 v=0 在底部，写到翻转后的行。
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


