using AsmArm64;
using Pylon.ModLoader;
using Pylon.ModLoader.Patching;
using static AsmArm64.Arm64RegisterX;
using static AsmArm64.Arm64ConditionalKind;
using AsmInstruction = AsmArm64.Arm64Instruction;

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
Check(CodeSite.TryValidateProfile(profile with { ByteOffset = 4 }, "build", out _),
    "site inside a full-method fingerprint");
Check(!CodeSite.TryValidateProfile(profile, "other", out var build) && build.Status == PatchStatus.BuildMismatch, "BuildID mismatch");
Check(!CodeSite.TryValidateProfile(profile with { ByteOffset = 8 }, "build", out var bounds) && bounds.Status == PatchStatus.OutOfBounds, "method range");
Check(!CodeSite.TryValidateProfile(profile with { ByteOffset = 1 }, "build", out var unaligned) && unaligned.Status == PatchStatus.Unaligned, "site alignment");
Check(!CodeSite.TryValidateProfile(profile with { ExpectedFingerprint = new byte[8] }, "build", out var fingerprint) && fingerprint.Status == PatchStatus.FingerprintMismatch, "complete fingerprint mismatch");
Check(!CodeSite.TryValidateProfile(profile with { MethodAddress = ulong.MaxValue - 3 }, "build", out var wrap) && wrap.Status == PatchStatus.OutOfBounds, "method end overflow");
using (var replacement = new Arm64PatchBuilder(profile.SiteAddress))
{
    replacement.RET();
    Check(Arm64PatchTransaction.TryPlan(profile, "build", replacement, out var transaction, out _), "single instruction plan");
    Check(transaction!.Preview.Contains("ret") && transaction.Preview.Contains("siteRva=0x100"), "assembly plan preview");
}
using (var replacement = new Arm64PatchBuilder(profile.SiteAddress + 4))
{
    replacement.NOP();
    Check(Arm64PatchTransaction.TryPlan(profile with { ByteOffset = 4 }, "build", replacement, out _, out _),
        "site inside a full-method fingerprint plans");
}
using (var replacement = new Arm64PatchBuilder(profile.SiteAddress))
{
    replacement.RET().NOP();
    Check(!Arm64PatchTransaction.TryPlan(profile, "build", replacement, out _, out _), "multiword plan rejected");
}
Check(System.Runtime.InteropServices.Marshal.SizeOf<Pylon.NaviteAPI.Host.NativePatchRequestV2>() == 120, "request ABI layout");
Check(System.Runtime.InteropServices.Marshal.SizeOf<Pylon.NaviteAPI.Host.NativePatchDiagnosticV2>() == 64, "diagnostic ABI layout");

const ulong relaySite = 0x7000_1000;
const ulong relayStub = relaySite + 0x640_0000;

static bool Relay(uint word, ulong stubBase, out RelocatedArm64Instruction relocated,
    out PatchDiagnostic diagnostic, byte[]? literal = null)
{
    using var assembler = new Arm64Assembler(stubBase);
    return Arm64Relocator.TryRelocate(assembler, stubBase, relaySite, word, literal,
        out relocated, out diagnostic);
}

static string MnemonicOf(uint word) =>
    AsmInstruction.Decode(word).Mnemonic.ToString().ToLowerInvariant();

foreach (var verbatim in new[] { 0xd503201fu, 0x8b030041u, 0xf9400820u, 0xd65f03c0u, 0x910003fdu })
{
    Check(Relay(verbatim, relayStub, out var copy, out _) &&
          copy.Strategy == "verbatim" && copy.Words.Count == 1 && copy.Words[0] == verbatim,
        $"verbatim 0x{verbatim:x8}");
}
Check(Relay(0x14000002, relayStub, out var branch, out _) &&
      branch.Strategy == "b" && branch.Words.Count == 1 &&
      AsmInstruction.Decode(branch.Words[0]).Mnemonic == Arm64Mnemonic.B,
    "b re-encoded");
Check(Relay(0x97ffffff, relayStub, out var link, out _) &&
      link.Strategy == "bl" && link.Words.Count == 5 && link.CodeWords == 5 &&
      AsmInstruction.Decode(link.Words[4]).Mnemonic == Arm64Mnemonic.B,
    "bl becomes mov lr + b");
