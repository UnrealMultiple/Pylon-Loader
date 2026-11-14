using System;
using System.IO;
using System.Text;

namespace Pylon.ModLoader.Textures;

/// <summary>
/// 内存里的小图 → PNG 字节（RGBA，无压缩存储块）：给运行时生成的占位纹理用，
/// 产物直接喂 PylonTexture.Decode 走框架上传管线。
/// </summary>
internal static class RawPng
{
    /// <summary>pixels 按 ABGR 打包（0xAABBGGRR），宽高必须匹配。</summary>
    public static byte[] Encode(int width, int height, uint[] pixels)
    {
        // 每行一个过滤字节（0 = 无过滤）。
        var raw = new byte[height * (width * 4 + 1)];
        for (var y = 0; y < height; y++)
        {
            var row = y * (width * 4 + 1);
            for (var x = 0; x < width; x++)
            {
                var pixel = pixels[y * width + x];
                raw[row + 1 + x * 4] = (byte)(pixel >> 16);
                raw[row + 2 + x * 4] = (byte)(pixel >> 8);
                raw[row + 3 + x * 4] = (byte)pixel;
                raw[row + 4 + x * 4] = (byte)(pixel >> 24);
            }
        }

        using var output = new MemoryStream();
        output.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        Chunk(output, "IHDR", Header(width, height));
        Chunk(output, "IDAT", ZlibStored(raw));
        Chunk(output, "IEND", []);
        return output.ToArray();
    }

    private static byte[] Header(int width, int height)
    {
        var header = new byte[13];
        header[0] = (byte)(width >> 24);
        header[1] = (byte)(width >> 16);
        header[2] = (byte)(width >> 8);
        header[3] = (byte)width;
        header[4] = (byte)(height >> 24);
        header[5] = (byte)(height >> 16);
        header[6] = (byte)(height >> 8);
        header[7] = (byte)height;
        header[8] = 8;   // 位深
        header[9] = 6;   // 颜色类型 RGBA
        return header;
    }

    /// <summary>zlib 容器 + deflate 存储块：不压缩，但解码端拿到的是合法流。</summary>
    private static byte[] ZlibStored(byte[] data)
    {
        const int blockMax = 0xFFFF;
        var blocks = (data.Length + blockMax - 1) / blockMax;
        var body = new byte[2 + data.Length + blocks * 5 + 4];
        var at = 0;
        body[at++] = 0x78;
        body[at++] = 0x01;

        var offset = 0;
        while (true)
        {
            var remain = data.Length - offset;
            var length = Math.Min(remain, blockMax);
            var final = length >= remain ? 1 : 0;
            body[at++] = (byte)final;
            body[at++] = (byte)length;
            body[at++] = (byte)(length >> 8);
            body[at++] = (byte)~length;
            body[at++] = (byte)(~length >> 8);
            Buffer.BlockCopy(data, offset, body, at, length);
            at += length;
            offset += length;
            if (final != 0) break;
        }

        var adler = Adler32(data);
        body[at++] = (byte)(adler >> 24);
        body[at++] = (byte)(adler >> 16);
        body[at++] = (byte)(adler >> 8);
        body[at] = (byte)adler;
        return body;
    }

    private static void Chunk(Stream output, string type, byte[] data)
    {
        var length = new byte[]
        {
            (byte)(data.Length >> 24), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length,
        };
        output.Write(length);

        var body = new byte[4 + data.Length];
        Encoding.ASCII.GetBytes(type, 0, type.Length, body, 0);
        data.CopyTo(body, 4);
        output.Write(body);

        var crc = Crc32(body);
        output.Write([(byte)(crc >> 24), (byte)(crc >> 16), (byte)(crc >> 8), (byte)crc]);
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        for (var i = 0; i < data.Length; i++)
        {
            crc ^= data[i];
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ (0xEDB88320 & ~(uint)((crc & 1) - 1));
        }

        return ~crc;
    }

    private static uint Adler32(byte[] data)
    {
        uint a = 1, b = 0;
        for (var i = 0; i < data.Length; i++)
        {
            a = (a + data[i]) % 65521;
            b = (b + a) % 65521;
        }

        return (b << 16) | a;
    }
}
