using System.Collections.Generic;
using System.Linq;

namespace Pylon.ModLoader;

public readonly record struct PlannedInstruction(uint Offset, uint Expected, uint Replacement, string Assembly);

/// <summary>Validated dry-run plan; execution is deliberately explicit and one instruction at a time.</summary>
public sealed class Arm64PatchPlan
{
    public IReadOnlyList<PlannedInstruction> Instructions { get; }
    public bool IsEmpty => Instructions.Count == 0;

    private Arm64PatchPlan(IReadOnlyList<PlannedInstruction> instructions) => Instructions = instructions;

    public static bool TryCreate(CodeSite site, uint offset, IReadOnlyList<uint> expected,
        IReadOnlyList<Arm64PatchInstruction> replacements, out Arm64PatchPlan plan,
        out PatchDiagnostic diagnostic)
    {
        plan = new(Array.Empty<PlannedInstruction>());
        diagnostic = default;
        if (expected.Count != replacements.Count || expected.Count == 0)
        {
            diagnostic = new(PatchStatus.InvalidArgument, 0, site.Address + offset, 0, 0);
            return false;
        }
        if (!site.TryValidate(offset, expected.ToArray(), out diagnostic)) return false;
        var planned = new List<PlannedInstruction>(expected.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            var replacement = replacements[i];
            if (replacement.Address != site.Address + offset + (uint)(i * 4))
            {
                diagnostic = new(PatchStatus.OutOfBounds, 0, replacement.Address, expected[i], replacement.Word);
                return false;
            }
            planned.Add(new((uint)(offset + i * 4), expected[i], replacement.Word, replacement.Assembly));
        }
        plan = new(planned);
        return true;
    }

    // Preview only. Install through Arm64PatchTransaction, which pins a complete
    // static fingerprint and sends owner/build/quiescence metadata to native v2.
}
