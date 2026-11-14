using AsmArm64;
using Pylon.ModLoader;
using static AsmArm64.Arm64RegisterX;
using static AsmArm64.Arm64ConditionalKind;

var checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    checks++;
    Console.WriteLine("PASS " + name);
}

(uint Word, ulong Pc, ulong Target, Arm64Mnemonic Mnemonic)[] vectors =
[
    (0x14000002, 0x4000, 0x4008, Arm64Mnemonic.B),
    (0x97ffffff, 0x4000, 0x3ffc, Arm64Mnemonic.BL),
    (0x54ffffe0, 0x4000, 0x3ffc, Arm64Mnemonic.B),
    (0xb4000040, 0x4000, 0x4008, Arm64Mnemonic.CBZ),
    (0xb5000040, 0x4000, 0x4008, Arm64Mnemonic.CBNZ),
    (0x3607ffe0, 0x4000, 0x3ffc, Arm64Mnemonic.TBZ),
    (0x3707ffe0, 0x4000, 0x3ffc, Arm64Mnemonic.TBNZ),
    (0x10000020, 0x4000, 0x4004, Arm64Mnemonic.ADR),
    (0x70ffffe0, 0x4000, 0x3fff, Arm64Mnemonic.ADR),
    (0x90000020, 0x4ffc, 0x8000, Arm64Mnemonic.ADRP),
    (0xf0ffffe0, 0x4ffc, 0x3000, Arm64Mnemonic.ADRP),
    (0x58000040, 0x4000, 0x4008, Arm64Mnemonic.LDR)
];
foreach (var vector in vectors)
{
    Check(Arm64Decoder.TryDecode(vector.Pc, vector.Word, out var decoded, out _), "decode " + vector.Mnemonic);
    Check(decoded!.Mnemonic == vector.Mnemonic && decoded.PcTarget == vector.Target,
        $"{vector.Mnemonic} target 0x{vector.Target:x}");
}
Check(!Arm64Decoder.TryDecode(0x4001, vectors[0].Word, out _, out var alignment) &&
    alignment.Status == PatchStatus.Unaligned, "unaligned instruction boundary");
Check(!Arm64Decoder.TryDecode(0, 0x17ffffff, out _, out var overflow) &&
    overflow.Status == PatchStatus.OutOfBounds, "negative target overflow");
Check(!Arm64Decoder.TryDecode(0x4000, uint.MaxValue, out _, out var invalid) &&
    invalid.Status == PatchStatus.UnsupportedInstruction, "invalid encoding rejected");
using (var assembly = new Arm64PatchBuilder(0x4000))
{
    assembly.B(0x4010).BL(0x4020).B(EQ, 0x4000).ADR(X0, 0x4040).ADRP(X1, 0x8000)
        .MOVZ(X2, 7).MOV(X3, X2).ADD(X4, X2, X3).SUB(X4, X4, X2).CMP(X4, X2).RET();
    Check(assembly.TryBuild(out var plan, out var errors), "strongly typed assembler: " + string.Join(';', errors));
    Check(plan.Count == 11, "instruction count");
    foreach (var instruction in plan)
        Check(Arm64Decoder.TryDecode(instruction.Address, instruction.Word, out _, out _), "round trip " + instruction.Assembly);
}
using (var assembly = new Arm64PatchBuilder(0x4000))
{
    assembly.B(0x8000000000);
    Check(!assembly.TryBuild(out _, out var errors) && errors.Count != 0, "assembly failure is structured");
}
var profile = new CodeSiteProfile("build", "test", 0x4000, 0x100, 0, 8, "test", "site",
    new uint[] { 0xd503201f, 0xd65f03c0 }, new byte[] { 0x1f, 0x20, 0x03, 0xd5, 0xc0, 0x03, 0x5f, 0xd6 });
Check(CodeSite.TryValidateProfile(profile, "build", out _), "complete profile");
Check(!CodeSite.TryValidateProfile(profile, "other", out var build) && build.Status == PatchStatus.BuildMismatch, "BuildID mismatch");
Check(!CodeSite.TryValidateProfile(profile with { ByteOffset = 4 }, "build", out var bounds) && bounds.Status == PatchStatus.OutOfBounds, "method range");
Check(!CodeSite.TryValidateProfile(profile with { ByteOffset = 1 }, "build", out var unaligned) && unaligned.Status == PatchStatus.Unaligned, "site alignment");
Check(!CodeSite.TryValidateProfile(profile with { ExpectedFingerprint = new byte[8] }, "build", out var fingerprint) && fingerprint.Status == PatchStatus.FingerprintMismatch, "complete fingerprint mismatch");
Check(!CodeSite.TryValidateProfile(profile with { MethodAddress = ulong.MaxValue - 3 }, "build", out var wrap) && wrap.Status == PatchStatus.OutOfBounds, "method end overflow");
using (var replacement = new Arm64PatchBuilder(profile.SiteAddress))
{
    replacement.RET();
    Check(Arm64PatchTransaction.TryPlan(profile, "build", replacement, out var transaction, out _), "single instruction plan");
    Check(transaction!.Preview.Contains("ret") && transaction.Preview.Contains("siteRva=0x100"), "assembly plan preview");
}
using (var replacement = new Arm64PatchBuilder(profile.SiteAddress))
{
    replacement.RET().NOP();
    Check(!Arm64PatchTransaction.TryPlan(profile, "build", replacement, out _, out _), "multiword plan rejected");
}
Check(System.Runtime.InteropServices.Marshal.SizeOf<Pylon.NaviteAPI.Host.NativePatchRequestV2>() == 120, "request ABI layout");
Check(System.Runtime.InteropServices.Marshal.SizeOf<Pylon.NaviteAPI.Host.NativePatchDiagnosticV2>() == 64, "diagnostic ABI layout");
Console.WriteLine($"ALL {checks} CHECKS PASSED");