Check((MnemonicOf(link.Words[0]) is "movz" or "mov") && MnemonicOf(link.Words[1]) == "movk" &&
      MnemonicOf(link.Words[2]) == "movk" && MnemonicOf(link.Words[3]) == "movk" &&
      ((link.Words[0] >> 5) & 0xFFFF) == (ushort)((relaySite + 4) & 0xFFFF),
    "bl sets LR=site+4 before branching: " + string.Join(',', link.Words.Select(MnemonicOf)));
Check(Relay(0xD63F0120, relayStub, out var blr, out _) &&
      blr.Strategy == "blr" && blr.Words.Count == 5 &&
      AsmInstruction.Decode(blr.Words[4]).Mnemonic == Arm64Mnemonic.BR,
    "blr x9 becomes mov lr + br x9");
Check(!Relay(0xD63F03C0, relayStub, out _, out var blrX30) &&
      blrX30.Status == PatchStatus.UnsupportedInstruction, "blr x30 rejected (LR is the target)");
Check(Relay(0x54ffffe0, relayStub, out var cond, out _) &&
      cond.Strategy == "b.cond" && cond.Words.Count == 2,
    "b.cond far form is 2 instructions");
Check(AsmInstruction.Decode(cond.Words[0]).ToString().Contains("b.ne", StringComparison.OrdinalIgnoreCase),
    "b.eq inverted to b.ne");
Check(!Relay(0x54000010, relayStub, out _, out var bc) &&
      bc.Status == PatchStatus.UnsupportedInstruction, "bc.cond rejected");
Check(!Relay(0xD503245F, relayStub, out _, out var bti) &&
      bti.Status == PatchStatus.UnsupportedInstruction, "bti landing pad rejected");
using (var lowAssembler = new Arm64Assembler(0x10000))
{
    Check(!Arm64Relocator.TryRelocate(lowAssembler, 0x10000, 0x1000, 0x17FFF800, null,
            out _, out var underflow) && underflow.Status == PatchStatus.OutOfBounds,
        "pc-relative target underflow rejected");
}
Check(Relay(0xb4000040, relayStub, out var cbz, out _) &&
      cbz.Strategy == "cbz" && cbz.Words.Count == 2 &&
      AsmInstruction.Decode(cbz.Words[0]).ToString().Contains("cbnz", StringComparison.OrdinalIgnoreCase),
    "cbz inverted to cbnz");
Check(Relay(0xb5000040, relayStub, out var cbnz, out _) &&
      cbnz.Strategy == "cbnz" && cbnz.Words.Count == 2 &&
      AsmInstruction.Decode(cbnz.Words[0]).ToString().Contains("cbz ", StringComparison.OrdinalIgnoreCase),
    "cbnz inverted to cbz");
Check(!Relay(0xb400005f, relayStub, out _, out var cbzXzr) &&
      cbzXzr.Status == PatchStatus.UnsupportedInstruction, "cbz xzr rejected");
Check(Relay(0x3607ffe0, relayStub, out var tbz, out _) &&
      tbz.Strategy == "tbz" && tbz.Words.Count == 2 &&
      AsmInstruction.Decode(tbz.Words[0]).ToString().Contains("tbnz", StringComparison.OrdinalIgnoreCase),
    "tbz inverted to tbnz");
Check(Relay(0x3707ffe0, relayStub, out var tbnz, out _) &&
      tbnz.Strategy == "tbnz" && tbnz.Words.Count == 2 &&
      AsmInstruction.Decode(tbnz.Words[0]).ToString().Contains("tbz ", StringComparison.OrdinalIgnoreCase),
    "tbnz inverted to tbz");
Check(Relay(0x10000020, relayStub, out var adr, out _) &&
      adr.Strategy == "adr" && adr.Words.Count == 4, "adr degraded to movz/movk");
Check(!Relay(0x1000003f, relayStub, out _, out var adrXzr) &&
      adrXzr.Status == PatchStatus.UnsupportedInstruction, "adr xzr rejected");
