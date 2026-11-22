using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Metadata.Tables;
using Il2CppInterop.Generator;
using Il2CppInterop.Generator.Contexts;
using Il2CppInterop.Generator.MetadataAccess;
using Il2CppInterop.Generator.Passes;

var root = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
VerifyGenerated.Run(root);
using var metadata = new AssemblyMetadataAccess(Directory.GetFiles(Path.Combine(root, "refs/DummyDll"), "*.dll"));
using var context = new RewriteGlobalContext(new GeneratorOptions { InlineArrayNativeImage = Path.Combine(root, ".work/apk-extract/extracted/lib/arm64-v8a/libil2cpp.so") }, metadata, NullMetadataAccess.Instance);
Pass05CreateRenameGroups.DoPass(context);
Pass10CreateTypedefs.DoPass(context);
Pass11ComputeTypeSpecifics.DoPass(context);
Pass12FillTypedefs.DoPass(context);
Pass15GenerateMemberContexts.DoPass(context);
Pass21GenerateValueTypeFields.DoPass(context);
Pass39GenerateStructViews.DoPass(context);
var game = context.GetAssemblyByName("Assembly-CSharp");
foreach (var name in new[] { "Float_FixedArray_2", "Float_FixedArray_3", "Vector2_DynamicArray_120", "Bool_DynamicArray_255" })
{
    var type = game.GetTypeByName(name);
    var view = context.TryGetStructView(type.OriginalType.ToTypeSignature()) ?? throw new Exception("Missing view: " + name);
    if (view.Methods.Single(m => m.Name == "get_Item").Signature!.ReturnType is not ByReferenceTypeSignature)
        throw new Exception("Not a ref indexer: " + name);
    Console.WriteLine("PASS metadata/ref signature: " + name);
}
// No native proof means copy semantics, even when every metadata/layout heuristic matches.
using (var noProof = new RewriteGlobalContext(new GeneratorOptions(), metadata, NullMetadataAccess.Instance))
{
    Pass05CreateRenameGroups.DoPass(noProof); Pass10CreateTypedefs.DoPass(noProof); Pass11ComputeTypeSpecifics.DoPass(noProof);
    Pass12FillTypedefs.DoPass(noProof); Pass15GenerateMemberContexts.DoPass(noProof); Pass39GenerateStructViews.DoPass(noProof);
    if (noProof.TryGetStructView(game.GetTypeByName("Float_FixedArray_3").OriginalType.ToTypeSignature()) is not null)
        throw new Exception("Unproven native layout accepted");
    Console.WriteLine("PASS missing native evidence fallback");
}
if (context.TryGetStructView(game.GetTypeByName("IntArray_10").OriginalType.ToTypeSignature()) is not null)
    throw new Exception("Unrecognized native accessor accepted");
Console.WriteLine("PASS unrecognized native accessor fallback");
// Reject non-blittable storage even when an indexer contract is present.
var badStorage = game.GetTypeByName("Float_FixedArray_2").OriginalType;
var savedSignature = badStorage.Fields.First(f => !f.IsStatic).Signature;
badStorage.Fields.First(f => !f.IsStatic).Signature = new FieldSignature(badStorage.DeclaringModule!.CorLibTypeFactory.String);
// Ordinary homogeneous structs, even with deceptive storage names, must not become arrays.
var fake = game.GetTypeByName("Float_FixedArray_3");
var original = fake.OriginalType;
original.Properties.Clear();
var fresh = new RewriteGlobalContext(new GeneratorOptions { InlineArrayNativeImage = Path.Combine(root, ".work/apk-extract/extracted/lib/arm64-v8a/libil2cpp.so") }, metadata, NullMetadataAccess.Instance);
Pass05CreateRenameGroups.DoPass(fresh); Pass10CreateTypedefs.DoPass(fresh); Pass11ComputeTypeSpecifics.DoPass(fresh);
Pass12FillTypedefs.DoPass(fresh); Pass15GenerateMemberContexts.DoPass(fresh); Pass39GenerateStructViews.DoPass(fresh);
if (fresh.TryGetStructView(original.ToTypeSignature()) is not null) throw new Exception("False positive val0 struct");
Console.WriteLine("PASS ordinary val0/val1/val2 struct fallback");
if (fresh.TryGetStructView(badStorage.ToTypeSignature()) is not null) throw new Exception("Non-blittable live view");
Console.WriteLine("PASS non-blittable array fallback");
badStorage.Fields.First(f => !f.IsStatic).Signature = savedSignature;
// Export only the actual generated view bodies and Vector2 storage, without native cctors.
// This permits host execution of the emitted indexer IL against controlled unmanaged memory.
var asm = new AssemblyDefinition("ArrayViewFixture", new Version(1,0));
var module = new ModuleDefinition("ArrayViewFixture.dll"); asm.Modules.Add(module);
var vector = game.GetTypeByName("Microsoft.Xna.Framework.Vector2").NewType;
var vectorFixture = new TypeDefinition("Microsoft.Xna.Framework", "Vector2", TypeAttributes.Public | TypeAttributes.ExplicitLayout | TypeAttributes.Sealed,
    new TypeReference(module, new AssemblyReference("System.Runtime", new Version(10,0,0,0)) { PublicKeyOrToken = Convert.FromHexString("B03F5F7F11D50A3A") }, "System", "ValueType"));
foreach (var f in vector.Fields.Where(f => !f.IsStatic))
    vectorFixture.Fields.Add(new FieldDefinition(f.Name, FieldAttributes.Public, module.DefaultImporter.ImportTypeSignature(typeof(float))) { FieldOffset = f.FieldOffset });
module.TopLevelTypes.Add(vectorFixture);
foreach (var name in new[] { "Float_FixedArray_3", "Vector2_DynamicArray_120" })
{
    var source = game.GetTypeByName(name).NewType;
    var view = source.NestedTypes.Single(t => t.Name == "View");
    source.NestedTypes.Remove(view);
    view.BaseType = vectorFixture.BaseType;
    view.Name = name + "View";
    view.Attributes = TypeAttributes.Public | TypeAttributes.SequentialLayout | TypeAttributes.Sealed;
    view.Fields.Single(f => f.Name == "_owner").Signature = new FieldSignature(module.CorLibTypeFactory.Object);
    // Rebind self member refs and the Vector2 return type into the isolated fixture.
    foreach (var method in view.Methods)
    {
        if (method.Signature!.ReturnType is ByReferenceTypeSignature br && br.BaseType.FullName.Contains("Vector2"))
            method.Signature.ReturnType = new ByReferenceTypeSignature(vectorFixture.ToTypeSignature());
        foreach (var instruction in method.CilMethodBody!.Instructions)
            if (instruction.Operand is MemberReference member && (member.Name == "_p" || member.Name == "get_Length"))
                instruction.Operand = new MemberReference(view, member.Name, (MemberSignature)member.Signature!);
            else if (instruction.Operand is MethodSpecification spec && spec.Method?.Name == "AsRef" &&
                     name == "Vector2_DynamicArray_120")
                spec.Signature!.TypeArguments[0] = vectorFixture.ToTypeSignature();
    }
    foreach (var prop in view.Properties)
        if (prop.Signature!.ReturnType is ByReferenceTypeSignature br && br.BaseType.FullName.Contains("Vector2"))
            prop.Signature.ReturnType = new ByReferenceTypeSignature(vectorFixture.ToTypeSignature());
    module.TopLevelTypes.Add(view);
}
var output = Path.Combine(root, ".work/array-audit/ArrayViewFixture.dll");
asm.Write(output);
Console.WriteLine("Wrote generated-IL execution fixture: " + output);
