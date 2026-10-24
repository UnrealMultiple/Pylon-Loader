using Disarm;
using Microsoft.Extensions.Logging;

namespace Il2CppInterop.Common.XrefScans;

internal static class XrefScanUtilFinder
{
    public static IntPtr FindLastRcxReadAddressBeforeCallTo(IntPtr codeStart, IntPtr callTarget)
    {
        IntPtr result = IntPtr.Zero;

        foreach (var instruction in XrefScanner.DecoderForAddress(codeStart))
        {
            if (instruction.MnemonicCategory == Arm64MnemonicCategory.Return)
                return IntPtr.Zero;

            if (HasGroup(instruction.Mnemonic, "AArch64_GRP_JUMP"))
                continue;

            if (HasGroup(instruction.Mnemonic, "AArch64_GRP_CALL") &&
                (IntPtr)(long)ExtractTargetAddress(in instruction) == callTarget)
            {
                return result;
            }

            if (instruction.MnemonicCategory == Arm64MnemonicCategory.Move &&
                instruction.Op0Kind == Arm64OperandKind.Register &&
                instruction.Op1Kind == Arm64OperandKind.ImmediatePcRelative)
            {
                result = (IntPtr)((long)instruction.Address + instruction.Op1Imm);
            }
        }

        return IntPtr.Zero;
    }

    public static IntPtr FindByteWriteTargetRightAfterCallTo(IntPtr codeStart, IntPtr callTarget)
    {
        var seenCall = false;

        foreach (var instruction in XrefScanner.DecoderForAddress(codeStart))
        {
            if (instruction.MnemonicCategory == Arm64MnemonicCategory.Return)
                return IntPtr.Zero;

            if (HasGroup(instruction.Mnemonic, "AArch64_GRP_JUMP"))
                continue;

            if (HasGroup(instruction.Mnemonic, "AArch64_GRP_CALL") &&
                (IntPtr)(long)ExtractTargetAddress(in instruction) == callTarget)
            {
                seenCall = true;
            }
            else if (seenCall &&
                     instruction.MnemonicCategory == Arm64MnemonicCategory.Move &&
                     instruction.Op0Kind == Arm64OperandKind.Register &&
                     instruction.Op1Kind == Arm64OperandKind.ImmediatePcRelative)
            {
                return (IntPtr)((long)instruction.Address + instruction.Op1Imm);
            }
        }

        return IntPtr.Zero;
    }

    public static ulong ExtractTargetAddress(in Arm64Instruction instruction)
    {
        if (instruction.Op0Kind == Arm64OperandKind.None)
        {
            Logger.Instance.LogInformation("Not enough operands to extract target address");
            return 0;
        }

        var lastOperand = -1;
        if (instruction.Op0Kind != Arm64OperandKind.None) lastOperand = 0;
        if (instruction.Op1Kind != Arm64OperandKind.None) lastOperand = 1;
        if (instruction.Op2Kind != Arm64OperandKind.None) lastOperand = 2;
        if (instruction.Op3Kind != Arm64OperandKind.None) lastOperand = 3;

        return lastOperand switch
        {
            0 => instruction.Address + (ulong)instruction.Op0Imm,
            1 => instruction.Address + (ulong)instruction.Op1Imm,
            2 => instruction.Address + (ulong)instruction.Op2Imm,
            3 => instruction.Address + (ulong)instruction.Op3Imm,
            _ => 0,
        };
    }

    public static bool HasGroup(Arm64Mnemonic mnemonic, string group)
    {
        return mnemonic switch
        {
            Arm64Mnemonic.B or Arm64Mnemonic.BC => group is "AArch64_GRP_JUMP" or "AArch64_GRP_BRANCH_RELATIVE",
            Arm64Mnemonic.BL => group is "AArch64_GRP_CALL" or "AArch64_GRP_BRANCH_RELATIVE",
            Arm64Mnemonic.BLR => group == "AArch64_GRP_CALL",
            Arm64Mnemonic.BR => group == "AArch64_GRP_JUMP",
            Arm64Mnemonic.CBNZ or Arm64Mnemonic.CBZ or Arm64Mnemonic.TBNZ or Arm64Mnemonic.TBZ => group is "AArch64_GRP_JUMP" or "AArch64_GRP_BRANCH_RELATIVE",
            _ => false,
        };
    }
}