Check(Relay(0x90000020, relayStub, out var adrp, out _) &&
      adrp.Strategy == "adrp" && adrp.Words.Count == 1, "adrp re-encoded");

var literal64 = new byte[] { 0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11 };
var literal32 = literal64[..4];
Check(Relay(0x58000040, relayStub, out var literal, out _, literal64) &&
      literal.Strategy == "literal-inline" && literal.Words.Count == 6 && literal.CodeWords == 4,
    "ldr x literal inlined");
Check(literal.Words[4] == 0x55667788u && literal.Words[5] == 0x11223344u,
    "literal data words");
Check(((relayStub + (ulong)(literal.CodeWords * 4)) & 7) == 0,
    "ldr x literal data is 8-byte aligned");
Check(Relay(0x18000040, relayStub, out var ldrw, out _, literal32) &&
      ldrw.Words.Count == 4 && ldrw.Words[3] == 0x55667788u,
    "ldr w literal writes 4 bytes");
Check(Relay(0x98000040, relayStub, out var ldrsw, out _, literal32) &&
      ldrsw.Words.Count == 4 && ldrsw.Words[3] == 0x55667788u,
    "ldrsw inlined as 4 bytes");
Check(!Relay(0x58000040, relayStub, out _, out var missing) &&
      missing.Status == PatchStatus.InvalidArgument, "ldr literal without value rejected");
Check(!Relay(0x5800005f, relayStub, out _, out var ldrXzr, literal64) &&
      ldrXzr.Status == PatchStatus.InvalidArgument, "ldr literal into xzr rejected");
Check(!Relay(0xd8000040, relayStub, out _, out var prfm, literal64) &&
      prfm.Status == PatchStatus.InvalidArgument, "prfm literal rejected");

var literal128 = new byte[]
{
    0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07,
    0x08, 0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f
};
Check(Relay(0x9c000040, relayStub, out var ldrq, out _, literal128) &&
      ldrq.Strategy == "literal-inline" && ldrq.CodeWords == 4 && ldrq.Words.Count == 8,
    "ldr q literal inlined");
Check((ldrq.Words[0] & 0x3B000000) == 0x18000000 &&
      (ldrq.Words[0] >> 30) == 2 && (ldrq.Words[0] & 0x04000000) != 0 &&
      (ldrq.Words[0] & 0x1F) == 0 &&
      AsmInstruction.Decode(ldrq.Words[1]).Mnemonic == Arm64Mnemonic.B,
    "vector literal uses a PC-relative LDR and a skip B");
Check(ldrq.Words[ldrq.CodeWords] == 0x03020100u && ldrq.Words[ldrq.CodeWords + 3] == 0x0F0E0D0Cu,
    "vector literal data words");
Check(((relayStub + (ulong)(ldrq.CodeWords * 4)) & 15) == 0,
    "ldr q literal data is 16-byte aligned");
Check(Relay(0x1c000040, relayStub, out var ldrs, out _, literal32) &&
      ldrs.CodeWords == 2 && ldrs.Words.Count == 3 &&
      ldrs.Words[ldrs.CodeWords] == 0x55667788u, "ldr s literal inlined");
Check(Relay(0x5c000040, relayStub, out var ldrd, out _, literal64) &&
      ldrd.CodeWords == 2 && ldrd.Words.Count == 4 &&
      ldrd.Words[ldrd.CodeWords] == 0x55667788u &&
      ldrd.Words[ldrd.CodeWords + 1] == 0x11223344u, "ldr d literal inlined");
Check(!Relay(0x9c000040, relayStub, out _, out var shortData, literal64) &&
      shortData.Status == PatchStatus.InvalidArgument, "vector literal width enforced");
Check(!Relay(0xffffffff, relayStub, out _, out var invalidRelocation) &&
      invalidRelocation.Status == PatchStatus.UnsupportedInstruction, "invalid word rejected");

Check(InterceptStub.TryBuild(relayStub, relaySite, 0xd503201f, 7,
        0x1234_5678_9abc_def0UL, null, out var stubPlan, out var stubErrors),
    "stub builds: " + string.Join(';', stubErrors));
