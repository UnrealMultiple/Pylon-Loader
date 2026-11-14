using AsmArm64;
using Instruction = AsmArm64.Arm64Instruction;

namespace Pylon.ModLoader;

public readonly record struct Arm64OperandInfo(Arm64OperandKind Kind, string Text, long? Immediate);

/// <summary>AsmArm64 owns instruction and operand decoding; no opcode-mask classifier.</summary>
public sealed record DecodedArm64Instruction(
    ulong Address, uint Word, Arm64InstructionId Id, Arm64Mnemonic Mnemonic,
    string Assembly, IReadOnlyList<Arm64OperandInfo> Operands,
    long? PcDisplacement, ulong? PcTarget);

public static class Arm64Decoder
{
    public static bool TryDecode(ulong address, uint word, out DecodedArm64Instruction? decoded,
        out PatchDiagnostic diagnostic)
    {
        decoded = null;
        diagnostic = new(PatchStatus.Unaligned, 0, address, 0, word);
        if ((address & 3) != 0) return false;
        var instruction = Instruction.Decode(word);
        diagnostic = new(PatchStatus.UnsupportedInstruction, 0, address, 0, word);
        if (instruction.Id == Arm64InstructionId.Invalid) return false;
        var operands = new List<Arm64OperandInfo>(instruction.OperandCount);
        long? displacement = null;
        ulong? target = null;
        for (var i = 0; i < instruction.OperandCount; i++)
        {
            var operand = instruction.GetOperand(i);
            long? immediate = null;
            if (operand.Kind == Arm64OperandKind.Immediate)
                immediate = ((Arm64ImmediateOperand)operand).Value;
            if (operand.Kind == Arm64OperandKind.Label)
            {
                displacement = ((Arm64LabelOperand)operand).Offset;
                var origin = instruction.Mnemonic == Arm64Mnemonic.ADRP ? address & ~4095UL : address;
                if (!TryAdd(origin, displacement.Value, out var absolute))
                {
                    diagnostic = new(PatchStatus.OutOfBounds, 0, address, 0, word);
                    return false;
                }
                target = absolute;
            }
            operands.Add(new(operand.Kind, operand.ToString(), immediate));
        }
        decoded = new(address, word, instruction.Id, instruction.Mnemonic, instruction.ToString(),
            operands.AsReadOnly(), displacement, target);
        diagnostic = new(PatchStatus.Ok, 0, address, 0, word);
        return true;
    }

    private static bool TryAdd(ulong origin, long offset, out ulong target)
    {
        target = 0;
        if (offset < 0)
        {
            var magnitude = (ulong)(-(offset + 1)) + 1;
            if (magnitude > origin) return false;
            target = origin - magnitude;
        }
        else
        {
            if ((ulong)offset > ulong.MaxValue - origin) return false;
            target = origin + (ulong)offset;
        }
        return true;
    }
}
