using System.Buffers.Binary;
using AsmArm64;
using AsmInstruction = AsmArm64.Arm64Instruction;

namespace Pylon.ModLoader.Patching;

public readonly record struct RelocatedArm64Instruction(
    uint OriginalWord, ulong SiteAddress, string Strategy, IReadOnlyList<uint> Words, int CodeWords);

public static class Arm64Relocator
{
    private static readonly Arm64RegisterX[] X =
    [
        Arm64RegisterX.X0, Arm64RegisterX.X1, Arm64RegisterX.X2, Arm64RegisterX.X3,
        Arm64RegisterX.X4, Arm64RegisterX.X5, Arm64RegisterX.X6, Arm64RegisterX.X7,
        Arm64RegisterX.X8, Arm64RegisterX.X9, Arm64RegisterX.X10, Arm64RegisterX.X11,
        Arm64RegisterX.X12, Arm64RegisterX.X13, Arm64RegisterX.X14, Arm64RegisterX.X15,
        Arm64RegisterX.X16, Arm64RegisterX.X17, Arm64RegisterX.X18, Arm64RegisterX.X19,
        Arm64RegisterX.X20, Arm64RegisterX.X21, Arm64RegisterX.X22, Arm64RegisterX.X23,
        Arm64RegisterX.X24, Arm64RegisterX.X25, Arm64RegisterX.X26, Arm64RegisterX.X27,
        Arm64RegisterX.X28, Arm64RegisterX.X29, Arm64RegisterX.X30
    ];

    private static readonly Arm64RegisterW[] W =
    [
        Arm64RegisterW.W0, Arm64RegisterW.W1, Arm64RegisterW.W2, Arm64RegisterW.W3,
        Arm64RegisterW.W4, Arm64RegisterW.W5, Arm64RegisterW.W6, Arm64RegisterW.W7,
        Arm64RegisterW.W8, Arm64RegisterW.W9, Arm64RegisterW.W10, Arm64RegisterW.W11,
        Arm64RegisterW.W12, Arm64RegisterW.W13, Arm64RegisterW.W14, Arm64RegisterW.W15,
        Arm64RegisterW.W16, Arm64RegisterW.W17, Arm64RegisterW.W18, Arm64RegisterW.W19,
        Arm64RegisterW.W20, Arm64RegisterW.W21, Arm64RegisterW.W22, Arm64RegisterW.W23,
        Arm64RegisterW.W24, Arm64RegisterW.W25, Arm64RegisterW.W26, Arm64RegisterW.W27,
        Arm64RegisterW.W28, Arm64RegisterW.W29, Arm64RegisterW.W30
    ];

