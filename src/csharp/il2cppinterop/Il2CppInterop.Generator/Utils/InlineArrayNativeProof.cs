using System.Buffers.Binary;
using AsmResolver.DotNet;
using Il2CppInterop.Generator.Extensions;

namespace Il2CppInterop.Generator.Utils;

/// <summary>Fail-closed recognizer for small ARM64 accessor bodies, not a disassembler or a name allowlist.</summary>
internal static class InlineArrayNativeProof
{
    public static bool Verify(string? path, MethodDefinition getter, MethodDefinition? setter,
        MethodDefinition? length, InlineArrayLayout layout)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
        using var image = File.OpenRead(path);
        Span<byte> header = stackalloc byte[20];
        if (image.Read(header) != header.Length || header[0] != 0x7f || header[1] != 'E' ||
            header[2] != 'L' || header[3] != 'F' || header[4] != 2 || header[5] != 1 ||
            BinaryPrimitives.ReadUInt16LittleEndian(header[18..]) != 183) return false;
        uint[] Read(MethodDefinition? method, int words)
        {
            if (method is null) return [];
            var offset = method.ExtractOffset();
            if (offset <= 0 || offset > image.Length - words * 4L) return [];
            image.Position = offset;
            var bytes = new byte[words * 4]; image.ReadExactly(bytes);
            return Enumerable.Range(0, words).Select(i => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i * 4))).ToArray();
        }
        if (layout.LengthOffset == 0)
        {
            // add x8,x0,w1,sxtw #log2(stride); add x0,x8,#base; ret
            // The metadata return type must already be ref element, and every inline field was checked.
            if (layout.Stride is not (1 or 2 or 4 or 8 or 16)) return false;
            var shift = System.Numerics.BitOperations.Log2((uint)layout.Stride);
            return Read(getter, 3).SequenceEqual(new uint[] {
                0x8b21c008u | (uint)shift << 10,
                0x91000100u | (uint)layout.BaseOffset << 10, 0xd65f03c0 });
        }
        if (layout.ElementType.FullName != "System.Single" || layout.Stride != 4 || layout.BaseOffset != 0)
            return false;
        // Verify actual native length, not a capacity inferred only from homogeneous fields.
        if (!Read(length, 2).SequenceEqual(new uint[] { 0x52800000u | (uint)layout.Capacity << 5, 0xd65f03c0 }))
            return false;
        if (layout.Capacity == 2)
            return Read(getter, 4).SequenceEqual(new uint[] { 0x7100003f, 0x1a9f07e8, 0xbc685800, 0xd65f03c0 }) &&
                   Read(setter, 4).SequenceEqual(new uint[] { 0x7100003f, 0x1a9f07e8, 0xbc285800, 0xd65f03c0 });
        if (layout.Capacity == 3)
        {
            uint[] prefix = [0x7100083f, 0x540000a0, 0x7100043f, 0x54000081,
                0x91001000, 0x14000002, 0x91002000];
            return Read(getter, 9).SequenceEqual(prefix.Concat(new uint[] { 0xbd400000, 0xd65f03c0 })) &&
                   Read(setter, 9).SequenceEqual(prefix.Concat(new uint[] { 0xbd000000, 0xd65f03c0 }));
        }
        return false;
    }
}
