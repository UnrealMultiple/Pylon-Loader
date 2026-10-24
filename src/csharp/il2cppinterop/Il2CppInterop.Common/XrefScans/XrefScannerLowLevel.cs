using System.Collections.Generic;
using Disarm;

namespace Il2CppInterop.Common.XrefScans;

public static class XrefScannerLowLevel
{
    public static IEnumerable<IntPtr> JumpTargets(IntPtr codeStart, bool ignoreRetn = false, int instructions = 250)
    {
        // An AArch64 function does not necessarily end in RET. A tail call ends
        // in an unconditional B, and a far export can be a veneer (LDR/BR), so a
        // RET-only stop rule decodes straight through into whatever function
        // follows and hands back its branches as well. Upstream's x86 decoder
        // stopped at the first unconditional branch for exactly that reason;
        // without that rule the .Single() call sites throw "Sequence contains
        // more than one element" and the ones that pick silently can aim a
        // hook at a neighbouring function.
        var firstFlowControl = true;

        foreach (var instruction in XrefScanner.DecoderForAddress(codeStart, instructions * 4))
        {
            // Any return ends the function, including the PAC forms RETAA/RETAB.
            if (instruction.MnemonicCategory == Arm64MnemonicCategory.Return && !ignoreRetn)
                yield break;

            // Only direct PC-relative branches have a statically known target.
            // BLR/BR are register-indirect; interpreting a register operand as
            // a displacement returns the instruction's own address and may
            // install a hook into the wrong function (native SIGSEGV).
            if ((instruction.Mnemonic is Arm64Mnemonic.B or Arm64Mnemonic.BC or Arm64Mnemonic.BL) &&
                instruction.Op0Kind == Arm64OperandKind.ImmediatePcRelative &&
                instruction.MnemonicConditionCode == Arm64ConditionCode.NONE &&
                instruction.FinalOpConditionCode == Arm64ConditionCode.NONE)
            {
                var target = XrefScanUtilFinder.ExtractTargetAddress(in instruction);
                if (target != 0)
                {
                    yield return (IntPtr)(long)target;

                    // Tail call: this branch is the end of the function, so the
                    // branches that follow belong to the next one.
                    if (firstFlowControl && instruction.Mnemonic == Arm64Mnemonic.B)
                        yield break;
                }
            }

            if (XrefScanUtilFinder.HasGroup(instruction.Mnemonic, "AArch64_GRP_JUMP") ||
                XrefScanUtilFinder.HasGroup(instruction.Mnemonic, "AArch64_GRP_CALL"))
                firstFlowControl = false;
        }
    }
}