    public static bool TryRelocate(Arm64Assembler assembler, ulong stubBase, ulong siteAddress,
        uint word, byte[]? literalData, out RelocatedArm64Instruction relocated,
        out PatchDiagnostic diagnostic)
    {
        relocated = default;
        var before = (int)assembler.SizeInBytes;
        var targetWordIndex = -1;
        var codeWords = 0;
        string strategy;

        // A patched BTI landing pad is no longer a valid indirect-branch target.
        if (word is 0xD503241F or 0xD503245F or 0xD503249F or 0xD50324DF)
        {
            diagnostic = new(PatchStatus.UnsupportedInstruction, 0, siteAddress, 0, word);
            return false;
        }

        if ((word & 0x3B000000) == 0x18000000)
        {
            if (!TryInlineLiteral(assembler, stubBase, word, literalData, out var inlineWords))
            {
                diagnostic = new(PatchStatus.InvalidArgument, 0, siteAddress, 0, word);
                return false;
            }
            strategy = "literal-inline";
            codeWords = inlineWords;
        }
        else if ((word & 0xFC000000) == 0x14000000)
        {
            if (!TryPcTarget(siteAddress, word, out var target, out diagnostic)) return false;
            assembler.B(new Arm64Label(target));
            strategy = "b";
            targetWordIndex = 0;
        }
        else if ((word & 0xFC000000) == 0x94000000)
        {
            if (!TryPcTarget(siteAddress, word, out var target, out diagnostic)) return false;
            // Make the callee observe the architectural link address (site+4), not a
            // stub address. This also keeps the path unwind-friendly: the callee returns
            // directly to the patched method, never through the stub.
            LoadConstant(assembler, X[30], siteAddress + 4);
            assembler.B(new Arm64Label(target));
            strategy = "bl";
            targetWordIndex = 4;
        }
        else if ((word & 0xFF000000) == 0x54000000)
        {
            var condition = (word & 0x10) == 0 ? ConditionOf(word) : null;
            if (condition is null)
            {
                diagnostic = new(PatchStatus.UnsupportedInstruction, 0, siteAddress, 0, word);
                return false;
            }
            if (!TryPcTarget(siteAddress, word, out var target, out diagnostic)) return false;
            assembler.B(Invert(condition.Value), new Arm64Label(
                stubBase + assembler.SizeInBytes + 8));
            assembler.B(new Arm64Label(target));
            strategy = "b.cond";
            targetWordIndex = 1;
        }
        else if ((word & 0x7E000000) == 0x34000000)
        {
            var register = (int)(word & 0x1F);
            if (register == 31)
            {
                diagnostic = new(PatchStatus.UnsupportedInstruction, 0, siteAddress, 0, word);
                return false;
            }
            if (!TryPcTarget(siteAddress, word, out var target, out diagnostic)) return false;
            var originalTestsNonZero = (word & 0x01000000) != 0;
            var is64 = (word & 0x80000000) != 0;
            var skip = new Arm64Label(stubBase + assembler.SizeInBytes + 8);
            if (is64)
            {
                if (originalTestsNonZero) assembler.CBZ(X[register], skip);
                else assembler.CBNZ(X[register], skip);
            }
            else
            {
                if (originalTestsNonZero) assembler.CBZ(W[register], skip);
                else assembler.CBNZ(W[register], skip);
            }
            assembler.B(new Arm64Label(target));
            strategy = originalTestsNonZero ? "cbnz" : "cbz";
            targetWordIndex = 1;
        }
        else if ((word & 0x7E000000) == 0x36000000)
        {
            var register = (int)(word & 0x1F);
            if (register == 31)
            {
                diagnostic = new(PatchStatus.UnsupportedInstruction, 0, siteAddress, 0, word);
                return false;
            }
            if (!TryPcTarget(siteAddress, word, out var target, out diagnostic)) return false;
            var originalTestsZero = (word & 0x01000000) == 0;
            var is64 = (word & 0x80000000) != 0;
            var bit = (byte)((((word >> 31) & 1) << 5) | ((word >> 19) & 0x1F));
            var skip = new Arm64Label(stubBase + assembler.SizeInBytes + 8);
            if (is64)
            {
                if (originalTestsZero) assembler.TBNZ(X[register], bit, skip);
                else assembler.TBZ(X[register], bit, skip);
            }
            else
            {
                if (originalTestsZero) assembler.TBNZ(W[register], bit, skip);
                else assembler.TBZ(W[register], bit, skip);
            }
            assembler.B(new Arm64Label(target));
            strategy = originalTestsZero ? "tbz" : "tbnz";
            targetWordIndex = 1;
        }
        else if ((word & 0x9F000000) == 0x10000000)
        {
            var register = (int)(word & 0x1F);
            if (register == 31)
            {
                diagnostic = new(PatchStatus.UnsupportedInstruction, 0, siteAddress, 0, word);
                return false;
            }
            if (!TryPcTarget(siteAddress, word, out var target, out diagnostic)) return false;
            LoadConstant(assembler, X[register], target);
            strategy = "adr";
        }
        else if ((word & 0x9F000000) == 0x90000000)
        {
            var register = (int)(word & 0x1F);
            if (register == 31)
            {
                diagnostic = new(PatchStatus.UnsupportedInstruction, 0, siteAddress, 0, word);
                return false;
            }
            if (!TryPcTarget(siteAddress, word, out var target, out diagnostic)) return false;
            assembler.ADRP(X[register], new Arm64Label(target));
            strategy = "adrp";
            targetWordIndex = 0;
        }
        else
        {
            var decoded = AsmInstruction.Decode(word);
            if (decoded.Id == AsmArm64.Arm64InstructionId.Invalid)
            {
                diagnostic = new(PatchStatus.UnsupportedInstruction, 0, siteAddress, 0, word);
                return false;
            }
            if (decoded.Mnemonic == Arm64Mnemonic.BLR)
            {
                var register = (int)((word >> 5) & 0x1F);
                // BLR x30 cannot be made transparent: setting LR would destroy the
                // branch target. BLR xzr is not a usable branch target either.
                if (register is 30 or 31)
                {
                    diagnostic = new(PatchStatus.UnsupportedInstruction, 0, siteAddress, 0, word);
                    return false;
                }
                LoadConstant(assembler, X[30], siteAddress + 4);
                assembler.BR(X[register]);
                strategy = "blr";
            }
            else if (decoded.Mnemonic.ToString().StartsWith("BLR",
                         StringComparison.OrdinalIgnoreCase))
            {
                // BLRAA/BLRAB authenticate the target with an extra modifier register;
                // a generic LR rewrite is not equivalent. Fail closed.
                diagnostic = new(PatchStatus.UnsupportedInstruction, 0, siteAddress, 0, word);
                return false;
            }
            else
            {
                assembler.AppendUInt32(word);
                strategy = "verbatim";
            }
        }

        return Verify(assembler, stubBase, before, siteAddress, word, targetWordIndex,
            codeWords, strategy, out relocated, out diagnostic);
    }

