using System.Buffers.Binary;
using System.Runtime.InteropServices;
using AsmArm64;
using Pylon.ModLoader.Logging;
using Pylon.NaviteAPI.Host;
using Instruction = AsmArm64.Arm64Instruction;

namespace Pylon.ModLoader.Patching;
/// <summary>
/// Runtime limit patcher: it resolves each method through the Il2CppInterop proxy
/// type with <see cref="MethodCode.TryResolve"/>, scans the live method body for the
/// compile-time CMP pattern, and patches the immediate in place.
///
/// The write still uses the native ABI v2 single-word path (which goes through
/// /proc/self/mem and never changes page permissions). The per-site fingerprint is a
/// 16-byte window read immediately before the write; it is not an offline authenticity
/// proof. Method identity comes from il2cpp name resolution + the CMP/branch pattern.
/// </summary>
internal static unsafe class RuntimeLimitInstaller
{
    private const int WindowBytes = 16;
    private const uint MaxMethodBytes = 512 * 1024;
    private static readonly object ImageLock = new();
    private static CodeImage _cachedImage;
    private static bool _cachedImageValid;

    private readonly record struct Match(int Offset, Arm64RegisterW Register, int Shift,
        uint Expected, uint Replacement);

    internal static int Apply(int total, IModLog log)
    {
        if (total <= LimitPatch.TileCount) return 0;
        if (!TryGetImage(out var image, log)) return LimitPatch.Entries.Length;

        var failed = 0;
        foreach (var entry in LimitPatch.Entries)
        {
            try
            {
                if (!ApplyEntry(entry, total, image, log)) failed++;
            }
            catch (Exception exception)
            {
                log.Warn($"[limit] runtime {entry.TypeName}.{entry.Method}: " +
                         exception.GetType().Name + ": " + exception.Message);
                failed++;
            }
        }
        log.Info($"[limit] runtime profile total={total} image={image.BuildId} " +
                 $"methods={LimitPatch.Entries.Length} failed={failed}");
        return failed;
    }

    /// <summary>
    /// Patch every <c>cmp #oldLimit</c> in one runtime-resolved method. Returns the number
    /// of sites patched or already carrying the new limit; -1 on resolution/read/write error.
    /// No native string scanner and no offline method RVA are involved.
    /// </summary>
    internal static int PatchMethod(System.Type klass, string method, int argc, uint oldLimit,
        uint newLimit, bool belowToo, bool shifted, IModLog log)
    {
        if (!TryGetImage(out var image, log)) return -1;
        return TryPatchMethod(klass, method, argc, oldLimit, (int)newLimit, belowToo, shifted,
            -1, image, log, out var patched, out var already)
            ? patched + already
            : -1;
    }

    private static bool TryGetImage(out CodeImage image, IModLog log)
    {
        lock (ImageLock)
        {
            if (_cachedImageValid)
            {
                image = _cachedImage;
                return true;
            }
        }
        image = default;
        if (!NativeLibrary.TryLoad("libil2cpp.so", out var module))
        {
            log.Warn("[limit] runtime: libil2cpp.so unavailable");
            return false;
        }
        if (!NativeLibrary.TryGetExport(module, "il2cpp_domain_get", out var symbol))
        {
            NativeLibrary.Free(module);
            log.Warn("[limit] runtime: il2cpp_domain_get unavailable");
            return false;
        }
        var found = CodeImage.TryQuery(unchecked((ulong)symbol.ToInt64()), out image, out var status);
        NativeLibrary.Free(module);
        if (!found || image.ImageBase == 0 || string.IsNullOrEmpty(image.BuildId))
        {
            log.Warn($"[limit] runtime: image identity unavailable ({status})");
            return false;
        }
        lock (ImageLock)
        {
            _cachedImage = image;
            _cachedImageValid = true;
        }
        return true;
    }

    private static bool ApplyEntry(LimitPatch.Entry entry, int total, CodeImage image, IModLog log)
    {
        var target = total + entry.Offset;
        if (!TryPatchMethod(entry.Class, entry.Method, entry.Argc, entry.Old, target,
                entry.BelowToo, entry.Shifted, entry.Sites, image, log,
                out var patched, out var already))
            return false;
        log.Info($"[limit] runtime {entry.TypeName}.{entry.Method}: " +
                 $"patched={patched} already={already}");
        return true;
    }

