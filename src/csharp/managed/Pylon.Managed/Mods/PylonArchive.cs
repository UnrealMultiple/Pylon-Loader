using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Pylon.Managed.Mods;

/// <summary>
/// .pmod v2 自研容器（PYPM）的只读解析器。
///
/// 布局：
///   magic    "PYPM"（4 字节）
///   version  u32 = 2
///   count    u32
///   TOC 逐条：pathLen u16 + flags u8(bit0=deflate) + path(UTF-8) + rawSize u32 + dataOff u64 + storedLen u32
///   数据区：各条目内容依次排布
///
/// 使用模式是"读一次就关"：程序集字节喂给 AssemblyLoadContext 后立即 Dispose，
/// 不持有句柄也不缓存整包——见 ModEntry / ModPackageEntry。
/// </summary>
internal sealed class PylonArchive : IDisposable
{
    private const uint Magic = 0x4D505950; // "PYPM" little-endian
    private const uint FormatVersion = 2;
    private const byte FlagDeflate = 0x01;

    private readonly FileStream _stream;
    private readonly Dictionary<string, TocEntry> _entries;

    private PylonArchive(FileStream stream, Dictionary<string, TocEntry> entries)
    {
        _stream = stream;
        _entries = entries;
    }

    private readonly record struct TocEntry(byte Flags, uint RawSize, long DataOffset, uint StoredLength);

    public static bool IsArchive(string path)
    {
        Span<byte> header = stackalloc byte[4];
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4);
            stream.ReadExactly(header);
            return BinaryPrimitives.ReadUInt32LittleEndian(header) == Magic;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>打开并解析 TOC；格式不对抛 InvalidDataException（含旧 zip 的明确提示）。</summary>
    public static PylonArchive Open(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            Span<byte> header = stackalloc byte[8];
            stream.ReadExactly(header);
            var magic = BinaryPrimitives.ReadUInt32LittleEndian(header);
            if (magic != Magic)
                throw new InvalidDataException("pmod 格式不正确（v2 起为自研容器，zip 不再支持）：请重新打包");
            var version = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
            if (version != FormatVersion)
                throw new InvalidDataException($"pmod 版本不支持：{version}（当前 {FormatVersion}）");

            Span<byte> countBuf = stackalloc byte[4];
            stream.ReadExactly(countBuf);
            var count = BinaryPrimitives.ReadUInt32LittleEndian(countBuf);
            if (count > 4096) throw new InvalidDataException($"TOC 条目过多：{count}");

            var entries = new Dictionary<string, TocEntry>((int)count, StringComparer.Ordinal);
            var lenBuf = new byte[3];
            var meta = new byte[16];
            for (var i = 0; i < count; i++)
            {
                stream.ReadExactly(lenBuf);
                var pathLength = BinaryPrimitives.ReadUInt16LittleEndian(lenBuf);
                var flags = lenBuf[2];

                var pathBytes = new byte[pathLength];
                stream.ReadExactly(pathBytes);
                var entryPath = Encoding.UTF8.GetString(pathBytes);
                if (entryPath.Contains('\\') || entryPath.StartsWith('/') || entryPath.Split('/', StringSplitOptions.RemoveEmptyEntries) is { Length: 0 })
                    throw new InvalidDataException($"TOC 路径非法：{entryPath}");

                stream.ReadExactly(meta);
                var rawSize = BinaryPrimitives.ReadUInt32LittleEndian(meta);
                var dataOffset = BinaryPrimitives.ReadInt64LittleEndian(meta.AsSpan(4));
                var storedLength = BinaryPrimitives.ReadUInt32LittleEndian(meta.AsSpan(12));

                if (!entries.TryAdd(entryPath, new TocEntry(flags, rawSize, dataOffset, storedLength)))
                    throw new InvalidDataException("TOC 重复条目：" + entryPath);
            }

            return new PylonArchive(stream, entries);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public IReadOnlyCollection<string> EntryNames => _entries.Keys;

    public bool Contains(string path) => _entries.ContainsKey(path);

    /// <summary>读出一个条目的完整内容；解压在托管侧完成，读毕即可丢弃。</summary>
    public byte[] Read(string path)
    {
        if (!_entries.TryGetValue(path, out var entry)) throw new FileNotFoundException("包内没有该条目：" + path);
        var stored = new byte[entry.StoredLength];
        _stream.Seek(entry.DataOffset, SeekOrigin.Begin);
        _stream.ReadExactly(stored);

        if ((entry.Flags & FlagDeflate) == 0) return stored;

        var raw = new byte[entry.RawSize];
        using var source = new MemoryStream(stored, writable: false);
        using var deflate = new DeflateStream(source, CompressionMode.Decompress);
        var read = deflate.ReadAtLeast(raw, (int)entry.RawSize, throwOnEndOfStream: false);
        if (read != entry.RawSize) throw new InvalidDataException($"条目解压不完整：{path}（期望 {entry.RawSize}，得到 {read}）");
        return raw;
    }

    /// <summary>打开一个条目的只读流（解压惰性进行）；用完必须 Dispose。</summary>
    public Stream OpenRead(string path)
    {
        if (!_entries.TryGetValue(path, out var entry)) throw new FileNotFoundException("包内没有该条目：" + path);
        var stored = new byte[entry.StoredLength];
        _stream.Seek(entry.DataOffset, SeekOrigin.Begin);
        _stream.ReadExactly(stored);
        var source = new MemoryStream(stored, writable: false);
        if ((entry.Flags & FlagDeflate) == 0) return source;
        return new DeflateStream(source, CompressionMode.Decompress);
    }

    /// <summary>读 manifest.json 文本；缺失返回 null。</summary>
    public string? ReadManifestText() =>
        _entries.ContainsKey("manifest.json")
            ? Encoding.UTF8.GetString(Read("manifest.json"))
            : null;

    public void Dispose() => _stream.Dispose();
}