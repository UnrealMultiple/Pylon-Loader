# LimitPatch offline static profiles

Run from the repository root with .NET 10:

```powershell
dotnet run --project src/csharp/tools/limit-profile -- --self-test
dotnet run --project src/csharp/tools/limit-profile -- C:/path/to/Pylon-Loader <output.json>
dotnet run --project src/csharp/tools/limit-profile -- --verify-profile <output.json>
```

The generator reads only the explicitly supported local reference in `refs/README.md` (development baseline 1.4.6), `refs/script.json`, and `refs/dump.cs`. It does not inspect a process, another checkout, or Bunny-Loader. Profile provenance pins the reference ELF SHA256 and GNU BuildID, plus both metadata hashes. The generated profile comes from that reference, not from live actual instructions. No binaries are copied.

The ELF reader requires ELF64 little endian AArch64, `.eh_frame_hdr` encodings `1b/03/3b`, a version-1 `zR` or `zPLR` CIE ending in `R=1b`, and an exact method RVA match in both metadata and an indexed FDE. Unsupported layouts fail instead of estimating a method length. Each emitted site contains a complete 16 byte expected fingerprint starting at CMP. `expectedMethod` contains the complete FDE bytes required by strict native v2. Hex strings preserve little endian byte order.

AsmArm64 2.4.0 identifies CMP immediate with LSL shift 0 or 12, followed by B.cond/CSEL/CSET after at most three decoded STP/STR/NOP instructions. This explicit allowlist preserves NZCV and excludes intervening flag writers and control flow. Conditions match the legacy limit policy: HI/LS/GT/LE, plus HS/LO/GE/LT for belowToo. Replacements use typed CMP assembler calls and retain the original shift. The target `total + totalOffset` replaces the 12 bit immediate field; it is not the already-shifted effective operand. Expansion above that field is rejected. The generation report explicitly lists unsupported methods. On this reference, all 20 methods have 21 supported sites: PlaceObject has stores between CMP and B.cond, and GetTileDefinition uses `CMP ..., #754, LSL #12`. This is offline coverage; it does not claim that the two newly supported methods have executed on Android.

## Host interface

Deserialize the static artifact at startup and convert hex with `Convert.FromHexString`. Construct `CompareLimitSite(offset, oldLimit, totalOffset, allowBelow, fingerprint, shiftAmount)` and `CompareLimitMethodProfile(method, methodRva, expectedMethod, sites)`. Every generated site contains `shiftAmount` (0 or 12); the optional constructor default of 0 retains source compatibility for unshifted callers. The planner rejects a shift that differs from the decoded static expected CMP. Both constructors defensively copy their input; exposed collections are read-only.

```csharp
if (!CompareLimitPolicy.Plan(methodProfile, total, out var plan, out var reason))
    return; // Explicit unsupported result; no native writes.
foreach (var instruction in plan!.Replacements)
    Console.WriteLine($"{instruction.Offset:x}: {instruction.Assembly}");
```

`CompareLimitPolicy.Plan` is pure and accepts no live byte source. `LimitPatch.ApplyProfile(profile, expectedBuildId, authenticatedBuildId, imageBase, total, callerGuaranteesQuiescence, log)` installs one method using the existing v2 transaction. When constructing a replacement in `ApplyProfile`, preserve `site.ShiftAmount`: call `assembler.CMP((Arm64RegisterW)register, (ushort)target, default, site.ShiftAmount)` (or the X-register overload). `target` is `total + site.TotalOffset`, bounded to 0..4095, and the static decoded shift must match site metadata before installation. The pure planner supports both W- and X-register immediate CMP. The host must authenticate loaded ELF BuildID with `CodeImage.TryQuery`, ensure the seed belongs to the intended IL2CPP image, and exclude competing writers for the duration of the call. Execution may continue: v2 writes go through `/proc/self/mem` without changing page permissions, so `callerGuaranteesQuiescence` now means "no competing writers", not "threads suspended". Passing a string is not itself authentication. Runtime mismatch must fail closed; never synthesize expected from actual.

Every site uses the same immutable complete-method expected. Native v2 must validate prior registered writes in that method against registry expected/replacement pairs. Without that support, the second site conflicts with the first. Sites are individually atomic; a multi-site method is not an all-or-nothing transaction. Failure may leave prior sites installed; do not automatically fall back to a native scanner over partially patched code. `LimitPatch.Apply` installs through `RuntimeLimitInstaller`, which resolves every method at runtime; the pinned JSON profile path and the legacy native scanner (`PylonMod_PatchCompareLimit` / `LimitPatch.ApplyLegacy*`) have been removed.

Self-tests cover immutable source bytes, typed W/X CMP replacement with LSL #12 retained, mismatching fingerprints and shift metadata, out-of-range immediate fields, every condition, zero through three preserving instructions, rejection of four intervening instructions, and rejection of intervening NZCV writers/control flow or invalid consumers. Profile verification re-plans all 21 emitted sites against their static complete-method bytes and requires complete coverage of all 20 methods with no failures; it does not claim execution on Android. The previously supported 18 methods / 19 sites have separately passed device validation; these offline checks do not extend that device result to the two new patterns.
