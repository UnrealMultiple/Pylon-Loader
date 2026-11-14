using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pylon.ModLoader;

internal static class StaticLimitInstaller
{
    // Executed by the content-install stage before the world is available. The
    // caller owns this quiescence contract; native does not suspend game threads.
    internal static int Apply(int total, IModLog log)
    {
        if (total <= 754) return 0;
        using var resource = typeof(StaticLimitInstaller).Assembly.GetManifestResourceStream("Pylon.LimitProfile.json");
        if (resource is null)
        {
            log.Warn("[arm64] static limit profile resource missing");
            return 20;
        }
        var profile = JsonSerializer.Deserialize(resource, LimitProfileJson.Default.LimitProfileDocument);
        if (profile is null || profile.Schema != 1 || profile.Provenance.BuildId.Length == 0)
        {
            log.Warn("[arm64] invalid static limit profile");
            return 20;
        }
        if (!NativeLibrary.TryLoad("libil2cpp.so", out var module))
        {
            log.Warn("[arm64] loaded il2cpp image unavailable");
            return 20;
        }
        var found = NativeLibrary.TryGetExport(module, "il2cpp_domain_get", out var symbol);
        CodeImage image = default;
        var identified = found && CodeImage.TryQuery((ulong)symbol, out image, out _);
        if (!identified)
        {
            NativeLibrary.Free(module);
            log.Warn("[arm64] loaded ELF BuildID unavailable");
            return 20;
        }
        NativeLibrary.Free(module);
        if (!string.Equals(image.BuildId, profile.Provenance.BuildId, StringComparison.Ordinal))
        {
            log.Warn($"[arm64] BuildMismatch expected={profile.Provenance.BuildId} actual={image.BuildId}");
            return 20;
        }
        var failed = 0;
        foreach (var method in profile.Methods)
        {
            if (method.Sites.Length == 0)
            {
                log.Warn($"[arm64] unsupported static limit site: {method.Method}");
                failed++;
                continue;
            }
            var staticMethod = new CompareLimitMethodProfile(method.Method, method.MethodRva,
                Convert.FromHexString(method.ExpectedMethod), method.Sites.Select(site => new CompareLimitSite(
                    site.Offset, site.OldLimit, site.TotalOffset, site.AllowBelow,
                    Convert.FromHexString(site.ExpectedFingerprint), site.ShiftAmount)));
            if (LimitPatch.ApplyProfile(staticMethod, profile.Provenance.BuildId, image.BuildId,
                image.ImageBase, total, true, log))
                log.Info($"[arm64] AsmArm64 business patch installed: {method.Method} sites={method.Sites.Length}");
            else failed++;
        }
        log.Info($"[arm64] AsmArm64 static limit profile={profile.Profile} BuildID={image.BuildId} methods={profile.Methods.Length} failed={failed}");
        return failed;
    }
}

internal sealed class LimitProfileDocument
{
    public int Schema { get; set; }
    public string Profile { get; set; } = "";
    public LimitProfileProvenance Provenance { get; set; } = new();
    public LimitProfileMethod[] Methods { get; set; } = [];
}
internal sealed class LimitProfileProvenance { public string BuildId { get; set; } = ""; }
internal sealed class LimitProfileMethod
{
    public string Method { get; set; } = "";
    public uint MethodRva { get; set; }
    public string ExpectedMethod { get; set; } = "";
    public LimitProfileSite[] Sites { get; set; } = [];
}
internal sealed class LimitProfileSite
{
    public uint Offset { get; set; }
    public uint OldLimit { get; set; }
    public int TotalOffset { get; set; }
    public bool AllowBelow { get; set; }
    public int ShiftAmount { get; set; }
    public string ExpectedFingerprint { get; set; } = "";
}
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LimitProfileDocument))]
internal partial class LimitProfileJson : JsonSerializerContext;
