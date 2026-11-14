using System.Collections.ObjectModel;
using System.Buffers.Binary;
using AsmArm64;
using Instruction = AsmArm64.Arm64Instruction;

namespace Pylon.ModLoader;

/// <summary>One offline identified comparison and its complete 16 byte context.</summary>
public sealed class CompareLimitSite
{
    public uint Offset { get; }
    public uint OldLimit { get; }
    public int TotalOffset { get; }
    public bool AllowBelow { get; }
    public int ShiftAmount { get; }
    public IReadOnlyList<byte> ExpectedFingerprint { get; }
    public CompareLimitSite(uint offset, uint oldLimit, int totalOffset, bool allowBelow, IEnumerable<byte> fingerprint, int shiftAmount = 0)
    {
        Offset = offset; OldLimit = oldLimit; TotalOffset = totalOffset; AllowBelow = allowBelow;
        ShiftAmount = shiftAmount;
        ExpectedFingerprint = Array.AsReadOnly(fingerprint.ToArray());
    }
}

/// <summary>Explicit immutable offline input. Expected bytes must never come from live memory.</summary>
public sealed class CompareLimitMethodProfile
{
    public string Method { get; }
    public uint MethodRva { get; }
    public IReadOnlyList<byte> ExpectedMethod { get; }
    public IReadOnlyList<CompareLimitSite> Sites { get; }
    public CompareLimitMethodProfile(string method, uint methodRva, IEnumerable<byte> expectedMethod, IEnumerable<CompareLimitSite> sites)
    {
        Method = method; MethodRva = methodRva;
        ExpectedMethod = Array.AsReadOnly(expectedMethod.ToArray());
        Sites = Array.AsReadOnly(sites.ToArray());
    }
}

public sealed record CompareLimitReplacement(uint Offset, uint Expected, uint Replacement, string Assembly);
public sealed record CompareLimitMethodPlan(CompareLimitMethodProfile Profile, IReadOnlyList<CompareLimitReplacement> Replacements);

/// <summary>Pure planner: no native calls, live reads, address discovery, or installation.</summary>
public static class CompareLimitPolicy
{
    // AsmArm64 decodes both aliases (CMP, CSET, CSEL, B.cond) and operand kinds.
    public static bool IsCandidate(ReadOnlySpan<byte> method, int offset, uint oldLimit, bool allowBelow, out string reason)
    {
        reason = "unsupported comparison or consumer";
        if (offset < 0 || (offset & 3) != 0 || offset > method.Length - 16) return false;
        var cmp = Instruction.Decode(BinaryPrimitives.ReadUInt32LittleEndian(method.Slice(offset, 4)));
        if (cmp.Mnemonic != Arm64Mnemonic.CMP || cmp.OperandCount < 2 ||
            cmp.GetOperand(0).Kind != Arm64OperandKind.Register || cmp.GetOperand(1).Kind != Arm64OperandKind.Immediate ||
            ((Arm64ImmediateOperand)cmp.GetOperand(1)).Value != oldLimit) return false;
        if (cmp.OperandCount > 2 && (cmp.GetOperand(2).Kind != Arm64OperandKind.Shift ||
            ((Arm64ShiftOperand)cmp.GetOperand(2)).Amount is not (0 or 12))) return false;
        var register = ((Arm64RegisterOperand)cmp.GetOperand(0)).Value;
        if (register.Kind != Arm64RegisterKind.W && register.Kind != Arm64RegisterKind.X) return false;
        // Decode at most three intervening instructions. This deliberately narrow allowlist
        // contains only AsmArm64-identified instructions that cannot write NZCV or branch.
        for (var gap = 0; gap <= 3 && offset + 4 * (gap + 2) <= method.Length; gap++)
        {
            var consumer = Instruction.Decode(BinaryPrimitives.ReadUInt32LittleEndian(method.Slice(offset + 4 * (gap + 1), 4)));
            if (consumer.Mnemonic is Arm64Mnemonic.STP or Arm64Mnemonic.STR or Arm64Mnemonic.NOP) continue;
            if (consumer.Mnemonic != Arm64Mnemonic.B && consumer.Mnemonic != Arm64Mnemonic.CSEL &&
                consumer.Mnemonic != Arm64Mnemonic.CSET) return false;
            for (var i = 0; i < consumer.OperandCount; i++)
            {
                var operand = consumer.GetOperand(i);
                if (operand.Kind != Arm64OperandKind.Enum) continue;
                var condition = ((Arm64EnumOperand)operand).AsConditional;
                if (condition is Arm64ConditionalKind.HI or Arm64ConditionalKind.LS or Arm64ConditionalKind.GT or Arm64ConditionalKind.LE ||
                    (allowBelow && condition is Arm64ConditionalKind.HS or Arm64ConditionalKind.LO or Arm64ConditionalKind.GE or Arm64ConditionalKind.LT))
                { reason = string.Empty; return true; }
            }
            return false;
        }
        return false;
    }

    public static bool Plan(CompareLimitMethodProfile profile, int total, out CompareLimitMethodPlan? plan, out string reason)
    {
        plan = null; reason = "invalid static method profile";
        if (total <= 754 || profile.MethodRva == 0 || profile.ExpectedMethod.Count == 0 ||
            (profile.ExpectedMethod.Count & 3) != 0 || profile.Sites.Count == 0) return false;
        var method = profile.ExpectedMethod.ToArray();
        var replacements = new List<CompareLimitReplacement>();
        var offsets = new HashSet<uint>();
        foreach (var site in profile.Sites)
        {
            if (!offsets.Add(site.Offset) || site.Offset > int.MaxValue || site.ExpectedFingerprint.Count != 16 ||
                site.Offset > method.Length - 16 || !method.AsSpan((int)site.Offset, 16).SequenceEqual(site.ExpectedFingerprint.ToArray()))
                return false;
            if (!IsCandidate(method, (int)site.Offset, site.OldLimit, site.AllowBelow, out reason)) return false;
            var target = (long)total + site.TotalOffset;
            if (target < 0 || target > 4095) { reason = "CMP immediate exceeds 12 bits; no expansion is installed"; return false; }
            var expected = BinaryPrimitives.ReadUInt32LittleEndian(method.AsSpan((int)site.Offset, 4));
            var cmp = Instruction.Decode(expected);
            var register = ((Arm64RegisterOperand)cmp.GetOperand(0)).Value;
            var shiftAmount = cmp.OperandCount > 2 ? ((Arm64ShiftOperand)cmp.GetOperand(2)).Amount : 0;
            if (site.ShiftAmount != shiftAmount) { reason = "CMP shift differs from static site metadata"; return false; }
            using var assembler = new Arm64Assembler((ulong)profile.MethodRva + site.Offset);
            if (register.Kind == Arm64RegisterKind.W) assembler.CMP((Arm64RegisterW)register, (ushort)target, default, shiftAmount);
            else assembler.CMP((Arm64RegisterX)register, (ushort)target, default, shiftAmount);
            assembler.End();
            if (assembler.Buffer.Length != 4) { reason = "replacement is not one instruction"; return false; }
            var word = BinaryPrimitives.ReadUInt32LittleEndian(assembler.Buffer);
            replacements.Add(new(site.Offset, expected, word, Instruction.Decode(word).ToString()));
        }
        plan = new(profile, new ReadOnlyCollection<CompareLimitReplacement>(replacements));
        reason = string.Empty;
        return true;
    }
}
