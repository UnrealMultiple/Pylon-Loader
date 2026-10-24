using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Pylon.ModLoader;
using Pylon.ModLoader.Patching;

if (args.Length == 1 && args[0] == "--self-test") { SelfTest.Run(); return; }
if (args.Length == 2 && args[0] == "--verify-profile") { SelfTest.Verify(args[1]); return; }
if (args.Length != 2) throw new ArgumentException("Usage: dotnet run --project src/csharp/tools/limit-profile -- <repository-root> <output.json> | --self-test");
var root = Path.GetFullPath(args[0]);
var elfPath = Path.Combine(root, "refs/lib/arm64-v8a/libil2cpp.so");
var scriptPath = Path.Combine(root, "refs/script.json");
var dumpPath = Path.Combine(root, "refs/dump.cs");
var elf = new Elf(File.ReadAllBytes(elfPath));
using var script = JsonDocument.Parse(File.ReadAllText(scriptPath));
var scriptMethods = script.RootElement.GetProperty("ScriptMethod").EnumerateArray().ToArray();
var dump = File.ReadAllText(dumpPath);
var methods = new List<object>();
var failures = new List<string>();
foreach (var target in Targets.All)
{
    var name = target.Type + "$$" + target.Method;
    var candidates = scriptMethods.Where(m => m.GetProperty("Name").GetString() == name).ToArray();
    // Cross-check metadata RVA against a dump declaration with exactly the legacy argument count.
    var dumpRvas = Regex.Matches(dump, @"// RVA: 0x([0-9A-Fa-f]+)[^\r\n]*\r?\n[^\r\n]*\b" + Regex.Escape(target.Method) + @"\(([^\r\n]*)\)")
        .Where(m => CountArgs(m.Groups[2].Value) == target.Argc)
        .Select(m => Convert.ToUInt32(m.Groups[1].Value, 16)).ToHashSet();
    candidates = candidates.Where(m => dumpRvas.Contains(m.GetProperty("Address").GetUInt32())).ToArray();
    if (candidates.Length != 1) { failures.Add(name + ": metadata/dump RVA is ambiguous or missing"); continue; }
    var rva = candidates[0].GetProperty("Address").GetUInt32();
    var size = elf.MethodSize(rva);
    if (size == 0 || (size & 3) != 0) { failures.Add(name + ": no exact supported ELF FDE"); continue; }
    var expected = elf.Read(rva, checked((int)size));
    var sites = new List<object>();
    for (var offset = 0; offset <= expected.Length - 16; offset += 4)
    {
        if (!CompareLimitPolicy.IsCandidate(expected, offset, target.Old, target.Below, out _)) continue;
        var cmp = AsmArm64.Arm64Instruction.Decode(BinaryPrimitives.ReadUInt32LittleEndian(expected.AsSpan(offset, 4)));
        var shiftAmount = cmp.OperandCount > 2 ? ((AsmArm64.Arm64ShiftOperand)cmp.GetOperand(2)).Amount : 0;
        sites.Add(new { offset, oldLimit = target.Old, totalOffset = target.Delta, allowBelow = target.Below, shiftAmount,
            expectedFingerprint = Convert.ToHexString(expected.AsSpan(offset, 16)).ToLowerInvariant() });
    }
    if (sites.Count == 0) failures.Add(name + ": no supported CMP + conditional consumer within three NZCV-preserving instructions; legacy remains required");
    methods.Add(new { method = name, argc = target.Argc, methodRva = rva, methodSize = size,
        expectedMethod = Convert.ToHexString(expected).ToLowerInvariant(), sites });
}
var output = new
{
    schema = 1, profile = "terraria-1.4.5.8.6-arm64-v8a", complete = failures.Count == 0,
    provenance = new { source = "refs/README.md:3 explicit local development baseline", elf = "refs/lib/arm64-v8a/libil2cpp.so",
        elfSha256 = Convert.ToHexString(SHA256.HashData(elf.Bytes)).ToLowerInvariant(), buildId = elf.BuildId,
        scriptSha256 = Hash(scriptPath), dumpSha256 = Hash(dumpPath), boundary = "ELF .eh_frame_hdr indexed FDE, verified exact method start",
        decoder = "AsmArm64 2.4.0", generatedUtc = DateTimeOffset.UtcNow },
    methods, failures
};
File.WriteAllText(args[1], JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine($"Generated {methods.Count}/20 methods; {failures.Count} unsupported. ELF BuildID={elf.BuildId}");
foreach (var failure in failures) Console.WriteLine(failure);
static int CountArgs(string args) => string.IsNullOrWhiteSpace(args) ? 0 : args.Split(',').Length;
static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

sealed record Target(string Type, string Method, int Argc, uint Old = 753, int Delta = -1, bool Below = false);
static class Targets
{
    public static readonly Target[] All =
    [
        new("Terraria.WorldGen", "PlaceTile", 7), new("Terraria.WorldGen", "PlaceObject", 8),
        new("Terraria.WorldGen", "KillTile", 5), new("Terraria.WorldGen", "KillWall", 3),
        new("Terraria.WorldGen", "TileFrameImportant", 5), new("Terraria.WorldGen", "WouldTileReplacementWork", 3),
        new("Terraria.WorldGen", "CheckPile", 2), new("Terraria.WorldGen", "Check2x1", 3),
        new("Terraria.WorldGen", "Check2x2Style", 3), new("Terraria.WorldGen", "placeTrap", 3),
        new("Terraria.WorldGen", "BlockBelowMakesSandConvertIntoHardenedSand", 2), new("Terraria.DelegateMethods", "SpreadTile", 6),
        new("Terraria.Projectile", "AI_006", 0), new("Terraria.Player", "PlaceThing_Tiles_CheckRopeUsability", 1),
        new("Terraria.GameContent.SmartCursorHelper", "Step_Boulders", 3), new("Terraria.GameContent.SmartCursorHelper", "Step_PumpkinSeeds", 3),
        new("Terraria.WorldGen", "TileFrameCosmetic", 3, 754, 0, true), new("Terraria.WorldGen", "ValidateTypes", 0, 754, 0, true),
        new("Terraria.Collision", "IsWorldPointSolid", 2, 754, 0), new("Terraria.TileData", "GetTileDefinition", 8, 754, 0)
    ];
}

// Small strict ELF64 LE reader. Unsupported encodings fail instead of guessing a boundary.
sealed class Elf
{
    public byte[] Bytes { get; }
    readonly List<(ulong Va, ulong Offset, ulong Size)> segments = new();
    readonly Dictionary<string, (ulong Va, ulong Offset, ulong Size)> sections = new();
    public string BuildId { get; }
    uint U32(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(Bytes.AsSpan(offset, 4));
    ulong U64(int offset) => BinaryPrimitives.ReadUInt64LittleEndian(Bytes.AsSpan(offset, 8));
    ushort U16(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(Bytes.AsSpan(offset, 2));
    int I32(int offset) => BinaryPrimitives.ReadInt32LittleEndian(Bytes.AsSpan(offset, 4));
    public Elf(byte[] bytes)
    {
        Bytes = bytes;
        if (bytes.Length < 64 || !bytes.AsSpan(0, 6).SequenceEqual(new byte[] { 0x7f, 69, 76, 70, 2, 1 }) || U16(18) != 183)
            throw new InvalidDataException("Expected AArch64 ELF64 little endian");
        var ph = checked((int)U64(32));
        for (var i = 0; i < U16(56); i++)
        { var p = ph + i * U16(54); if (U32(p) == 1) segments.Add((U64(p + 16), U64(p + 8), U64(p + 32))); }
        var sh = checked((int)U64(40)); var stride = U16(58);
        var names = checked((int)U64(sh + U16(62) * stride + 24));
        for (var i = 0; i < U16(60); i++)
        {
            var p = sh + i * stride; var start = checked(names + (int)U32(p));
            var end = Array.IndexOf(bytes, (byte)0, start);
            var name = Encoding.ASCII.GetString(bytes, start, end - start);
            sections[name] = (U64(p + 16), U64(p + 24), U64(p + 32));
        }
        var note = sections[".note.gnu.build-id"]; var n = checked((int)note.Offset);
        if (U32(n + 8) != 3 || U32(n) != 4 || Encoding.ASCII.GetString(bytes, n + 12, 3) != "GNU")
            throw new InvalidDataException("Missing GNU BuildID");
        BuildId = Convert.ToHexString(bytes.AsSpan(n + 16, checked((int)U32(n + 4)))).ToLowerInvariant();
    }
    int FileOffset(ulong va)
    {
        foreach (var s in segments) if (va >= s.Va && va - s.Va < s.Size) return checked((int)(s.Offset + va - s.Va));
        throw new InvalidDataException($"Unmapped ELF VA {va:x}");
    }
    public byte[] Read(ulong va, int size)
    {
        if (size <= 0 || FileOffset(va + (uint)size - 1) != FileOffset(va) + size - 1) throw new InvalidDataException("Non-contiguous method");
        return Bytes.AsSpan(FileOffset(va), size).ToArray();
    }
    public uint MethodSize(uint start)
    {
        var hdr = sections[".eh_frame_hdr"]; var h = checked((int)hdr.Offset);
        if (Bytes[h] != 1 || Bytes[h + 1] != 0x1b || Bytes[h + 2] != 3 || Bytes[h + 3] != 0x3b)
            throw new InvalidDataException("Unsupported .eh_frame_hdr encoding");
        var count = U32(h + 8);
        for (var i = 0; i < count; i++)
        {
            var entry = checked(h + 12 + (int)i * 8);
            var initial = checked((ulong)((long)hdr.Va + I32(entry)));
            if (initial != start) continue;
            var fdeVa = checked((ulong)((long)hdr.Va + I32(entry + 4))); var f = FileOffset(fdeVa);
            var length = U32(f); if (length < 12 || length == uint.MaxValue) return 0;
            var cie = f + 4 - checked((int)U32(f + 4));
            if (U32(cie + 4) != 0 || Bytes[cie + 8] != 1) return 0;
            var p = cie + 9; var end = Array.IndexOf(Bytes, (byte)0, p);
            var augmentation = Encoding.ASCII.GetString(Bytes, p, end - p); p = end + 1;
            if (augmentation != "zR" && augmentation != "zPLR") return 0;
            Leb(ref p); Leb(ref p); Leb(ref p); var augmentationLength = Leb(ref p);
            var encodingPosition = checked(p + (int)augmentationLength - 1);
            // Both supported augmentation strings end with R (FDE pointer encoding).
            if (Bytes[encodingPosition] != 0x1b) return 0;
            var decodedStart = checked((ulong)((long)fdeVa + 8 + I32(f + 8)));
            if (decodedStart != start) return 0;
            return U32(f + 12);
        }
        return 0;
    }
    ulong Leb(ref int p)
    {
        ulong value = 0; var shift = 0;
        while (shift < 64) { var b = Bytes[p++]; value |= (ulong)(b & 127) << shift; if ((b & 128) == 0) return value; shift += 7; }
        throw new InvalidDataException("Invalid LEB128");
    }
}

static class SelfTest
{
    public static void Run()
    {
        using var a = new AsmArm64.Arm64Assembler(0x1000);
        a.CMP(AsmArm64.Arm64Factory.W0, 753); a.B(AsmArm64.Arm64ConditionalKind.HI, new AsmArm64.Arm64Label(0x1010)); a.Nop(); a.Nop(); a.End();
        var original = a.Buffer.ToArray();
        var site = new CompareLimitSite(0, 753, -1, false, original);
        var profile = new CompareLimitMethodProfile("test", 0x1000, original, new[] { site });
        Array.Clear(original);
        if (!CompareLimitPolicy.Plan(profile, 800, out var plan, out var reason) || plan!.Replacements.Count != 1) throw new Exception(reason);
        if (CompareLimitPolicy.Plan(profile, 5000, out _, out _)) throw new Exception("Oversized immediate accepted");
        var bad = new CompareLimitMethodProfile("test", 0x1000, profile.ExpectedMethod, new[] { new CompareLimitSite(0, 753, -1, false, new byte[16]) });
        if (CompareLimitPolicy.Plan(bad, 800, out _, out _)) throw new Exception("Fingerprint mismatch accepted");
        foreach (var condition in Enum.GetValues<AsmArm64.Arm64ConditionalKind>())
        {
            if ((int)condition > 15) continue;
            using var c = new AsmArm64.Arm64Assembler(0x1000);
            c.CMP(AsmArm64.Arm64Factory.W1, 754); c.B(condition, new AsmArm64.Arm64Label(0x1010)); c.Nop(); c.Nop(); c.End();
            var data = c.Buffer.ToArray();
            var baseAllowed = condition is AsmArm64.Arm64ConditionalKind.HI or AsmArm64.Arm64ConditionalKind.LS or AsmArm64.Arm64ConditionalKind.GT or AsmArm64.Arm64ConditionalKind.LE;
            var belowAllowed = baseAllowed || condition is AsmArm64.Arm64ConditionalKind.HS or AsmArm64.Arm64ConditionalKind.LO or AsmArm64.Arm64ConditionalKind.GE or AsmArm64.Arm64ConditionalKind.LT;
            if (CompareLimitPolicy.IsCandidate(data, 0, 754, false, out _) != baseAllowed ||
                CompareLimitPolicy.IsCandidate(data, 0, 754, true, out _) != belowAllowed) throw new Exception("Wrong condition policy: " + condition);
            data.AsSpan(4, 4).Clear();
            if (CompareLimitPolicy.IsCandidate(data, 0, 754, true, out _)) throw new Exception("Invalid consumer accepted");
        }
        foreach (var wide in new[] { false, true })
        {
            using var shifted = new AsmArm64.Arm64Assembler(0x1000);
            if (wide) shifted.CMP(AsmArm64.Arm64Factory.X1, 754, default, 12);
            else shifted.CMP(AsmArm64.Arm64Factory.W1, 754, default, 12);
            shifted.B(AsmArm64.Arm64ConditionalKind.HI, new AsmArm64.Arm64Label(0x1010));
            shifted.Nop(); shifted.Nop(); shifted.End();
            var bytes = shifted.Buffer.ToArray();
            if (!CompareLimitPolicy.IsCandidate(bytes, 0, 754, false, out _)) throw new Exception("Shifted CMP rejected");
            var shiftedSite = new CompareLimitSite(0, 754, 0, false, bytes, 12);
            var shiftedProfile = new CompareLimitMethodProfile("shifted", 0x1000, bytes, new[] { shiftedSite });
            if (!CompareLimitPolicy.Plan(shiftedProfile, 800, out var shiftedPlan, out reason)) throw new Exception(reason);
            var decoded = AsmArm64.Arm64Instruction.Decode(shiftedPlan!.Replacements.Single().Replacement);
            if (((AsmArm64.Arm64ImmediateOperand)decoded.GetOperand(1)).Value != 800 ||
                ((AsmArm64.Arm64ShiftOperand)decoded.GetOperand(2)).Amount != 12 ||
                ((AsmArm64.Arm64RegisterOperand)decoded.GetOperand(0)).Value.Kind != (wide ? AsmArm64.Arm64RegisterKind.X : AsmArm64.Arm64RegisterKind.W))
                throw new Exception("Shifted CMP replacement changed shift, width, or immediate");
            var wrongShift = new CompareLimitMethodProfile("shift mismatch", 0x1000, bytes,
                new[] { new CompareLimitSite(0, 754, 0, false, bytes) });
            if (CompareLimitPolicy.Plan(wrongShift, 800, out _, out _)) throw new Exception("Shift metadata mismatch accepted");
            if (CompareLimitPolicy.Plan(shiftedProfile, 4096, out _, out _)) throw new Exception("Oversized shifted immediate accepted");
        }
        var source = profile.ExpectedMethod.ToArray();
        for (var gap = 0; gap <= 4; gap++)
        {
            var bytes = new byte[Math.Max(16, (gap + 2) * 4)];
            source.AsSpan(0, 4).CopyTo(bytes);
            for (var i = 0; i < gap; i++)
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4 + i * 4), i % 2 == 0 ? 0xa90007e0u : 0xb90003e0u); // STP / STR
            source.AsSpan(4, 4).CopyTo(bytes.AsSpan((gap + 1) * 4));
            if (CompareLimitPolicy.IsCandidate(bytes, 0, 753, false, out _) != (gap <= 3)) throw new Exception("Wrong intervening instruction bound");
            if (gap == 0 || gap > 3) continue;
            var gapProfile = new CompareLimitMethodProfile("stores", 0x1000, bytes,
                new[] { new CompareLimitSite(0, 753, -1, false, bytes.AsSpan(0, 16).ToArray()) });
            if (!CompareLimitPolicy.Plan(gapProfile, 800, out _, out reason)) throw new Exception(reason);
            source.AsSpan(0, 4).CopyTo(bytes.AsSpan(4)); // A second CMP overwrites NZCV.
            if (CompareLimitPolicy.IsCandidate(bytes, 0, 753, false, out _)) throw new Exception("Intervening NZCV writer accepted");
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 0x14000001); // Unconditional B is not a preserving gap.
            if (CompareLimitPolicy.IsCandidate(bytes, 0, 753, false, out _)) throw new Exception("Intervening control flow accepted");
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 0xd503201f); // NOP
            if (!CompareLimitPolicy.IsCandidate(bytes, 0, 753, false, out _)) throw new Exception("NZCV-preserving NOP rejected");
        }
        Console.WriteLine("PASS immutable expected, typed CMP replacement, fingerprint mismatch, immediate bounds, all conditions, invalid consumer, W/X shifted CMP, shift metadata, 0-3 preserving instructions, NZCV writer/control-flow rejection");
    }
    public static void Verify(string path)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var count = 0; var sites = 0;
        foreach (var method in json.RootElement.GetProperty("methods").EnumerateArray())
        {
            var expected = Convert.FromHexString(method.GetProperty("expectedMethod").GetString()!);
            if (expected.Length != method.GetProperty("methodSize").GetInt32()) throw new Exception("Method size mismatch");
            var entries = method.GetProperty("sites").EnumerateArray().Select(s => new CompareLimitSite(
                s.GetProperty("offset").GetUInt32(), s.GetProperty("oldLimit").GetUInt32(), s.GetProperty("totalOffset").GetInt32(),
                s.GetProperty("allowBelow").GetBoolean(), Convert.FromHexString(s.GetProperty("expectedFingerprint").GetString()!),
                s.GetProperty("shiftAmount").GetInt32())).ToArray();
            if (entries.Length == 0) continue;
            var profile = new CompareLimitMethodProfile(method.GetProperty("method").GetString()!, method.GetProperty("methodRva").GetUInt32(), expected, entries);
            if (!CompareLimitPolicy.Plan(profile, 800, out var plan, out var reason)) throw new Exception(profile.Method + ": " + reason);
            count++; sites += plan!.Replacements.Count;
        }
        if (!json.RootElement.GetProperty("complete").GetBoolean() || json.RootElement.GetProperty("failures").GetArrayLength() != 0 ||
            count != 20 || sites != 21) throw new Exception("Unexpected profile coverage; review reference changes explicitly");
        Console.WriteLine($"PASS static profile: {count}/20 supported methods, {sites} validated 16-byte sites; no unsupported methods");
    }
}
