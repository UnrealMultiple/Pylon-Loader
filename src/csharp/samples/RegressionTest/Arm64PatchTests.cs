using Pylon.ModLoader;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Patching;

namespace RegressionTest;

internal static class Arm64PatchTests
{
    internal static void Run(ModLogger log)
    {
        try
        {
            if (System.Runtime.InteropServices.NativeLibrary.TryLoad("libil2cpp.so", out var imageHandle))
            {
                if (System.Runtime.InteropServices.NativeLibrary.TryGetExport(imageHandle, "il2cpp_domain_get", out var address) &&
                    CodeImage.TryQuery((ulong)address, out var image, out var status) &&
                    CodeSite.TryCreate((ulong)address, 4, out var nativeSite, out var siteDiagnostic) &&
                    nativeSite.TryRead(0, out var actualWord, out siteDiagnostic) &&
                    Arm64Decoder.TryDecode((ulong)address, actualWord, out var actualInstruction, out siteDiagnostic))
                    log.Info($"[arm64] live instruction read+AsmArm64 decode ok BuildID={image.BuildId} instruction={actualInstruction!.Assembly}");
                System.Runtime.InteropServices.NativeLibrary.Free(imageHandle);
            }
            using (var assembler = new Arm64PatchBuilder(0x1000))
            {
                assembler.B(0x1008);
                if (assembler.TryBuild(out var instructions, out var errors))
                    log.Info($"[arm64] typed assembler round-trip ok: {instructions[0].Assembly}");
                else log.Warn("[arm64] assembly failed: " + string.Join(";", errors));
            }
            var pc = 0x1000UL;
            var b = 0x14000002u; // b +8
            if (!CodeSite.TryDecode(pc, b, out var decoded, out var diagnostic))
            {
                log.Warn($"[arm64] decode B failed: {diagnostic.Status}");
                return;
            }

            if (decoded.Kind != Arm64InstructionKind.B || decoded.Target != pc + 8)
                log.Warn($"[arm64] decode B mismatch kind={decoded.Kind} target=0x{decoded.Target:x}");
            else
                log.Info($"[arm64] decode B ok target=0x{decoded.Target:x}");

            if (Arm64Disassembly.TryFormat(b, pc, out var text))
                log.Info($"[arm64] AsmArm64: {text}");
            else
                log.Warn("[arm64] AsmArm64 formatting unavailable");

            if (!CodeSite.TryPcRelative(pc, b, out var target, out diagnostic) || target != pc + 8)
                log.Warn($"[arm64] pc-relative B failed: {diagnostic.Status} target=0x{target:x}");
            else
                log.Info("[arm64] pc-relative target ok");

            if (CodeSite.TryDecode(0x1002, b, out _, out diagnostic) ||
                diagnostic.Status != PatchStatus.Unaligned)
                log.Warn($"[arm64] unaligned PC was accepted: {diagnostic.Status}");
            else
                log.Info("[arm64] invalid alignment rejected");
        }
        catch (System.Exception e)
        {
            log.Warn("[arm64] callback boundary test failed: " + e.Message);
        }

        // P1: generated-stub end-to-end test. Kept outside the legacy checks so a failure
        // here is reported with its own [p1] prefix.
        Arm64InterceptDeviceTests.Run(log);
    }
}
