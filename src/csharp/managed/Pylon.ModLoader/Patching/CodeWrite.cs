namespace Pylon.ModLoader.Patching;
/// <summary>
/// Thin wrapper over native ABI v1: PylonMod_Arm64Write now writes one aligned
/// 32-bit word through /proc/self/mem and never changes page permissions.
/// </summary>
internal static class CodeWrite
{
    internal static bool TryWrite(IntPtr address, uint expected, uint replacement,
        out PatchDiagnostic diagnostic)
    {
        diagnostic = default;
        if (!CodeSite.TryCreate(unchecked((ulong)address.ToInt64()), 4, out var site, out diagnostic))
            return false;
        return site.TryWrite(0, expected, replacement, out diagnostic);
    }
}
