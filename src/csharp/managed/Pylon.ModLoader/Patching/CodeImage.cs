using Pylon.NaviteAPI.Host;

namespace Pylon.ModLoader.Patching;
/// <summary>Identity read from the loaded ELF's GNU note, selected by code address.</summary>
public readonly record struct CodeImage(ulong ImageBase, string BuildId)
{
    public static unsafe bool TryQuery(ulong methodAddress, out CodeImage image, out PatchStatus status)
    {
        status = (PatchStatus)Arm64Native.TryGetCodeImage(methodAddress, out var native);
        image = default;
        if (status != PatchStatus.Ok) return false;
        if (native.BuildIdLength == 0 || native.BuildIdLength > 64)
        {
            status = PatchStatus.BuildMismatch;
            return false;
        }
        image = new(native.ImageBase, Convert.ToHexString(new ReadOnlySpan<byte>(native.BuildId,
            (int)native.BuildIdLength)).ToLowerInvariant());
        return true;
    }
}
