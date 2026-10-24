using System.Reflection;
using Disarm;
using Il2CppInterop.Common.Attributes;
using Microsoft.Extensions.Logging;

namespace Il2CppInterop.Common.XrefScans;

public static class XrefScanner
{
    public static unsafe IEnumerable<XrefInstance> XrefScan(MethodBase methodBase)
    {
        var fieldValue = Il2CppInteropUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(methodBase)
            ?.GetValue(null);
        if (fieldValue == null) return Enumerable.Empty<XrefInstance>();

        CachedScanResultsAttribute? cachedAttribute = null;
        try
        {
            cachedAttribute = methodBase.GetCustomAttribute<CachedScanResultsAttribute>(false);
        }
        catch (Exception e)
        {
            Logger.Instance.LogWarning("Failed to get custom attribute for {TypeName}.{MethodName}: {Error}. Falling back to scanning", methodBase.DeclaringType!.FullName, methodBase.Name, e.Message);
        }

        if (cachedAttribute == null)
        {
            XrefScanUtil.CallMetadataInitForMethod(methodBase);

            return XrefScanImpl(DecoderForAddress(*(IntPtr*)(IntPtr)fieldValue));
        }

        if (cachedAttribute.XrefRangeStart == cachedAttribute.XrefRangeEnd)
            return Enumerable.Empty<XrefInstance>();

        XrefScanMethodDb.CallMetadataInitForMethod(cachedAttribute);

        return XrefScanMethodDb.CachedXrefScan(cachedAttribute).Where(it =>
            it.Type == XrefType.Method || XrefScannerManager.Impl.XrefGlobalClassFilter(it.Pointer));
    }

    public static IEnumerable<XrefInstance> UsedBy(MethodBase methodBase)
    {
        var cachedAttribute = methodBase.GetCustomAttribute<CachedScanResultsAttribute>(false);
        if (cachedAttribute == null || cachedAttribute.RefRangeStart == cachedAttribute.RefRangeEnd)
            return Enumerable.Empty<XrefInstance>();

        return XrefScanMethodDb.ListUsers(cachedAttribute);
    }

    internal static unsafe IEnumerable<Arm64Instruction> DecoderForAddress(IntPtr codeStart, int lengthLimit = 1000)
    {
        if (codeStart == IntPtr.Zero) throw new NullReferenceException(nameof(codeStart));

        return Disassembler.Disassemble((byte*)codeStart, lengthLimit, (ulong)codeStart, Disassembler.Options.IgnoreErrors);
    }

    internal static IEnumerable<XrefInstance> XrefScanImpl(IEnumerable<Arm64Instruction> instructions, bool skipClassCheck = false)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.MnemonicCategory == Arm64MnemonicCategory.Return)
                yield break;

            if (XrefScanUtilFinder.HasGroup(instruction.Mnemonic, "AArch64_GRP_JUMP") ||
                XrefScanUtilFinder.HasGroup(instruction.Mnemonic, "AArch64_GRP_CALL"))
            {
                // Register-indirect branches (BR/BLR) have no address to extract.
                if (instruction.Op0Kind != Arm64OperandKind.ImmediatePcRelative)
                    continue;
                var targetAddress = XrefScanUtilFinder.ExtractTargetAddress(in instruction);
                if (targetAddress != 0)
                    yield return new XrefInstance(XrefType.Method, (nint)targetAddress, (nint)instruction.Address);
                continue;
            }

            if (instruction.MnemonicCategory != Arm64MnemonicCategory.Move)
                continue;

            XrefInstance? result = null;
            try
            {
                if (instruction.Op1Kind == Arm64OperandKind.ImmediatePcRelative)
                {
                    var movTarget = (IntPtr)((long)instruction.Address + instruction.Op1Imm);
                    if (skipClassCheck || XrefScannerManager.Impl.XrefGlobalClassFilter(movTarget))
                        result = new XrefInstance(XrefType.Global, movTarget, (nint)instruction.Address);
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogError("{Error}", ex.ToString());
            }

            if (result != null)
                yield return result.Value;
        }
    }
}