    private static bool TryInlineLiteral(Arm64Assembler assembler, ulong stubBase, uint word,
        byte[]? literalData, out int codeWords)
    {
        codeWords = 0;
        if (literalData is null) return false;
        var opc = word >> 30;
        if (opc == 3) return false;
        var isVector = (word & 0x04000000) != 0;
        var register = (int)(word & 0x1F);
        var dataBytes = isVector
            ? (opc == 0 ? 4 : opc == 1 ? 8 : 16)
            : (opc == 1 ? 8 : 4);
        if (literalData.Length != dataBytes) return false;

        if (isVector)
        {
            // LDR S/D/Q (literal) reads directly from the inline data address. This
            // avoids a scratch GPR and, unlike the previous [SP] save slot, is safe
            // against asynchronous signal delivery.
            var codeEnd = stubBase + (ulong)assembler.SizeInBytes + 8;
            var dataStart = AlignUp(codeEnd, (ulong)dataBytes);
            if (opc == 0) assembler.LDR(S(register), new Arm64Label(dataStart));
            else if (opc == 1) assembler.LDR(D(register), new Arm64Label(dataStart));
            else assembler.LDR(Q(register), new Arm64Label(dataStart));
            assembler.B(new Arm64Label(dataStart + (ulong)dataBytes));
            var padding = (int)((dataStart - (stubBase + assembler.SizeInBytes)) / 4);
            for (var i = 0; i < padding; i++) assembler.AppendUInt32(0xD503201F);
            AppendData(assembler, literalData);
            codeWords = 2 + padding;
            return true;
        }

        if (register == 31) return false;
        var destination = X[register];
        var integerEnd = stubBase + (ulong)assembler.SizeInBytes + 12;
        var dataStartInteger = AlignUp(integerEnd, (ulong)dataBytes);
        assembler.ADR(destination, new Arm64Label(dataStartInteger));
        var integerAccessor = new Arm64ImmediateMemoryAccessor(destination, 0);
        if (opc == 2) assembler.LDRSW(destination, integerAccessor);
        else if (opc == 1) assembler.LDR(destination, integerAccessor);
        else assembler.LDR(W[register], integerAccessor);
        assembler.B(new Arm64Label(dataStartInteger + (ulong)dataBytes));
        var integerPadding = (int)((dataStartInteger - (stubBase + assembler.SizeInBytes)) / 4);
        for (var i = 0; i < integerPadding; i++) assembler.AppendUInt32(0xD503201F);
        AppendData(assembler, literalData);
        codeWords = 3 + integerPadding;
        return true;
    }

