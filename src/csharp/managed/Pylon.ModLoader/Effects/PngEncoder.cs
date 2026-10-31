using System.IO;
using System.IO.Compression;
using System.Text;

namespace Pylon.ModLoader.Effects;

/// <summary>
/// 最小 PNG 编码器（RGBA8，无交错），只给 SaveNeutralLut 用：
/// 游戏的 ImageConversion.EncodeToPNG 在代理程序集里不可用，而这里不需要真的建一张 Unity 纹理。
/// 纯 BCL，无第三方依赖。
/// </summary>
internal static class PngEncoder
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>RGBA（自上而下）→ PNG 字节。</summary>
    public static byte[] Encode(int width, int height, byte[] rgba)
    {
        if (width <= 0 || height <= 0 || rgba is null || rgba.Length != width * height * 4)
            throw new ArgumentException("PNG 输入尺寸与数据不符");

        // 先独立压缩 IDAT 数据（zlib：头 + deflate + adler32）
        var row = width * 4;
        var raw = new byte[height * (row + 1)];
        for (var y = 0; y < height; y++)
        {
            raw[y * (row + 1)] = 0;  // filter: None
            Buffer.BlockCopy(rgba, y * row, raw, y * (row + 1) + 1, row);
        }

        byte[] idatData;
        using (var zms = new MemoryStream())
        {
            using (var z = new ZLibStream(zms, CompressionLevel.Fastest, leaveOpen: true))
                z.Write(raw, 0, raw.Length);
            idatData = zms.ToArray();
        }

        var ihdr = new byte[13];
        WriteBe32(ihdr, 0, (uint)width);
        WriteBe32(ihdr, 4, (uint)height);
        ihdr[8] = 8;   // bit depth
        ihdr[9] = 6;   // color type RGBA
        ihdr[10] = 0;  // compression
        ihdr[11] = 0;  // filter
        ihdr[12] = 0;  // interlace

        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);
        WriteChunk(ms, "IHDR", ihdr);
        WriteChunk(ms, "IDAT", idatData);
        WriteChunk(ms, "IEND", []);
        return ms.ToArray();
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        var length = BitConverter.GetBytes((uint)data.Length);
        Array.Reverse(length);
        s.Write(length, 0, 4);

        var typeBytes = Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes, 0, 4);
        s.Write(data, 0, data.Length);

        // CRC32(type + data)：初始 0xFFFFFFFF，结束时取反
        var crc = 0xFFFFFFFFu;
        foreach (var b in typeBytes) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        foreach (var b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        crc = ~crc;
        var crcBytes = BitConverter.GetBytes(crc);
        Array.Reverse(crcBytes);
        s.Write(crcBytes, 0, 4);
    }

    private static void WriteBe32(byte[] dst, int offset, uint value)
    {
        dst[offset] = (byte)(value >> 24);
        dst[offset + 1] = (byte)(value >> 16);
        dst[offset + 2] = (byte)(value >> 8);
        dst[offset + 3] = (byte)value;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }
}
