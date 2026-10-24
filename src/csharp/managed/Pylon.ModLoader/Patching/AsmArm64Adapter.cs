using System.Buffers.Binary;

#if PYLON_ASM_ARM64
using AsmArm64;
#endif

namespace Pylon.ModLoader.Patching;
/// <summary>
/// Optional managed adapter for the vendored AsmArm64 decoder. Native CodeSite
/// validation remains authoritative; this class only provides rich text decoding.
/// </summary>
public static class Arm64Disassembly
{
    public static bool TryFormat(uint word, ulong pc, out string text)
    {
        text = string.Empty;
#if PYLON_ASM_ARM64
        try
        {
            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, word);
            text = Arm64Disassembler.DisassembleToString(bytes, pc).Trim();
            return text.Length != 0;
        }
        catch
        {
            text = string.Empty;
            return false;
        }
#else
        _ = word;
        _ = pc;
        return false;
#endif
    }
}