    private static void AppendData(Arm64Assembler assembler, byte[] data)
    {
        for (var offset = 0; offset < data.Length; offset += 4)
            assembler.AppendUInt32(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4)));
    }

    private static ulong AlignUp(ulong value, ulong alignment) =>
        (value + alignment - 1) & ~(alignment - 1);

    private static bool TryPcTarget(ulong address, uint word, out ulong target,
        out PatchDiagnostic diagnostic)
    {
        target = 0;
        if (!Arm64Decoder.TryDecode(address, word, out var decoded, out diagnostic) ||
            decoded!.PcTarget is null)
        {
            if (diagnostic.Status == PatchStatus.Ok)
                diagnostic = new(PatchStatus.UnsupportedInstruction, 0, address, 0, word);
            return false;
        }
        target = decoded.PcTarget.Value;
        return true;
    }

    private static Arm64ConditionalKind? ConditionOf(uint word)
    {
        var condition = (int)(word & 0xF);
        if (condition >= 0xE) return null;
        return condition switch
        {
            0 => Arm64ConditionalKind.EQ,
            1 => Arm64ConditionalKind.NE,
            2 => Arm64ConditionalKind.HS,
            3 => Arm64ConditionalKind.LO,
            4 => Arm64ConditionalKind.MI,
            5 => Arm64ConditionalKind.PL,
            6 => Arm64ConditionalKind.VS,
            7 => Arm64ConditionalKind.VC,
            8 => Arm64ConditionalKind.HI,
            9 => Arm64ConditionalKind.LS,
            10 => Arm64ConditionalKind.GE,
            11 => Arm64ConditionalKind.LT,
            12 => Arm64ConditionalKind.GT,
            _ => Arm64ConditionalKind.LE
        };
    }

    private static Arm64ConditionalKind Invert(Arm64ConditionalKind condition) => condition switch
    {
        Arm64ConditionalKind.EQ => Arm64ConditionalKind.NE,
        Arm64ConditionalKind.NE => Arm64ConditionalKind.EQ,
        Arm64ConditionalKind.HS => Arm64ConditionalKind.LO,
        Arm64ConditionalKind.LO => Arm64ConditionalKind.HS,
        Arm64ConditionalKind.MI => Arm64ConditionalKind.PL,
        Arm64ConditionalKind.PL => Arm64ConditionalKind.MI,
        Arm64ConditionalKind.VS => Arm64ConditionalKind.VC,
        Arm64ConditionalKind.VC => Arm64ConditionalKind.VS,
        Arm64ConditionalKind.HI => Arm64ConditionalKind.LS,
        Arm64ConditionalKind.LS => Arm64ConditionalKind.HI,
        Arm64ConditionalKind.GE => Arm64ConditionalKind.LT,
        Arm64ConditionalKind.LT => Arm64ConditionalKind.GE,
        Arm64ConditionalKind.GT => Arm64ConditionalKind.LE,
        _ => Arm64ConditionalKind.GT
    };

    private static void LoadConstant(Arm64Assembler assembler, Arm64RegisterX destination,
        ulong value)
    {
        assembler.MOVZ(destination, (ushort)(value & 0xFFFF));
        assembler.MOVK(destination, (ushort)((value >> 16) & 0xFFFF), default, 16);
        assembler.MOVK(destination, (ushort)((value >> 32) & 0xFFFF), default, 32);
        assembler.MOVK(destination, (ushort)((value >> 48) & 0xFFFF), default, 48);
    }

    private static bool Verify(Arm64Assembler assembler, ulong stubBase, int before,
        ulong siteAddress, uint original, int targetWordIndex, int codeWords,
        string strategy, out RelocatedArm64Instruction relocated, out PatchDiagnostic diagnostic)
    {
        var buffer = assembler.Buffer;
        var first = before / 4;
        var count = buffer.Length / 4 - first;
        if (codeWords == 0) codeWords = count;
        if (codeWords > count) codeWords = count;
        if (count < 1 || codeWords < 1)
        {
            diagnostic = new(PatchStatus.UnsupportedInstruction, 0, siteAddress, 0, original);
            relocated = default;
            return false;
        }

        var words = new uint[count];
        for (var i = 0; i < count; i++)
            words[i] = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice((first + i) * 4, 4));

        for (var i = 0; i < codeWords; i++)
        {
            if (AsmInstruction.Decode(words[i]).Id == AsmArm64.Arm64InstructionId.Invalid)
            {
                diagnostic = new(PatchStatus.UnsupportedInstruction, 0, siteAddress, 0, words[i]);
                relocated = default;
                return false;
            }
        }

        if (targetWordIndex >= 0 && targetWordIndex < count)
        {
            if (!TryPcTarget(siteAddress, original, out var expected, out _) ||
                !TryPcTarget(stubBase + (ulong)(before + targetWordIndex * 4),
                    words[targetWordIndex], out var actual, out _))
            {
                diagnostic = new(PatchStatus.UnsupportedInstruction, 0, siteAddress, 0, original);
                relocated = default;
                return false;
            }
            if (actual != expected)
            {
                diagnostic = new(PatchStatus.FingerprintMismatch, 0, siteAddress,
                    original, words[targetWordIndex]);
                relocated = default;
                return false;
            }
        }

        relocated = new(original, siteAddress, strategy, words, codeWords);
        diagnostic = new(PatchStatus.Ok, 0, siteAddress, 0, words[^1]);
        return true;
    }

    private static Arm64RegisterS S(int index) => index switch
    {
        0 => Arm64RegisterS.S0,
        1 => Arm64RegisterS.S1,
        2 => Arm64RegisterS.S2,
        3 => Arm64RegisterS.S3,
        4 => Arm64RegisterS.S4,
        5 => Arm64RegisterS.S5,
        6 => Arm64RegisterS.S6,
        7 => Arm64RegisterS.S7,
        8 => Arm64RegisterS.S8,
        9 => Arm64RegisterS.S9,
        10 => Arm64RegisterS.S10,
        11 => Arm64RegisterS.S11,
        12 => Arm64RegisterS.S12,
        13 => Arm64RegisterS.S13,
        14 => Arm64RegisterS.S14,
        15 => Arm64RegisterS.S15,
        16 => Arm64RegisterS.S16,
        17 => Arm64RegisterS.S17,
        18 => Arm64RegisterS.S18,
        19 => Arm64RegisterS.S19,
        20 => Arm64RegisterS.S20,
        21 => Arm64RegisterS.S21,
        22 => Arm64RegisterS.S22,
        23 => Arm64RegisterS.S23,
        24 => Arm64RegisterS.S24,
        25 => Arm64RegisterS.S25,
        26 => Arm64RegisterS.S26,
        27 => Arm64RegisterS.S27,
        28 => Arm64RegisterS.S28,
        29 => Arm64RegisterS.S29,
        30 => Arm64RegisterS.S30,
        _ => Arm64RegisterS.S31
    };

    private static Arm64RegisterD D(int index) => index switch
    {
        0 => Arm64RegisterD.D0,
        1 => Arm64RegisterD.D1,
        2 => Arm64RegisterD.D2,
        3 => Arm64RegisterD.D3,
        4 => Arm64RegisterD.D4,
        5 => Arm64RegisterD.D5,
        6 => Arm64RegisterD.D6,
        7 => Arm64RegisterD.D7,
        8 => Arm64RegisterD.D8,
        9 => Arm64RegisterD.D9,
        10 => Arm64RegisterD.D10,
        11 => Arm64RegisterD.D11,
        12 => Arm64RegisterD.D12,
        13 => Arm64RegisterD.D13,
        14 => Arm64RegisterD.D14,
        15 => Arm64RegisterD.D15,
        16 => Arm64RegisterD.D16,
        17 => Arm64RegisterD.D17,
        18 => Arm64RegisterD.D18,
        19 => Arm64RegisterD.D19,
        20 => Arm64RegisterD.D20,
        21 => Arm64RegisterD.D21,
        22 => Arm64RegisterD.D22,
        23 => Arm64RegisterD.D23,
        24 => Arm64RegisterD.D24,
        25 => Arm64RegisterD.D25,
        26 => Arm64RegisterD.D26,
        27 => Arm64RegisterD.D27,
        28 => Arm64RegisterD.D28,
        29 => Arm64RegisterD.D29,
        30 => Arm64RegisterD.D30,
        _ => Arm64RegisterD.D31
    };

    private static Arm64RegisterQ Q(int index) => index switch
    {
        0 => Arm64RegisterQ.Q0,
        1 => Arm64RegisterQ.Q1,
        2 => Arm64RegisterQ.Q2,
        3 => Arm64RegisterQ.Q3,
        4 => Arm64RegisterQ.Q4,
        5 => Arm64RegisterQ.Q5,
        6 => Arm64RegisterQ.Q6,
        7 => Arm64RegisterQ.Q7,
        8 => Arm64RegisterQ.Q8,
        9 => Arm64RegisterQ.Q9,
        10 => Arm64RegisterQ.Q10,
        11 => Arm64RegisterQ.Q11,
        12 => Arm64RegisterQ.Q12,
        13 => Arm64RegisterQ.Q13,
        14 => Arm64RegisterQ.Q14,
        15 => Arm64RegisterQ.Q15,
        16 => Arm64RegisterQ.Q16,
        17 => Arm64RegisterQ.Q17,
        18 => Arm64RegisterQ.Q18,
        19 => Arm64RegisterQ.Q19,
        20 => Arm64RegisterQ.Q20,
        21 => Arm64RegisterQ.Q21,
        22 => Arm64RegisterQ.Q22,
        23 => Arm64RegisterQ.Q23,
        24 => Arm64RegisterQ.Q24,
        25 => Arm64RegisterQ.Q25,
        26 => Arm64RegisterQ.Q26,
        27 => Arm64RegisterQ.Q27,
        28 => Arm64RegisterQ.Q28,
        29 => Arm64RegisterQ.Q29,
        30 => Arm64RegisterQ.Q30,
        _ => Arm64RegisterQ.Q31
    };
}