    private static bool TryPatchMethod(System.Type klass, string method, int argc,
        uint oldLimit, int newLimit, bool belowToo, bool shifted, int expectedSites,
        CodeImage image, IModLog log, out int patched, out int already)
    {
        patched = 0;
        already = 0;
        var owner = klass.FullName ?? klass.Name;
        var klassPointer = MethodCode.KlassOf(klass);
        if (klassPointer == IntPtr.Zero)
        {
            log.Warn($"[limit] runtime {owner}: class pointer is not ready");
            return false;
        }
        if (!MethodCode.TryResolve(klassPointer, method, argc, out var body, out var end))
        {
            log.Warn($"[limit] runtime {owner}.{method}: method not found");
            return false;
        }

        var start = unchecked((ulong)body.ToInt64());
        var stop = unchecked((ulong)end.ToInt64());
        if (start == 0 || stop <= start)
        {
            log.Warn($"[limit] runtime {owner}.{method}: empty method body");
            return false;
        }
        var size = stop - start;
        if (size > MaxMethodBytes) size = MaxMethodBytes;
        var length = (int)(size & ~3UL);
        if (length < WindowBytes)
        {
            log.Warn($"[limit] runtime {owner}.{method}: method too small ({length})");
            return false;
        }
        var bytes = new byte[length];
        if (!TryRead(start, bytes))
        {
            log.Warn($"[limit] runtime {owner}.{method}: method body unreadable " +
                     $"at 0x{start:x}+{length}");
            return false;
        }
        if (newLimit < 0 || newLimit > 4095)
        {
            log.Warn($"[limit] runtime {owner}.{method}: target {newLimit} does not fit " +
                     "the 12-bit CMP immediate");
            return false;
        }

        if (oldLimit == (uint)newLimit)
        {
            // Loot can call with old == new when the vanilla/mod counts coincide.
            // Nothing to write; count the matching sites as already correct.
            already = FindMatches(bytes, oldLimit, newLimit, belowToo, shifted, owner, method, log).Count;
            if (expectedSites >= 0 && already != expectedSites)
            {
                log.Warn($"[limit] runtime {owner}.{method}: expected {expectedSites} site(s), " +
                         $"found {already} (body=0x{start:x} size={length} first={FirstWords(bytes)})");
                return false;
            }
            log.Info($"[limit] runtime {owner}.{method}: body=0x{start:x} size={length} " +
                     $"patched=0 already={already}");
            return already > 0;
        }

        var oldMatches = FindMatches(bytes, oldLimit, newLimit, belowToo, shifted, owner, method, log);
        var newMatches = FindMatches(bytes, (uint)newLimit, newLimit, belowToo, shifted, owner, method, log);
        if (expectedSites >= 0 && oldMatches.Count + newMatches.Count != expectedSites)
        {
            log.Warn($"[limit] runtime {owner}.{method}: expected {expectedSites} site(s), " +
                     $"found old={oldMatches.Count} new={newMatches.Count} " +
                     $"(body=0x{start:x} size={length} first={FirstWords(bytes)})");
            return false;
        }
        if (oldMatches.Count + newMatches.Count == 0)
        {
            log.Warn($"[limit] runtime {owner}.{method}: no CMP#{oldLimit} site " +
                     $"(body=0x{start:x} size={length} first={FirstWords(bytes)})");
            return false;
        }

        foreach (var match in oldMatches)
        {
            var site = start + (ulong)match.Offset;
            if (!TryPatchSite(site, match, image, owner, method, oldLimit, newLimit, log))
                return false;
            patched++;
        }
        already = newMatches.Count;
        if (already != 0)
            log.Info($"[limit] runtime {owner}.{method}: {already} site(s) already carry the new limit");
        log.Info($"[limit] runtime {owner}.{method}: body=0x{start:x} size={length} " +
                 $"patched={patched} already={already}");
        return true;
    }