var stubWords = new uint[stubPlan.StubBytes.Length / 4];
for (var i = 0; i < stubWords.Length; i++)
    stubWords[i] = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(
        stubPlan.StubBytes.AsSpan(i * 4, 4));
Check(stubWords.All(w => AsmInstruction.Decode(w).Id != AsmArm64.Arm64InstructionId.Invalid),
    "every stub word decodes");
Check(stubWords[0] == 0xD10C83FFu, "stub allocates an 800-byte frame");
Check(stubWords.Contains(0xD53B4209u) && stubWords.Contains(0xD53B4409u) &&
      stubWords.Contains(0xD53B4429u), "stub saves NZCV/FPCR/FPSR");
Check(stubWords.Contains(0xD51B4209u) && stubWords.Contains(0xD51B4409u) &&
      stubWords.Contains(0xD51B4429u), "stub restores NZCV/FPCR/FPSR");
var saveOffsets = stubWords
    .Where(w => ((w >> 5) & 0x1F) == 31 &&
                AsmInstruction.Decode(w).ToString().StartsWith("stp q", StringComparison.OrdinalIgnoreCase))
    .Select(w => (int)(((w >> 15) & 0x7F) << 4)).ToArray();
Check(saveOffsets.Length == 16 &&
      saveOffsets.Select((offset, index) => offset == InterceptFrame.VectorOffset + index * 32).All(ok => ok),
    "vector save slots do not overlap: " + string.Join(',', saveOffsets.Select(o => o.ToString("x"))));
Check(saveOffsets[^1] + 32 == InterceptFrame.DecisionOffset, "vector save ends at the decision slot");
var restoreOffsets = stubWords
    .Where(w => ((w >> 5) & 0x1F) == 31 &&
                AsmInstruction.Decode(w).ToString().StartsWith("ldp q", StringComparison.OrdinalIgnoreCase))
    .Select(w => (int)(((w >> 15) & 0x7F) << 4)).ToArray();
Check(restoreOffsets.Length == 16 &&
      restoreOffsets.Select((offset, index) => offset == InterceptFrame.VectorOffset + index * 32).All(ok => ok),
    "vector restore slots mirror the save");
Check(stubWords.Contains(0xD63F0120u), "stub calls the callback through x9");
Check(stubWords.Any(w => (w & 0xFF00001F) == 0x35000009u),
    "stub branches on the decision register");
Check(stubPlan.Relocation.Strategy == "verbatim" && stubPlan.Relocation.Words.Count == 1,
    "stub embeds the relocated instruction");
static uint BranchEncoding(ulong pc, ulong target) =>
    0x14000000u | (uint)(((long)target - (long)pc) >> 2 & 0x03ffffff);
Check(stubPlan.SiteBranchWord == BranchEncoding(relaySite, relayStub),
    "site branch encodes the stub page target");
var backPc = relayStub + (ulong)(stubWords.Length - 1) * 4;
Check(stubWords[^1] == BranchEncoding(backPc, relaySite + 4),
    "stub returns to site+4");
Check(InterceptStub.TryBuild(relayStub, relaySite, 0x54ffffe0, 1,
        0x1234_5678_9abc_def0UL, null, out var condStub, out _) &&
      condStub.Relocation.Words.Count == 2, "stub handles a conditional original");
Check(InterceptStub.TryBuild(relayStub, relaySite, 0x58000040, 9,
        0x1234_5678_9abc_def0UL, literal64, out var literalStub, out var literalStubErrors),
    "stub builds with an inlined literal: " + string.Join(';', literalStubErrors));
var literalDataIndex = literalStub.Relocation.CodeWords;
Check(literalStub.Relocation.Words.Count == literalDataIndex + 2 &&
      literalStub.Relocation.Words[literalDataIndex] == 0x55667788u,
    "stub keeps literal data outside the code check");
Check(!InterceptStub.TryBuild(relayStub + 2, relaySite, 0xd503201f, 1, 0UL, null, out _, out _),
    "unaligned stub page rejected");
Check(!InterceptStub.TryBuild(relayStub, relaySite + 2, 0xd503201f, 1, 0UL, null, out _, out _),
    "unaligned site rejected");

Console.WriteLine($"ALL {checks} CHECKS PASSED");