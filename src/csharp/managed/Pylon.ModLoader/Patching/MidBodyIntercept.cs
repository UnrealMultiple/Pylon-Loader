using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Pylon.ModLoader.Logging;
using Pylon.NaviteAPI.Host;

namespace Pylon.ModLoader.Patching;

public delegate bool InterceptHandler(IntPtr frame);

public sealed class MidBodyInterception
{
    public string Owner { get; init; } = "";
    public string SiteId { get; init; } = "";
    public ulong SiteAddress { get; init; }
    public ulong StubPage { get; init; }
    public string RelocationStrategy { get; init; } = "";
    public long Token { get; init; }
    internal InterceptHandler Handler { get; init; } = static _ => false;
}

public static unsafe class MidBodyIntercept
{
    private static readonly ConcurrentDictionary<long, MidBodyInterception> Registry = new();
    private static long _nextToken;
    private static IModLog? _log;

    public static bool TryInstall(CodeSiteProfile profile, string authenticatedBuildId,
        InterceptHandler handler, bool callerGuaranteesQuiescence,
        out MidBodyInterception? interception,
        out IReadOnlyList<Arm64AssemblyDiagnostic> diagnostics, IModLog? log = null)
    {
        interception = null;
        var errors = new List<Arm64AssemblyDiagnostic>();
        diagnostics = errors;
        _log = log ?? _log;
        if (handler is null)
        {
            errors.Add(new("InvalidArgument", "Handler is required."));
            return false;
        }
        if (!CodeSite.TryValidateProfile(profile, authenticatedBuildId, out var profileDiagnostic))
        {
            errors.Add(new("Profile", profileDiagnostic.Status.ToString()));
            return false;
        }
        if ((profile.ByteOffset & 3) != 0 ||
            (ulong)profile.ByteOffset + 4 > profile.MethodSize ||
            (ulong)profile.ByteOffset / 4 >= (ulong)profile.ExpectedWords.Count)
        {
            errors.Add(new("Profile", "Site offset must be a word inside the method fingerprint."));
            return false;
        }

        var siteAddress = profile.SiteAddress;
        var originalWord = profile.ExpectedWords[(int)(profile.ByteOffset / 4)];

        byte[]? literalData = null;
        if ((originalWord & 0x3B000000) == 0x18000000)
        {
            var opc = originalWord >> 30;
            var isVector = (originalWord & 0x04000000) != 0;
            var width = isVector
                ? (opc == 0 ? 4u : opc == 1 ? 8u : 16u)
                : (opc == 1 ? 8u : 4u);
            if (opc == 3 ||
                !Arm64Decoder.TryDecode(siteAddress, originalWord, out var decoded, out _) ||
                decoded!.PcTarget is null ||
                !TryReadLiteral(decoded.PcTarget.Value, width, out literalData))
            {
                errors.Add(new("Literal", "The literal pool value is not readable."));
                return false;
            }
        }

        var token = Interlocked.Increment(ref _nextToken);
        if (CodePageNative.Reserve(siteAddress, 0x400, out var page) != 0)
        {
            errors.Add(new("Reserve", "No free code page within branch reach of the site."));
            return false;
        }

        var keepPage = false;
        try
        {
            var entry = (ulong)(nint)(delegate* unmanaged<long, nint, int>)&OnIntercept;
            if (!InterceptStub.TryBuild(page, siteAddress, originalWord, (ulong)token, entry,
                    literalData, out var stub, out var stubDiagnostics))
            {
                errors.AddRange(stubDiagnostics);
                return false;
            }
            if (stub.StubBytes.Length > CodePageNative.PageBytes)
            {
                errors.Add(new("Reserve", "The generated stub exceeds the reserved code page."));
                return false;
            }

            fixed (byte* bytes = stub.StubBytes)
            {
                if (CodePageNative.Seal(page, bytes, (uint)stub.StubBytes.Length) != 0)
                {
                    errors.Add(new("Seal", "Sealing the stub page failed."));
                    return false;
                }
            }

            using var builder = new Arm64PatchBuilder(siteAddress);
            builder.B(page);
            if (!Arm64PatchTransaction.TryPlan(profile, authenticatedBuildId, builder,
                    out var transaction, out var planDiagnostics, fingerprintWordCount: 0))
            {
                errors.AddRange(planDiagnostics);
                return false;
            }
            if (!transaction!.TryApply(callerGuaranteesQuiescence, out var writeDiagnostic))
            {
                // ProtectionRestoreFailed may mean the word was already written and the
                // site now branches into this page, so the page must stay mapped.
                keepPage = writeDiagnostic.Status == PatchStatus.ProtectionRestoreFailed;
                errors.Add(new("Write", writeDiagnostic.Status.ToString()));
                return false;
            }

            // The site is live now; never release the page even if later bookkeeping fails.
            keepPage = true;
            interception = new MidBodyInterception
            {
                Owner = profile.Owner,
                SiteId = profile.SiteId,
                SiteAddress = siteAddress,
                StubPage = page,
                RelocationStrategy = stub.Relocation.Strategy,
                Token = token,
                Handler = handler
            };
            Registry[token] = interception;
            return true;
        }
        finally
        {
            if (!keepPage) CodePageNative.Release(page);
        }
    }

    private static unsafe bool TryReadLiteral(ulong literalAddress, uint byteWidth, out byte[]? data)
    {
        data = null;
        if ((literalAddress & 3) != 0 || byteWidth == 0 || byteWidth > 16 || (byteWidth & 3) != 0)
            return false;
        var bytes = new byte[byteWidth];
        fixed (byte* destination = bytes)
        {
            if (Arm64Native.TryReadMemory(literalAddress, destination, byteWidth, out _) != 0)
                return false;
        }
        data = bytes;
        return true;
    }

    [UnmanagedCallersOnly]
    private static int OnIntercept(long token, nint frame)
    {
        try
        {
            if (Registry.TryGetValue(token, out var interception) &&
                interception.Handler(frame))
            {
                return 1;
            }
        }
        catch (Exception exception)
        {
            try
            {
                _log?.Warn("[intercept] " + Safe.Dump(exception));
            }
            catch
            {
                // UnmanagedCallersOnly must never let an exception escape.
            }
        }
        return 0;
    }
}
