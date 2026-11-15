using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using AsmArm64;
using AsmInstruction = AsmArm64.Arm64Instruction;

namespace Pylon.ModLoader;

public readonly record struct InterceptStubPlan(
    byte[] StubBytes, uint SiteBranchWord, RelocatedArm64Instruction Relocation,
    IReadOnlyList<string> Disassembly);

public static class InterceptStub
{
    private const ushort FrameSize = (ushort)InterceptFrame.FrameBytes;

    public static bool TryBuild(ulong stubPage, ulong siteAddress, uint originalWord,
        ulong token, ulong callback, byte[]? literalData, out InterceptStubPlan plan,
        out IReadOnlyList<Arm64AssemblyDiagnostic> diagnostics)
    {
        plan = default;
        var errors = new List<Arm64AssemblyDiagnostic>();
        diagnostics = errors;
        if ((stubPage & 0xFFF) != 0 || (siteAddress & 3) != 0)
        {
            errors.Add(new("Unaligned", "Stub page and site must be aligned."));
            return false;
        }

        try
        {
            using var assembler = new Arm64Assembler(stubPage);

            assembler.SUB(Arm64RegisterSP.SP, Arm64RegisterSP.SP, FrameSize);
            for (var i = 0; i < 15; i++)
                assembler.STP(X(i * 2), X(i * 2 + 1),
                    new Arm64ImmediateMemoryAccessor(Arm64RegisterSP.SP, i * 16));
            assembler.STR(Arm64RegisterX.X30,
                new Arm64ImmediateMemoryAccessor(Arm64RegisterSP.SP, 0xF0));
            assembler.MRS(Arm64RegisterX.X9, Sys(Arm64SystemRegisterKnownId.NZCV));
            assembler.STR(Arm64RegisterX.X9,
                new Arm64ImmediateMemoryAccessor(Arm64RegisterSP.SP, InterceptFrame.NzcvOffset));
            assembler.MRS(Arm64RegisterX.X9, Sys(Arm64SystemRegisterKnownId.FPCR));
            assembler.STR(Arm64RegisterX.X9,
                new Arm64ImmediateMemoryAccessor(Arm64RegisterSP.SP, InterceptFrame.FpcrOffset));
            assembler.MRS(Arm64RegisterX.X9, Sys(Arm64SystemRegisterKnownId.FPSR));
            assembler.STR(Arm64RegisterX.X9,
                new Arm64ImmediateMemoryAccessor(Arm64RegisterSP.SP, InterceptFrame.FpsrOffset));
            for (var i = 0; i < 16; i++)
                assembler.STP(Q(i * 2), Q(i * 2 + 1), new Arm64ImmediateMemoryAccessor(
                    Arm64RegisterSP.SP, InterceptFrame.VectorOffset + i * 32));

            LoadConstant(assembler, Arm64RegisterX.X0, token);
            assembler.ADD(Arm64RegisterX.X1, Arm64RegisterSP.SP, (ushort)0);
            LoadConstant(assembler, Arm64RegisterX.X9, callback);
            assembler.BLR(Arm64RegisterX.X9);
            assembler.STR(Arm64RegisterW.W0, new Arm64ImmediateMemoryAccessor(
                Arm64RegisterSP.SP, InterceptFrame.DecisionOffset));

            assembler.LDR(Arm64RegisterX.X9, new Arm64ImmediateMemoryAccessor(
                Arm64RegisterSP.SP, InterceptFrame.FpcrOffset));
            assembler.MSR(Sys(Arm64SystemRegisterKnownId.FPCR), Arm64RegisterX.X9);
            assembler.LDR(Arm64RegisterX.X9, new Arm64ImmediateMemoryAccessor(
                Arm64RegisterSP.SP, InterceptFrame.FpsrOffset));
            assembler.MSR(Sys(Arm64SystemRegisterKnownId.FPSR), Arm64RegisterX.X9);
            for (var i = 0; i < 16; i++)
                assembler.LDP(Q(i * 2), Q(i * 2 + 1), new Arm64ImmediateMemoryAccessor(
                    Arm64RegisterSP.SP, InterceptFrame.VectorOffset + i * 32));
            for (var i = 0; i < 4; i++)
                assembler.LDP(X(i * 2), X(i * 2 + 1),
                    new Arm64ImmediateMemoryAccessor(Arm64RegisterSP.SP, i * 16));
            for (var i = 5; i < 15; i++)
                assembler.LDP(X(i * 2), X(i * 2 + 1),
                    new Arm64ImmediateMemoryAccessor(Arm64RegisterSP.SP, i * 16));
            assembler.LDR(Arm64RegisterW.W9, new Arm64ImmediateMemoryAccessor(
                Arm64RegisterSP.SP, InterceptFrame.DecisionOffset));
            assembler.LabelForward(out var skip, "skip");
            assembler.CBNZ(Arm64RegisterW.W9, skip);

            RestoreTail(assembler);
            var relocationStart = (int)(assembler.SizeInBytes / 4);
            if (!Arm64Relocator.TryRelocate(assembler, stubPage, siteAddress, originalWord,
                    literalData, out var relocation, out var relocationDiagnostic))
            {
                errors.Add(new("Relocation", $"{relocationDiagnostic.Status}"));
                return false;
            }
            assembler.B(new Arm64Label(siteAddress + 4));

            assembler.Label(skip, true);
            RestoreTail(assembler);
            assembler.B(new Arm64Label(siteAddress + 4));
            assembler.End();

            var buffer = assembler.Buffer;
            var words = new List<string>(buffer.Length / 4);
            var dataStart = relocationStart + relocation.CodeWords;
            var dataEnd = relocationStart + relocation.Words.Count;
            for (var i = 0; i < buffer.Length; i += 4)
            {
                var index = i / 4;
                var word = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(i, 4));
                if (index >= dataStart && index < dataEnd)
                {
                    words.Add($"0x{word:x8}");
                    continue;
                }
                var decoded = AsmInstruction.Decode(word);
                if (decoded.Id == AsmArm64.Arm64InstructionId.Invalid)
                {
                    errors.Add(new("Invalid", "Stub word does not decode."));
                    return false;
                }
                words.Add(decoded.ToString());
            }
            var bytes = buffer.ToArray();

            using var siteAssembler = new Arm64Assembler(siteAddress);
            siteAssembler.B(new Arm64Label(stubPage));
            siteAssembler.End();
            var siteBuffer = siteAssembler.Buffer;
            var siteWord = BinaryPrimitives.ReadUInt32LittleEndian(siteBuffer.Slice(0, 4));
            if (AsmInstruction.Decode(siteWord).Id == AsmArm64.Arm64InstructionId.Invalid)
            {
                errors.Add(new("Invalid", "Site branch does not decode."));
                return false;
            }

            plan = new(bytes, siteWord, relocation, words);
            return true;
        }
        catch (Arm64AssemblerException exception)
        {
            foreach (var diagnostic in exception.Diagnostics)
                errors.Add(new("Assembler", diagnostic.ToString()));
        }
        catch (Exception exception)
        {
            errors.Add(new("Assembler", exception.Message));
        }
        return false;
    }

    private static void RestoreTail(Arm64Assembler assembler)
    {
        assembler.LDR(Arm64RegisterX.X9, new Arm64ImmediateMemoryAccessor(
            Arm64RegisterSP.SP, InterceptFrame.NzcvOffset));
        assembler.MSR(Sys(Arm64SystemRegisterKnownId.NZCV), Arm64RegisterX.X9);
        assembler.LDP(Arm64RegisterX.X8, Arm64RegisterX.X9,
            new Arm64ImmediateMemoryAccessor(Arm64RegisterSP.SP, 0x40));
        assembler.LDR(Arm64RegisterX.X30, new Arm64ImmediateMemoryAccessor(
            Arm64RegisterSP.SP, 0xF0));
        assembler.ADD(Arm64RegisterSP.SP, Arm64RegisterSP.SP, FrameSize);
    }

    private static Arm64RegisterX X(int index) => index switch
    {
        0 => Arm64RegisterX.X0, 1 => Arm64RegisterX.X1, 2 => Arm64RegisterX.X2,
        3 => Arm64RegisterX.X3, 4 => Arm64RegisterX.X4, 5 => Arm64RegisterX.X5,
        6 => Arm64RegisterX.X6, 7 => Arm64RegisterX.X7, 8 => Arm64RegisterX.X8,
        9 => Arm64RegisterX.X9, 10 => Arm64RegisterX.X10, 11 => Arm64RegisterX.X11,
        12 => Arm64RegisterX.X12, 13 => Arm64RegisterX.X13, 14 => Arm64RegisterX.X14,
        15 => Arm64RegisterX.X15, 16 => Arm64RegisterX.X16, 17 => Arm64RegisterX.X17,
        18 => Arm64RegisterX.X18, 19 => Arm64RegisterX.X19, 20 => Arm64RegisterX.X20,
        21 => Arm64RegisterX.X21, 22 => Arm64RegisterX.X22, 23 => Arm64RegisterX.X23,
        24 => Arm64RegisterX.X24, 25 => Arm64RegisterX.X25, 26 => Arm64RegisterX.X26,
        27 => Arm64RegisterX.X27, 28 => Arm64RegisterX.X28, 29 => Arm64RegisterX.X29,
        _ => Arm64RegisterX.X30
    };

    private static Arm64RegisterQ Q(int index) => index switch
    {
        0 => Arm64RegisterQ.Q0, 1 => Arm64RegisterQ.Q1, 2 => Arm64RegisterQ.Q2,
        3 => Arm64RegisterQ.Q3, 4 => Arm64RegisterQ.Q4, 5 => Arm64RegisterQ.Q5,
        6 => Arm64RegisterQ.Q6, 7 => Arm64RegisterQ.Q7, 8 => Arm64RegisterQ.Q8,
        9 => Arm64RegisterQ.Q9, 10 => Arm64RegisterQ.Q10, 11 => Arm64RegisterQ.Q11,
        12 => Arm64RegisterQ.Q12, 13 => Arm64RegisterQ.Q13, 14 => Arm64RegisterQ.Q14,
        15 => Arm64RegisterQ.Q15, 16 => Arm64RegisterQ.Q16, 17 => Arm64RegisterQ.Q17,
        18 => Arm64RegisterQ.Q18, 19 => Arm64RegisterQ.Q19, 20 => Arm64RegisterQ.Q20,
        21 => Arm64RegisterQ.Q21, 22 => Arm64RegisterQ.Q22, 23 => Arm64RegisterQ.Q23,
        24 => Arm64RegisterQ.Q24, 25 => Arm64RegisterQ.Q25, 26 => Arm64RegisterQ.Q26,
        27 => Arm64RegisterQ.Q27, 28 => Arm64RegisterQ.Q28, 29 => Arm64RegisterQ.Q29,
        30 => Arm64RegisterQ.Q30, _ => Arm64RegisterQ.Q31
    };

    private static Arm64SystemRegister Sys(Arm64SystemRegisterKnownId id) => (Arm64SystemRegister)id;

    private static void LoadConstant(Arm64Assembler assembler, Arm64RegisterX destination,
        ulong value)
    {
        assembler.MOVZ(destination, (ushort)(value & 0xFFFF));
        assembler.MOVK(destination, (ushort)((value >> 16) & 0xFFFF), default, 16);
        assembler.MOVK(destination, (ushort)((value >> 32) & 0xFFFF), default, 32);
        assembler.MOVK(destination, (ushort)((value >> 48) & 0xFFFF), default, 48);
    }
}
