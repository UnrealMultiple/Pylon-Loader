using System.Buffers.Binary;
using AsmArm64;
using AsmInstruction = AsmArm64.Arm64Instruction;

namespace Pylon.ModLoader.Patching;

public readonly record struct Arm64AssemblyDiagnostic(string Code, string Message);

public readonly record struct Arm64PatchInstruction(
    ulong Address,
    uint Word,
    string Assembly,
    IReadOnlyList<Arm64AssemblyDiagnostic> Diagnostics);

/// <summary>
/// Strongly typed ARM64 patch planner. Native only receives the resulting plan
/// after CodeSite validation; callers do not hand-author instruction encodings.
/// </summary>
public sealed class Arm64PatchBuilder : IDisposable
{
    private readonly Arm64Assembler _assembler;
    private readonly ulong _baseAddress;
    private readonly List<(ulong Address, string Source)> _emitted = new();
    private readonly List<Arm64AssemblyDiagnostic> _diagnostics = new();
    private bool _ended;

    public Arm64PatchBuilder(ulong address)
    {
        if ((address & 3) != 0) throw new ArgumentException("ARM64 address must be aligned.", nameof(address));
        _baseAddress = address;
        _assembler = new Arm64Assembler(address);
    }

    public Arm64PatchBuilder B(ulong target) => Emit("b", () => _assembler.B(new Arm64Label(target)));
    public Arm64PatchBuilder BL(ulong target) => Emit("bl", () => _assembler.BL(new Arm64Label(target)));
    public Arm64PatchBuilder B(Arm64ConditionalKind condition, ulong target) =>
        Emit($"b.{condition.ToString().ToLowerInvariant()}", () => _assembler.B(condition, new Arm64Label(target)));
    public Arm64PatchBuilder ADR(Arm64RegisterX destination, ulong target) =>
        Emit($"adr {destination}, 0x{target:x}", () => _assembler.ADR(destination, new Arm64Label(target)));
    public Arm64PatchBuilder ADRP(Arm64RegisterX destination, ulong target) =>
        Emit($"adrp {destination}, 0x{target:x}", () => _assembler.ADRP(destination, new Arm64Label(target)));
    public Arm64PatchBuilder MOVZ(Arm64RegisterX destination, ushort immediate, int shift = 0) =>
        Emit($"movz {destination}, #{immediate}, lsl #{shift}", () => _assembler.MOVZ(destination, immediate, default, shift));
    public Arm64PatchBuilder MOV(Arm64RegisterX destination, Arm64RegisterX source) =>
        Emit($"mov {destination}, {source}", () => _assembler.MOV(destination, source));
    public Arm64PatchBuilder ADD(Arm64RegisterX destination, Arm64RegisterX left, Arm64RegisterX right) =>
        Emit($"add {destination}, {left}, {right}", () => _assembler.ADD(destination, left, right));
    public Arm64PatchBuilder ADD(Arm64RegisterX destination, Arm64RegisterX left, ushort immediate) =>
        Emit($"add {destination}, {left}, #{immediate}", () => _assembler.ADD(destination, left, immediate));
    public Arm64PatchBuilder SUB(Arm64RegisterX destination, Arm64RegisterX left, Arm64RegisterX right) =>
        Emit($"sub {destination}, {left}, {right}", () => _assembler.SUB(destination, left, right));
    public Arm64PatchBuilder CMP(Arm64RegisterX left, Arm64RegisterX right) =>
        Emit($"cmp {left}, {right}", () => _assembler.CMP(left, right));
    public Arm64PatchBuilder RET(Arm64RegisterX register = default) =>
        Emit("ret", () => _assembler.RET(register));
    public Arm64PatchBuilder NOP() => Emit("nop", () => _assembler.Nop());
    public Arm64PatchBuilder LDR(Arm64RegisterX destination, ulong literalAddress) =>
        Emit("ldr literal", () => _assembler.LDR(destination, new Arm64Label(literalAddress)));
    public Arm64PatchBuilder LDR(Arm64RegisterX destination, Arm64ImmediateMemoryAccessor source) =>
        Emit("ldr", () => _assembler.LDR(destination, source));
    public Arm64PatchBuilder CMP(Arm64RegisterW source, ushort immediate, int shiftAmount = 0) =>
        Emit("cmp immediate", () => _assembler.CMP(source, immediate, default, shiftAmount));
    public Arm64PatchBuilder CMP(Arm64RegisterX source, ushort immediate, int shiftAmount = 0) =>
        Emit("cmp immediate", () => _assembler.CMP(source, immediate, default, shiftAmount));
    public Arm64PatchBuilder CMP(Arm64RegisterXOrSP source, ushort immediate, int shiftAmount = 0) =>
        Emit("cmp immediate", () => _assembler.CMP(source, immediate, default, shiftAmount));
    public Arm64PatchBuilder MOVZ(Arm64RegisterW destination, ushort immediate) =>
        Emit("movz", () => _assembler.MOVZ(destination, immediate));
    public Arm64PatchBuilder SUB(Arm64RegisterX destination, Arm64RegisterX source, ushort immediate) =>
        Emit("sub immediate", () => _assembler.SUB(destination, source, immediate));

    public bool TryBuild(out IReadOnlyList<Arm64PatchInstruction> instructions,
        out IReadOnlyList<Arm64AssemblyDiagnostic> diagnostics)
    {
        var errors = new List<Arm64AssemblyDiagnostic>(_diagnostics);
        try
        {
            if (_ended) throw new InvalidOperationException("Builder already finalized.");
            if (errors.Count != 0) throw new InvalidOperationException("One or more instructions failed to assemble.");
            _assembler.End();
            _ended = true;
            var bytes = _assembler.Buffer;
            var result = new List<Arm64PatchInstruction>(_emitted.Count);
            for (var i = 0; i < _emitted.Count; i++)
            {
                var address = _emitted[i].Address;
                var word = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(i * 4, 4));
                var decoded = AsmInstruction.Decode(word);
                result.Add(new Arm64PatchInstruction(address, word, decoded.ToString(), Array.Empty<Arm64AssemblyDiagnostic>()));
            }
            instructions = result;
            diagnostics = errors;
            return true;
        }
        catch (Arm64AssemblerException exception)
        {
            foreach (var diagnostic in exception.Diagnostics)
                errors.Add(new("Assembler", diagnostic.ToString()));
        }
        catch (Exception exception)
        {
            errors.Add(new("Assembler", exception.Message));
        }
        instructions = Array.Empty<Arm64PatchInstruction>();
        diagnostics = errors;
        return false;
    }

    private Arm64PatchBuilder Emit(string source, Action emit)
    {
        if (_ended)
        {
            _diagnostics.Add(new("Lifecycle", "Builder already finalized."));
            return this;
        }
        try
        {
            var before = _assembler.SizeInBytes;
            emit();
            if (_assembler.SizeInBytes != before + 4)
            {
                _diagnostics.Add(new("InstructionSize", $"{source} did not emit exactly one instruction."));
                return this;
            }
            _emitted.Add((_baseAddress + (uint)before, source));
        }
        catch (Exception exception)
        {
            _diagnostics.Add(new("Assembler", $"{source}: {exception.Message}"));
        }
        return this;
    }

    public void Dispose() => _assembler.Dispose();
}