    private static bool TryPatchSite(ulong site, Match match, CodeImage image,
        string owner, string method, uint oldLimit, int target, IModLog log)
    {
        // Use the 16-byte aligned block containing the site. It never crosses a 4KB page,
        // so the native executable-mapping check sees a single permission range.
        var windowStart = site & ~0xFUL;
        var siteOffset = (int)(site - windowStart);
        var window = new byte[WindowBytes];
        if (!TryRead(windowStart, window))
        {
            log.Warn($"[limit] runtime {owner}.{method}: window unreadable at 0x{windowStart:x}");
            return false;
        }
        var words = new uint[4];
        for (var i = 0; i < words.Length; i++)
            words[i] = BinaryPrimitives.ReadUInt32LittleEndian(window.AsSpan(i * 4, 4));
        var windowRva = windowStart - image.ImageBase;
        if (windowStart < image.ImageBase || windowRva > uint.MaxValue)
        {
            log.Warn($"[limit] runtime {owner}.{method}: site 0x{site:x} is outside " +
                     $"image 0x{image.ImageBase:x}");
            return false;
        }

        var profile = new CodeSiteProfile(
            image.BuildId,
            "runtime-limit",
            windowStart,
            (uint)windowRva,
            (uint)siteOffset,
            WindowBytes,
            "LimitPatch",
            owner + "." + method + "/" + match.Offset.ToString("x"),
            Array.AsReadOnly(words),
            Array.AsReadOnly(window));
        using var builder = new Arm64PatchBuilder(site);
        builder.CMP(match.Register, (ushort)target, match.Shift);
        if (!Arm64PatchTransaction.TryPlan(profile, image.BuildId, builder,
                out var transaction, out var diagnostics))
        {
            log.Warn($"[limit] runtime {owner}.{method}: plan failed at 0x{site:x}: " +
                     string.Join("; ", diagnostics));
            return false;
        }
        if (!transaction!.TryApply(true, out var diagnostic))
        {
            log.Warn($"[limit] runtime {owner}.{method}: write failed at 0x{site:x}: " +
                     $"{diagnostic.Status} expected={diagnostic.Expected:x8} " +
                     $"actual={diagnostic.Actual:x8}");
            return false;
        }
        log.Info($"[limit] runtime {owner}.{method}: site=0x{site:x} " +
                 $"offset=0x{match.Offset:x} {oldLimit}->{target}");
        return true;
    }

    private static List<Match> FindMatches(byte[] method, uint limit, int target,
        bool belowToo, bool shifted, string owner, string methodName, IModLog log)
    {
        var matches = new List<Match>();
        for (var offset = 0; offset <= method.Length - WindowBytes; offset += 4)
        {
            if (!CompareLimitPolicy.IsCandidate(method, offset, limit, belowToo, out _))
                continue;
            var expected = BinaryPrimitives.ReadUInt32LittleEndian(method.AsSpan(offset, 4));
            var cmp = Instruction.Decode(expected);
            if (cmp.GetOperand(0).Kind != Arm64OperandKind.Register) continue;
            var register = ((Arm64RegisterOperand)cmp.GetOperand(0)).Value;
            if (register.Kind != Arm64RegisterKind.W) continue;
            var shift = cmp.OperandCount > 2 && cmp.GetOperand(2).Kind == Arm64OperandKind.Shift
                ? ((Arm64ShiftOperand)cmp.GetOperand(2)).Amount : 0;
            if (shift != (shifted ? 12 : 0)) continue;
            var replacement = PlanReplacement((Arm64RegisterW)register, (ushort)target, shift);
            if (replacement == 0)
            {
                log.Warn($"[limit] runtime {owner}.{methodName}: cannot assemble replacement " +
                         $"at 0x{offset:x}");
                continue;
            }
            matches.Add(new(offset, (Arm64RegisterW)register, shift, expected, replacement));
        }
        return matches;
    }

    private static uint PlanReplacement(Arm64RegisterW register, ushort target, int shift)
    {
        using var builder = new Arm64PatchBuilder(0);
        builder.CMP(register, target, shift);
        if (!builder.TryBuild(out var instructions, out _) || instructions.Count != 1) return 0;
        return instructions[0].Word;
    }

    private static bool TryRead(ulong address, byte[] buffer)
    {
        fixed (byte* destination = buffer)
            return Arm64Native.TryReadMemory(address, destination, (uint)buffer.Length, out _) == 0;
    }

    private static string FirstWords(byte[] bytes)
    {
        var count = Math.Min(4, bytes.Length / 4);
        var words = new uint[count];
        for (var i = 0; i < count; i++)
            words[i] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i * 4, 4));
        return string.Join(",", Array.ConvertAll(words, word => word.ToString("x8")));
    }
}
